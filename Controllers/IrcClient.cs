using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch のチャット（IRC over WebSocket）につなぎ、メッセージを受け取り・送るクライアント。TwitchLib.Client の代わりに、ClientWebSocket で直接つなぐ。
/// つなぎ方: <c>CAP REQ</c>（タグ・コマンド・メンバーシップ）→ <c>PASS</c> → <c>NICK</c> → <c>JOIN</c>。<c>PING</c> には <c>PONG</c> で答える。
/// 切れたら、間隔を空けて、つなぎ直す（認証の失敗のときは、やり直しても同じなので、つなぎ直さない）。
/// 送るメッセージは、改行を除き、500 文字までにして、30 秒に 90 件まで（配信者・モデレーターの上限 100 件の内側）に間隔を空けて送る。
/// </summary>
internal sealed class IrcClient : IDisposable
{
    /// <summary>
    /// Twitch のチャットの WebSocket の URL。
    /// </summary>
    internal const string DefaultUrl = "wss://irc-ws.chat.twitch.tv:443";

    /// <summary>
    /// 1 メッセージの最大の文字数（Twitch の上限）。
    /// </summary>
    internal const int MaxMessageLength = 500;

    /// <summary>
    /// 送れる件数（30 秒あたり）。配信者・モデレーターの上限（100 件）より少なくして、取りこぼしを防ぐ。
    /// </summary>
    internal const int MaxMessagesPerWindow = 90;

    private static readonly TimeSpan SendWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// この時間、何も届かなければ、切れたものとする。Twitch は、約 5 分ごとに PING を送ってくる。
    /// </summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(7);

    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly string _url;
    private readonly string _nick;
    private readonly string _pass;
    private readonly string _channel;
    private readonly Queue<DateTime> _sentAt = new();
    private Channel<string> _outbox = Channel.CreateUnbounded<string>();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private TaskCompletionSource<bool>? _firstReady;
    private volatile bool _authFailed;

    /// <summary>
    /// テストで、通信の代わりに、書き込んだ行を受け取るための差し替え。
    /// </summary>
    internal Func<string, Task>? WriteOverride { get; set; }

    private Func<string, Task>? _write;

    /// <summary>
    /// ログイン済みの状態か（Twitch が 001 を返してから、切れるまで）。
    /// </summary>
    internal bool IsConnected { get; private set; }

    /// <summary>
    /// 受け取った行・送った行（ログ用。PASS の行は含まない）。
    /// </summary>
    internal event Action<string>? Log;

    /// <summary>
    /// ログインできたとき。
    /// </summary>
    internal event Func<Task>? Connected;

    /// <summary>
    /// 切れたとき。
    /// </summary>
    internal event Func<Task>? Disconnected;

    /// <summary>
    /// 自分がチャンネルに参加できたとき（チャンネル名。# は付かない）。
    /// </summary>
    internal event Func<string, Task>? Joined;

    /// <summary>
    /// ほかのユーザーがチャンネルに参加したとき（ログイン名）。
    /// </summary>
    internal event Func<string, Task>? UserJoined;

    /// <summary>
    /// チャットのメッセージを受け取ったとき。
    /// </summary>
    internal event Func<ChatMessageEvent, Task>? MessageReceived;

    /// <summary>
    /// サブスク・Raid・お知らせなどを受け取ったとき。
    /// </summary>
    internal event Func<UserNoticeEvent, Task>? UserNoticeReceived;

    /// <summary>
    /// メッセージのエモートの名前を学んだものを、取り除くための道具。
    /// </summary>
    internal EmoteFilter Emotes { get; } = new();

    /// <summary>
    /// クライアントを作ります。
    /// </summary>
    /// <param name="nick">ログイン名（小文字）</param>
    /// <param name="pass">PASS の値（<c>oauth:</c> つきのトークン）</param>
    /// <param name="channel">参加するチャンネル（# は付けない。小文字）</param>
    /// <param name="url">つなぐ URL（省略時は Twitch）</param>
    internal IrcClient(string nick, string pass, string channel, string url = DefaultUrl)
    {
        _nick = nick.ToLowerInvariant();
        _pass = pass;
        _channel = channel.ToLowerInvariant();
        _url = url;
    }

    /// <summary>
    /// つなぎます。ログインできるまで待ちます（待ち切れなければ失敗）。そのあとの切断は、裏でつなぎ直します。
    /// </summary>
    internal async Task ConnectAsync()
    {
        if (_runTask is not null) return;

        _cts = new CancellationTokenSource();
        _authFailed = false;
        _firstReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _cts.Token;
        _runTask = Task.Run(() => RunAsync(token), token);

        try
        {
            await _firstReady.Task.WaitAsync(FirstConnectTimeout);
        }
        catch (TimeoutException)
        {
            await DisconnectAsync();
            throw new HttpRequestException("Twitch のチャットに接続できませんでした（時間切れ）。");
        }
    }

    /// <summary>
    /// 切ります（つなぎ直しも止める）。送りかけのメッセージは、少しだけ待って送ってから切ります。
    /// </summary>
    internal async Task DisconnectAsync()
    {
        _outbox.Writer.TryComplete();
        if (_runTask is not null)
        {
            try
            {
                // 送りかけのメッセージ（Logout の知らせなど）を送り切るための、短い猶予
                await _runTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (TimeoutException)
            {
                // 間に合わなければ、そのまま切る
            }
        }

        if (_cts is not null) await _cts.CancelAsync();
        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
                // 切断で止めたので、正常
            }
        }

        _runTask = null;
        IsConnected = false;
    }

    /// <summary>
    /// チャンネルへメッセージを送ります（送る順に並べて、間隔を空けて送る）。改行は空白にし、500 文字までにします。
    /// つながっていないときは、何もしません。
    /// </summary>
    /// <param name="text">メッセージ</param>
    /// <returns>送る列に入れたか</returns>
    internal bool SendMessage(string text)
    {
        if (!IsConnected) return false;

        var line = $"PRIVMSG #{_channel} :{SanitizeMessage(text)}";
        return _outbox.Writer.TryWrite(line);
    }

    /// <summary>
    /// 送るメッセージから、IRC の命令を混ぜられる改行などを除き、上限の文字数に収めます。
    /// </summary>
    /// <param name="text">メッセージ</param>
    internal static string SanitizeMessage(string text)
    {
        var flat = text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
        return flat.Length <= MaxMessageLength ? flat : flat[..MaxMessageLength];
    }

    /// <summary>
    /// 送れる件数の上限を超えないよう、次に送れる時刻までの待ち時間を返します（すぐ送れるなら 0）。
    /// 送った時刻の記録は、この呼び出しで古いものを捨てます。
    /// </summary>
    /// <param name="sentAt">この窓の中で送った時刻（古い順）</param>
    /// <param name="now">今の時刻</param>
    internal static TimeSpan WaitBeforeNextSend(Queue<DateTime> sentAt, DateTime now)
    {
        while (sentAt.Count > 0 && now - sentAt.Peek() >= SendWindow) sentAt.Dequeue();
        if (sentAt.Count < MaxMessagesPerWindow) return TimeSpan.Zero;

        return sentAt.Peek() + SendWindow - now;
    }

    /// <summary>
    /// 後始末をします。
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    /// <summary>
    /// つなぐ・受け取る・切れたらつなぎ直す、の繰り返し。止めるまで（または認証に失敗するまで）続く。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "通信の最上位。どんな失敗でも、ログに残して、つなぎ直す（受信が止まったままにしない）")]
    private async Task RunAsync(CancellationToken token)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!token.IsCancellationRequested && !_authFailed)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(_url), token);
                await SessionAsync(socket, () => delay = TimeSpan.FromSeconds(1), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RaiseLog($"IRC connection error: {ex.GetType().Name}");
            }

            _write = null;
            if (IsConnected)
            {
                IsConnected = false;
                await RaiseAsync(Disconnected, "Disconnected");
            }

            if (token.IsCancellationRequested || _authFailed || _outbox.Reader.Completion.IsCompleted) break;

            RaiseLog($"IRC reconnecting in {(int) delay.TotalSeconds}s");
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
        }

        _write = null;
        if (IsConnected)
        {
            IsConnected = false;
            await RaiseAsync(Disconnected, "Disconnected");
        }
    }

    /// <summary>
    /// 1 回の接続の間の処理。ログインの手続きをして、受信と送信を続ける。切れたら（または Twitch が切り直しを求めたら）戻る。
    /// </summary>
    private async Task SessionAsync(ClientWebSocket socket, Action onLoggedIn, CancellationToken token)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task WriteAsync(string line)
        {
            if (!line.StartsWith("PASS ", StringComparison.Ordinal)) RaiseLog($"{_nick} - Writing: {line}");
            await socket.SendAsync(Encoding.UTF8.GetBytes(line + "\r\n"), WebSocketMessageType.Text, true, sessionCts.Token);
        }

        _write = WriteAsync;
        await WriteAsync("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");
        await WriteAsync($"PASS {_pass}");
        await WriteAsync($"NICK {_nick}");
        await WriteAsync($"JOIN #{_channel}");

        var senderTask = SendLoopAsync(WriteAsync, sessionCts, token);
        try
        {
            while (true)
            {
                var text = await ReceiveTextAsync(socket, sessionCts.Token);
                if (text is null) return;

                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = line.TrimEnd('\r');
                    if (trimmed.Length == 0) continue;

                    RaiseLog($"{_nick} - Received: {trimmed}");
                    if (await HandleLineAsync(trimmed, onLoggedIn)) return;
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // 送る列が空になって、送り終えたので、この接続を閉じる（切断の指示のとき）
            return;
        }
        finally
        {
            await sessionCts.CancelAsync();
            try
            {
                await senderTask;
            }
            catch (OperationCanceledException)
            {
                // 接続を閉じたので、正常
            }
        }
    }

    /// <summary>
    /// 受け取った 1 行を処理します。PING には PONG を返し、種類に合ったイベントを起こします。
    /// </summary>
    /// <param name="line">1 行</param>
    /// <param name="onLoggedIn">ログインできたときに呼ぶ（つなぎ直しの間隔を、元に戻すため）</param>
    /// <returns>この接続を閉じて、つなぎ直すべきか（Twitch の RECONNECT・認証の失敗）</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "受信の最上位。読めない行 1 件で、受信を止めない。ログに残して続ける")]
    internal async Task<bool> HandleLineAsync(string line, Action? onLoggedIn = null)
    {
        IrcMessage? message;
        try
        {
            message = IrcParser.Parse(line);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            RaiseLog("IRC: 読めない行を受け取りました（捨てます）");
            return false;
        }

        if (message is null) return false;

        var write = WriteOverride ?? _write;
        switch (message.Command)
        {
            case "PING":
                if (write is not null) await write($"PONG :{message.Trailing ?? "tmi.twitch.tv"}");
                break;
            case "RECONNECT":
                RaiseLog("IRC: reconnect requested by Twitch");
                return true;
            case "001":
                IsConnected = true;
                onLoggedIn?.Invoke();
                _firstReady?.TrySetResult(true);
                await RaiseAsync(Connected, "Connected");
                break;
            case "NOTICE" when IsAuthFailure(message.Trailing):
                _authFailed = true;
                RaiseLog($"<Error> Twitch のチャットの認証に失敗しました: {message.Trailing}");
                _firstReady?.TrySetException(new HttpRequestException("Twitch のチャットの認証に失敗しました。Authorize からもう一度認証してください。"));
                return true;
            case "JOIN":
                await HandleJoinAsync(message);
                break;
            case "PRIVMSG":
                if (IrcEvents.ToChatMessage(message) is { } chat)
                {
                    Emotes.Learn(chat.EmoteNames);
                    await RaiseAsync(MessageReceived, chat, "MessageReceived");
                }

                break;
            case "USERNOTICE":
                if (IrcEvents.ToUserNotice(message) is { Kind: not UserNoticeKind.Other } notice)
                    await RaiseAsync(UserNoticeReceived, notice, "UserNoticeReceived");
                break;
        }

        return false;
    }

    private async Task HandleJoinAsync(IrcMessage message)
    {
        if (message.Nick is null) return;

        if (string.Equals(message.Nick, _nick, StringComparison.OrdinalIgnoreCase))
            await RaiseAsync(Joined, (message.Params.FirstOrDefault() ?? "").TrimStart('#'), "Joined");
        else
            await RaiseAsync(UserJoined, message.Nick, "UserJoined");
    }

    /// <summary>
    /// NOTICE の本文が、認証の失敗か。
    /// </summary>
    private static bool IsAuthFailure(string? notice)
        => notice is not null
           && (notice.Contains("Login authentication failed", StringComparison.OrdinalIgnoreCase)
               || notice.Contains("Improperly formatted auth", StringComparison.OrdinalIgnoreCase)
               || notice.Contains("Login unsuccessful", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 送る列から取り出して、上限の件数の内側で、間隔を空けて送り続けます。
    /// </summary>
    /// <param name="write">1 行を書く関数</param>
    /// <param name="session">この接続の取り消し（送る列が閉じられて、送り終えたら、これで接続を閉じる）</param>
    /// <param name="token">全体の取り消し</param>
    private async Task SendLoopAsync(Func<string, Task> write, CancellationTokenSource session, CancellationToken token)
    {
        await foreach (var line in _outbox.Reader.ReadAllAsync(session.Token))
        {
            var wait = WaitBeforeNextSend(_sentAt, DateTime.UtcNow);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, session.Token);

            await write(line);
            _sentAt.Enqueue(DateTime.UtcNow);
        }

        // 送る列が閉じられて、送り終えた: 切断の指示なので、この接続を閉じる
        if (!token.IsCancellationRequested) await session.CancelAsync();
    }

    /// <summary>
    /// 1 回分の WebSocket のメッセージ（文字列）を受け取ります。閉じられたときは null。
    /// 長い間、何も届かなければ <see cref="TimeoutException"/>（Twitch の PING も来ないので、切れたとする）。
    /// </summary>
    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken token)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(IdleTimeout);
        using var buffer = new System.IO.MemoryStream();
        var chunk = new byte[8192];
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(chunk, timeoutSource.Token);
                if (result.MessageType == WebSocketMessageType.Close) return null;

                await buffer.WriteAsync(chunk.AsMemory(0, result.Count), token);
                if (result.EndOfMessage) break;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Twitch のチャットから、長い間、何も届きませんでした。");
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void RaiseLog(string text) => Log?.Invoke(text);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "イベントを受ける側の失敗で、受信を止めない。ログに残して続ける")]
    private async Task RaiseAsync<T>(Func<T, Task>? handler, T argument, string name)
    {
        if (handler is null) return;
        try
        {
            await handler(argument);
        }
        catch (Exception ex)
        {
            RaiseLog($"<Error> IRC {name} handler failed: {ex.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "イベントを受ける側の失敗で、受信を止めない。ログに残して続ける")]
    private async Task RaiseAsync(Func<Task>? handler, string name)
    {
        if (handler is null) return;
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            RaiseLog($"<Error> IRC {name} handler failed: {ex.Message}");
        }
    }
}
