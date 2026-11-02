using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch の EventSub（WebSocket）につなぎ、イベントを受け取るクライアント。TwitchLib.EventSub.Websockets の代わりに、ClientWebSocket で直接つなぐ。
/// つなぎ直しも担当する: 切れたら、少しずつ間を空けて新しいセッションを作り直す。Twitch から「別の URL へ」と求められたら、
/// 新しい接続が確立してから、古い接続を閉じる（このときセッションは続くので、購読は作り直さない）。
/// キープアライブが来なくなったら、切れたものとして作り直す。
/// 受け取ったメッセージの種類（と、通知なら購読の種類）はログに残す。ユーザー名などの中身は、ログに出さない。
/// </summary>
internal sealed class EventSubClient : IDisposable
{
    /// <summary>
    /// EventSub の WebSocket の URL（キープアライブの間隔を 30 秒にする）。
    /// </summary>
    internal const string DefaultUrl = "wss://eventsub.wss.twitch.tv/ws?keepalive_timeout_seconds=30";

    /// <summary>
    /// キープアライブの間隔に足す余裕。この時間、何も届かなければ、切れたものとする。
    /// </summary>
    private static readonly TimeSpan KeepAliveMargin = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan FirstConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);
    private const int SeenMessageLimit = 200;

    private readonly string _url;
    private readonly HashSet<string> _seenMessageIds = [];
    private readonly Queue<string> _seenMessageOrder = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private TaskCompletionSource<bool>? _firstWelcome;
    private TimeSpan _keepAlive = TimeSpan.FromSeconds(30) + KeepAliveMargin;

    /// <summary>
    /// つないだ状態か（セッションが始まってから、切れるまで）。
    /// </summary>
    internal bool IsConnected { get; private set; }

    /// <summary>
    /// 新しいセッションが始まったとき（セッション ID を渡す）。購読は、このセッション ID で作る。
    /// つなぎ直しでセッションが続くときは、起きない。
    /// </summary>
    internal event Func<string, Task>? SessionStarted;

    /// <summary>
    /// 切れたとき。
    /// </summary>
    internal event Func<Task>? Disconnected;

    /// <summary>
    /// フォローの通知を受けたとき。
    /// </summary>
    internal event Func<FollowEvent, Task>? Followed;

    /// <summary>
    /// ビッツの通知を受けたとき。
    /// </summary>
    internal event Func<CheerEvent, Task>? Cheered;

    /// <summary>
    /// チャンネルポイントの交換の通知を受けたとき。
    /// </summary>
    internal event Func<RedemptionEvent, Task>? Redeemed;

    /// <summary>
    /// クライアントを作ります。
    /// </summary>
    /// <param name="url">つなぐ URL（テストなどで変える。省略時は Twitch の既定）</param>
    internal EventSubClient(string url = DefaultUrl)
    {
        _url = url;
    }

    /// <summary>
    /// つなぎます。最初のセッションが始まるまで待ちます（待ち切れなければ失敗）。そのあとの切断は、裏でつなぎ直します。
    /// </summary>
    internal async Task ConnectAsync()
    {
        if (_runTask is not null) return;

        _cts = new CancellationTokenSource();
        _firstWelcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = _cts.Token;
        _runTask = Task.Run(() => RunAsync(token), token);

        try
        {
            await _firstWelcome.Task.WaitAsync(FirstConnectTimeout);
        }
        catch (TimeoutException)
        {
            await DisconnectAsync();
            throw new HttpRequestException("EventSub に接続できませんでした（時間切れ）。");
        }
    }

    /// <summary>
    /// 切ります（つなぎ直しも止める）。
    /// </summary>
    internal async Task DisconnectAsync()
    {
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
    /// 後始末をします。
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    /// <summary>
    /// つなぐ・受け取る・切れたらつなぎ直す、の繰り返し。止めるまで続く。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "通信の最上位。どんな失敗でも、ログに残して、つなぎ直す（受信が止まったままにしない）")]
    private async Task RunAsync(CancellationToken token)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                IrcClient.ConfigureKeepAlive(socket);
                await socket.ConnectAsync(new Uri(_url), token);
                await ReceiveLoopAsync(socket, () => delay = TimeSpan.FromSeconds(1), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogController.OutputLog($"EventSub connection error: {ex.GetType().Name}");
            }

            if (IsConnected)
            {
                IsConnected = false;
                await RaiseAsync(Disconnected, "Disconnected");
            }

            if (token.IsCancellationRequested) break;

            LogController.OutputLog($"EventSub reconnecting in {(int) delay.TotalSeconds}s");
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

        IsConnected = false;
    }

    /// <summary>
    /// 1 つの接続（と、つなぎ直しで引き継いだ接続）から、メッセージを受け取り続けます。切れたら戻ります。
    /// </summary>
    /// <param name="first">最初の接続</param>
    /// <param name="onSessionStarted">セッションが始まったときに呼ぶ（つなぎ直しの間隔を、元に戻すため）</param>
    /// <param name="token">止めるための取り消し</param>
    private async Task ReceiveLoopAsync(ClientWebSocket first, Action onSessionStarted, CancellationToken token)
    {
        var current = first;
        var owned = new List<ClientWebSocket>();
        try
        {
            while (true)
            {
                var text = await ReceiveTextAsync(current, _keepAlive, token);
                if (text is null) return;

                var message = await HandleMessageAsync(text, resumedSession: false);
                if (message.Kind == EventSubMessageKind.Welcome) onSessionStarted();
                if (message.Kind != EventSubMessageKind.Reconnect || message.ReconnectUrl is null) continue;

                // 別の URL へつなぎ直す: 新しい接続のセッションが続くことを確かめてから、古い接続を閉じる（取りこぼしを防ぐ）
                var next = new ClientWebSocket();
                IrcClient.ConfigureKeepAlive(next);
                owned.Add(next);
                await next.ConnectAsync(new Uri(message.ReconnectUrl), token);
                var welcome = await ReceiveTextAsync(next, FirstConnectTimeout, token);
                if (welcome is null) return;

                await HandleMessageAsync(welcome, resumedSession: true);
                await CloseQuietlyAsync(current);
                current = next;
            }
        }
        finally
        {
            await CloseQuietlyAsync(current);
            foreach (var socket in owned) socket.Dispose();
        }
    }

    /// <summary>
    /// 受け取ったメッセージ 1 件を処理します。種類をログに残し、通知なら、購読の種類に合ったイベントを起こします。
    /// 同じ ID の通知が 2 度届いたときは、2 度目を捨てます。
    /// </summary>
    /// <param name="json">受け取った本文</param>
    /// <param name="resumedSession">つなぎ直しで、同じセッションが続いているか（続くなら、新しいセッションとして知らせない）</param>
    /// <returns>読んだメッセージ</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "受信の最上位。読めないメッセージ 1 件で、受信を止めない。ログに残して続ける")]
    internal async Task<EventSubMessage> HandleMessageAsync(string json, bool resumedSession)
    {
        EventSubMessage message;
        try
        {
            message = EventSubParser.Parse(json);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            LogController.OutputLog("EventSub: 読めないメッセージを受け取りました（捨てます）");
            return new EventSubMessage(EventSubMessageKind.Unknown, "", null, null, null, null, null);
        }

        switch (message.Kind)
        {
            case EventSubMessageKind.Welcome:
                _keepAlive = TimeSpan.FromSeconds(message.KeepAliveTimeoutSeconds ?? 30) + KeepAliveMargin;
                IsConnected = true;
                LogController.OutputLog($"EventSub WebSocket {message.SessionId} {(resumedSession ? "resumed" : "connected")}.");
                _firstWelcome?.TrySetResult(true);
                if (!resumedSession && message.SessionId is not null && SessionStarted is { } started)
                    await RaiseSessionStartedAsync(started, message.SessionId);
                break;
            case EventSubMessageKind.Reconnect:
                LogController.OutputLog("EventSub: reconnect requested by Twitch");
                break;
            case EventSubMessageKind.Revocation:
                LogController.OutputLog($"<Error> EventSub: 購読が取り消されました ({message.SubscriptionType})");
                break;
            case EventSubMessageKind.Notification:
                await HandleNotificationAsync(message);
                break;
            case EventSubMessageKind.Unknown:
                LogController.OutputLog("EventSub: 知らない種類のメッセージを受け取りました（無視します）");
                break;
        }

        return message;
    }

    private async Task HandleNotificationAsync(EventSubMessage message)
    {
        if (!MarkSeen(message.MessageId))
        {
            LogController.OutputLog($"EventSub: 同じ通知を受け取ったので、捨てます ({message.SubscriptionType})");
            return;
        }

        LogController.OutputLog($"EventSub notification: {message.SubscriptionType}");
        if (message.Event is not { } payload) return;

        switch (message.SubscriptionType)
        {
            case "channel.follow":
                if (EventSubParser.ParseFollow(payload) is { } follow) await RaiseAsync(Followed, follow, "Followed");
                break;
            case "channel.cheer":
                await RaiseAsync(Cheered, EventSubParser.ParseCheer(payload), "Cheered");
                break;
            case "channel.channel_points_custom_reward_redemption.add":
                if (EventSubParser.ParseRedemption(payload) is { } redemption) await RaiseAsync(Redeemed, redemption, "Redeemed");
                break;
            default:
                LogController.OutputLog($"EventSub: 扱わない購読の通知です ({message.SubscriptionType})");
                break;
        }
    }

    /// <summary>
    /// メッセージ ID を見たことにします。すでに見ていたら false。
    /// </summary>
    private bool MarkSeen(string messageId)
    {
        if (string.IsNullOrEmpty(messageId)) return true;
        if (!_seenMessageIds.Add(messageId)) return false;

        _seenMessageOrder.Enqueue(messageId);
        while (_seenMessageOrder.Count > SeenMessageLimit)
            _seenMessageIds.Remove(_seenMessageOrder.Dequeue());
        return true;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "イベントを受ける側の失敗で、受信を止めない。ログに残して続ける")]
    private static async Task RaiseSessionStartedAsync(Func<string, Task> handler, string sessionId)
    {
        try
        {
            await handler(sessionId);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> EventSub SessionStarted handler failed: {ex.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "イベントを受ける側の失敗で、受信を止めない。ログに残して続ける")]
    private static async Task RaiseAsync<T>(Func<T, Task>? handler, T argument, string name)
    {
        if (handler is null) return;
        try
        {
            await handler(argument);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> EventSub {name} handler failed: {ex.Message}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "イベントを受ける側の失敗で、受信を止めない。ログに残して続ける")]
    private static async Task RaiseAsync(Func<Task>? handler, string name)
    {
        if (handler is null) return;
        try
        {
            await handler();
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> EventSub {name} handler failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 1 メッセージ分の本文（文字列）を受け取ります。閉じられたときは null。
    /// 指定の時間、何も届かなければ <see cref="TimeoutException"/>（キープアライブが来ないので、切れたとする）。
    /// </summary>
    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, TimeSpan timeout, CancellationToken token)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutSource.CancelAfter(timeout);
        using var buffer = new MemoryStream();
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
            throw new TimeoutException("EventSub のメッセージが、一定時間届きませんでした。");
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// 接続を閉じます（失敗しても、何もしません）。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "後始末。閉じられなくても、続けて破棄するので、失敗は無視する")]
    private static async Task CloseQuietlyAsync(ClientWebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
        }
        catch (Exception)
        {
            // 閉じられなくても、破棄で切れる
        }
    }
}
