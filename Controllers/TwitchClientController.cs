using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using FaraBotModerator.Enums;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using DeepL.Model;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch チャットクライアント経由の操作（メッセージ送信、イベント受信など）を管理するコントローラー
/// </summary>
public class TwitchClientController
{
    private readonly BouyomiChanController _bouyomiChanController = new();
    /// <summary>
    /// 表示用のチャットの待ち行列（送った順に並ぶ）。受信（IRC の受信のスレッド）と表示（UI の監視ループ）が別のスレッドから触るので、並行アクセスに対応したものを使う
    /// </summary>
    private readonly ChatRowQueue _chatDataQueue = new();

    /// <summary>
    /// チャットのアイコンを待つ時間（ミリ秒）。これを過ぎたら、アイコン無しで表示する（後ろのチャットを止め続けないため）
    /// </summary>
    private const int ChatIconWaitMilliseconds = 1500;
    private readonly SecretKeyModel _secretKeys;
    private readonly TwitchApiController _twitchApiController;
    private IrcClient? _irc;

    /// <summary>
    /// 「Login」の知らせを、すでに送ったか（つなぎ直しのたびに、同じ知らせをチャットに出さない）。
    /// </summary>
    private bool _loginAnnounced;
    private readonly UniqueChannelPointController _uniqueChannelPointController;
    private readonly TwitchTranslationController _twitchTranslationController;
    private readonly PokemonCommandController _pokemonCommandController;
    private string _twitchUserName = "";
    private string _twitchUserDisplayName = "";

    /// <summary>
    /// Twitch チャットクライアントが接続されているかどうかを取得します。
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// 棒読みちゃんコントローラーを取得します。
    /// </summary>
    public BouyomiChanController BouyomiChanController => _bouyomiChanController;

    /// <summary>
    /// TwitchClientController のコンストラクタ
    /// </summary>
    /// <param name="secretKeys">設定情報モデル</param>
    /// <param name="twitchApiController">Twitch APIコントローラー</param>
    public TwitchClientController(SecretKeyModel secretKeys, TwitchApiController twitchApiController)
    {
        _secretKeys = secretKeys;
        _twitchApiController = twitchApiController;
        _twitchTranslationController = new TwitchTranslationController(_secretKeys.DeepL.ApiKey);
        _uniqueChannelPointController = new UniqueChannelPointController(_secretKeys);
        _pokemonCommandController =
            new PokemonCommandController(PokemonCommandController.ParseRegisteredFormat(_secretKeys.Pokemon.BattleFormat),
                PokemonCommandController.ParseDataSource(_secretKeys.Pokemon.DataSource), _secretKeys.Pokemon.SyncKey);
    }

    /// <summary>
    /// Twitch チャットクライアントを接続します。ログインできるまで待ちます（できなければ例外）。そのあとの切断は、裏でつなぎ直します。
    /// </summary>
    public async Task ConnectAsync()
    {
        var client = _secretKeys.Twitch.Client;
        _twitchUserName = client.UserName;
        _twitchUserDisplayName = client.DisplayName;

        _irc = new IrcClient(_twitchUserName, "oauth:" + Settings.Default.AccessToken, _twitchUserName);
        _irc.Log += OnIrcLog;
        _irc.Connected += OnConnectedAsync;
        _irc.Disconnected += OnDisconnectedAsync;
        _irc.Joined += OnJoinedAsync;
        _irc.UserJoined += OnUserJoinedAsync;
        _irc.MessageReceived += HandleMessageAsync;
        _irc.UserNoticeReceived += OnUserNoticeAsync;

        await _irc.ConnectAsync();
    }

    /// <summary>
    /// Twitch チャットクライアントを切断します。切断は裏で行い、UI スレッドを止めません（送りかけの知らせを送り切ってから切る）。
    /// </summary>
    public void Disconnect()
    {
        if (_irc == null) return;

        if (_irc.IsConnected)
        {
            SendMessage(_twitchUserName, $"Logout {Settings.Default.BotName}.");
        }

        LogController.OutputLog($"Logout {Settings.Default.BotName}.");

        _twitchTranslationController.Dispose();

        _ = DisconnectIrcAsync(_irc);
    }

    /// <summary>
    /// IRC のクライアントを切って、後始末をします。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "切断の後始末。失敗しても、ログに残して終える")]
    private static async Task DisconnectIrcAsync(IrcClient irc)
    {
        try
        {
            await irc.DisconnectAsync();
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> チャットの切断に失敗しました: {ex.Message}");
        }
        finally
        {
            irc.Dispose();
        }
    }

    /// <summary>
    /// アプリケーションからTwitchへチャットメッセージを送信し、翻訳処理も行います。
    /// </summary>
    /// <param name="message">送信するメッセージ</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    public async Task SendApplicationMessageAsync(string message)
    {
        if (message.Equals("")) return;
        try
        {
            SendMessage(_twitchUserName, message);
            await SendMessageTranslationAsync(_twitchUserName, _twitchUserDisplayName, message);
        }
        catch (Exception ex)
        {
            // 呼び出し元は結果を待たない（_ = で捨てる）ので、例外はここで受けてログに残す（振る舞いは async void のときと同じ）
            LogController.OutputLog($"<Error> アプリからのメッセージの送信に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// ボット名付きのモデレーターメッセージを送信します。
    /// </summary>
    /// <param name="message">送信するメッセージ</param>
    public void SendModeratorMessage(string message)
    {
        if (message.Equals("")) return;
        SendMessage(_twitchUserName, $"[{Settings.Default.BotName}] {message}");
    }

    /// <summary>
    /// テストメッセージを送信します（内部用）。
    /// </summary>
    /// <param name="userName">送信者名</param>
    /// <param name="message">送信メッセージ</param>
    public void SendTestMessage(string userName, string message)
    {
        SendMessage(userName, message);
    }

    /// <summary>
    /// Twitchへメッセージを送信し、UI表示用にキューへ追加します。
    /// </summary>
    /// <param name="userName">送信者名</param>
    /// <param name="message">送信メッセージ</param>
    /// <param name="userId">送信者ユーザーID（任意）</param>
    private void SendMessage(string userName, string message, string userId = "")
    {
        if (_irc == null) return;
        _irc.SendMessage(message);
        AddChatListData(userName, message, userId);
    }

    /// <summary>
    /// キューからチャットデータを1件取り出します。
    /// </summary>
    /// <returns>チャットデータ。キューが空の場合はnull</returns>
    public ChatModel? PickChatData()
    {
        return _chatDataQueue.TryPick();
    }

    /// <summary>
    /// チャットデータをUI表示用キューに、送った順のまま追加します。アイコンURLは後から取得し、間に合わなければ空にします。
    /// </summary>
    /// <param name="userName">ユーザー名</param>
    /// <param name="message">メッセージ内容</param>
    /// <param name="userId">ユーザーID</param>
    private void AddChatListData(string userName, string message, string userId = "")
    {
        // 行は今の順番で確保する（アイコンの取得の速さで、表示の順が入れ替わらないように）
        var chatModel = new ChatModel {Name = userName, Chat = message};
        _chatDataQueue.Enqueue(chatModel, GetChatIconAsync(userName, userId));
    }

    /// <summary>
    /// チャットのアイコンURLを取得します。時間内に取れないとき・取れないときは null（アイコン無しで表示する）。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "アイコンが取れなくても、チャットの行は表示する（アイコン無し）。このタスクは行の待ち行列に渡されるため、種類を問わず受けて null にしないと、行が出なくなる")]
    private async Task<string?> GetChatIconAsync(string userName, string userId)
    {
        try
        {
            var lookup = userId != ""
                ? _twitchApiController.GetTwitchIconUrlByIdAsync(userId)
                : _twitchApiController.GetTwitchIconUrlByLoginAsync(userName);
            return await lookup.WaitAsync(TimeSpan.FromMilliseconds(ChatIconWaitMilliseconds));
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // アイコンが取れなくても、チャットの行は表示する（アイコン無し）
            LogController.OutputLog($"<Error> チャットのアイコンを取得できませんでした: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// IRC のログ出力イベントハンドラ（受け取った行・送った行。PASS の行は含まない）
    /// </summary>
    private static void OnIrcLog(string text)
    {
        LogController.OutputLog(text);
    }

    /// <summary>
    /// Twitch接続完了時のイベントハンドラ
    /// </summary>
    private Task OnConnectedAsync()
    {
        IsConnected = true;
        LogController.OutputLog($@"Connected to {_twitchUserName}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Twitch切断時のイベントハンドラ
    /// </summary>
    private Task OnDisconnectedAsync()
    {
        IsConnected = false;
        LogController.OutputLog($"Disconnected to {_twitchUserName}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// サブスク・Raid・お知らせ（USERNOTICE）を、種類ごとの処理に振り分けます。
    /// </summary>
    private Task OnUserNoticeAsync(UserNoticeEvent e)
    {
        switch (e.Kind)
        {
            case UserNoticeKind.NewSubscriber:
                HandleNewSubscriber(e);
                break;
            case UserNoticeKind.PrimeSubscriber:
                HandlePrimeSubscriber(e);
                break;
            case UserNoticeKind.ReSubscriber:
                HandleReSubscriber(e);
                break;
            case UserNoticeKind.GiftedSubscription:
                HandleGiftedSubscription(e);
                break;
            case UserNoticeKind.Raid:
                return HandleRaidAsync(e);
            case UserNoticeKind.Announcement:
                return HandleAnnouncementAsync(e);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// /announcement を実行したときの処理
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private async Task HandleAnnouncementAsync(UserNoticeEvent e)
    {
        try
        {
            var userName = e.Channel;
            var displayName = _secretKeys.Twitch.Client.DisplayName;
            var sourceMessage = e.Message;
            await SendMessageTranslationAsync(userName, displayName, sourceMessage, true);
        }
        catch (Exception ex)
        {
            LogController.OutputLog(e.Message);
            LogController.OutputLog($"<Error> {ex.Message}");
        }
    }

    /// <summary>
    /// チャンネル参加時のイベントハンドラ
    /// </summary>
    private Task OnJoinedAsync(string channel)
    {
        // つなぎ直して、参加し直したときは、同じ知らせを、チャットに出さない
        if (_loginAnnounced) return Task.CompletedTask;
        _loginAnnounced = true;

        SendMessage(channel, $"Login {Settings.Default.BotName}.");
        LogController.OutputLog($"Login {Settings.Default.BotName}.");

        SendMessage(channel,
            Settings.Default.BotName + (_secretKeys.BouyomiChan.Checked ? " " : " Not ") + "Connecting BouyomiChan.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// ユーザーがチャンネルに参加した時のイベントハンドラ
    /// </summary>
    private static Task OnUserJoinedAsync(string username)
    {
        LogController.OutputLog($"<Join> {username}", TwitchEventEnum.Join);
        return Task.CompletedTask;
    }

    /// <summary>
    /// イベントの通知（チャットへの送信と、棒読みちゃんでの読み上げ）を行います。
    /// 画面の「Reaction Event Settings」で、そのイベントのチェックボックスが OFF のときは、どちらも行いません。
    /// </summary>
    /// <param name="model">イベントの設定（チェックボックスの状態）</param>
    /// <param name="userName">チャット一覧に出す名前</param>
    /// <param name="message">チャットへ送るメッセージ（接頭辞の [BotName] は、ここで付ける）</param>
    /// <param name="talkText">棒読みちゃんで読み上げる文（null なら読み上げない）</param>
    /// <param name="userId">チャット一覧に出すユーザー ID（任意）</param>
    private void SendEventNotification(IEventModel model, string userName, string message, string? talkText,
        string userId = "")
    {
        if (!model.Checked) return;

        SendMessage(userName, $"[{Settings.Default.BotName}] {message}", userId);
        if (talkText is not null)
            BouyomiChanController.AddEventTalkTask(talkText, _secretKeys.BouyomiChan.Checked);
    }

    /// <summary>
    /// EventSubからのフォローイベントを処理し、メッセージ送信と読み上げを行います。
    /// </summary>
    /// <param name="e">フォローのイベント</param>
    internal void TwitchEventSubOnFollow(FollowEvent e)
    {
        var followerName = e.UserName;
        var followerChannelUrl = $"https://twitch.tv/{e.UserLogin}";
        var message = _secretKeys.Event.Follow.Message.Replace("{followerName}", followerName)
            .Replace("{followerChannelUrl}", followerChannelUrl);

        SendEventNotification(_secretKeys.Event.Follow, followerName, message, $"{followerName}さんがFollowしました");
        LogController.OutputLog($"<Follow> Name: {followerName}, URL: {followerChannelUrl}",
            TwitchEventEnum.Follow);
    }


    /// <summary>
    /// Bits のメッセージの、累計の置き換え文字。
    /// </summary>
    internal const string BitsTotalPlaceholder = "{totalBitsAmount}";

    /// <summary>
    /// Bits のメッセージのテンプレートを、値で置き換えます。
    /// </summary>
    /// <param name="template">テンプレート</param>
    /// <param name="userName">ビッツを投げたユーザーの名前</param>
    /// <param name="amount">投げられたビッツの量</param>
    /// <param name="total">ビッツの合計（取得できなかったときは null。「-」で表す）</param>
    internal static string BuildBitsMessage(string template, string userName, int amount, int? total)
        => template.Replace("{bitsAmount}", amount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{bitsSendUserName}", userName, StringComparison.Ordinal)
            .Replace(BitsTotalPlaceholder, total?.ToString(CultureInfo.InvariantCulture) ?? "-", StringComparison.Ordinal);

    /// <summary>
    /// EventSubからのBitsイベントを処理し、メッセージ送信と読み上げを行います。
    /// </summary>
    /// <param name="e">ビッツのイベント（匿名のときは、ユーザーが無いので、何もしない）</param>
    internal async Task SendBitsEventSubMessageAsync(CheerEvent e)
    {
        var bitsSendUserName = e.UserName;
        if (bitsSendUserName == null) return;

        var bitsAmount = e.Bits;
        var template = _secretKeys.Event.Bits.Message;
        // 累計は、メッセージに {totalBitsAmount} がある（通知が有効）ときだけ、Twitch の API から取る
        int? total = null;
        if (_secretKeys.Event.Bits.Checked && template.Contains(BitsTotalPlaceholder, StringComparison.Ordinal))
            total = e.UserId is null ? null : await _twitchApiController.GetBitsTotalAsync(e.UserId);
        var message = BuildBitsMessage(template, bitsSendUserName, bitsAmount, total);
        SendEventNotification(_secretKeys.Event.Bits, bitsSendUserName, message,
            $"{bitsSendUserName}さん{bitsAmount}bitsありがとうございます");
        LogController.OutputLog($"<Bits> UserName: {bitsSendUserName}, Amount: {bitsAmount}", TwitchEventEnum.Bits);
    }

    /// <summary>
    /// EventSubからのチャンネルポイント交換イベントを処理します。
    /// </summary>
    /// <param name="e">チャンネルポイント交換のイベント</param>
    internal void SendChannelPointEventSubMessage(RedemptionEvent e)
    {
        var channelPointTitle = e.RewardTitle;
        var channelPointCost = e.RewardCost;
        var channelPointUserId = e.UserLogin;
        var message = _secretKeys.Event.ChannelPoint.Message
            .Replace("{channelPointCost}", channelPointCost.ToString())
            .Replace("{channelPointTitle}", channelPointTitle)
            .Replace("{channelPointUserName}", channelPointUserId);

        // ChannelPointは読み上げしない
        SendEventNotification(_secretKeys.Event.ChannelPoint, channelPointUserId, message, null);
        LogController.OutputLog($"<ChannelPoint> UserName: {channelPointUserId}, Title: {channelPointTitle}",
            TwitchEventEnum.ChannelPoint);

        // チャンネルポイント固有の処理は別で行う
        SendModeratorMessage(_uniqueChannelPointController.Exec(channelPointUserId, channelPointTitle));
    }

    /// <summary>
    /// 別チャンネルからRaidが来たときの処理を行います。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private async Task HandleRaidAsync(UserNoticeEvent e)
    {
        try
        {
            var raiderName = e.RaiderLogin;
            var raiderChannelUrl = $"https://twitch.tv/{raiderName}";
            var message = _secretKeys.Event.Raid.Message.Replace("{raiderName}", raiderName)
                .Replace("{raiderChannelUrl}", raiderChannelUrl);
            SendEventNotification(_secretKeys.Event.Raid, raiderName, message, $"{raiderName}さんにRaidされました");
            LogController.OutputLog($"<Raid> Name: {raiderName}, URL: {raiderChannelUrl}", TwitchEventEnum.Raid);

            await _twitchApiController.SendShoutoutAsync(raiderName);
        }
        catch (Exception ex)
        {
            // 受信の処理を止めないよう、ここで受けてログに残す
            LogController.OutputLog($"<Error> Raid の処理に失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 新規サブスクライブ時の処理を行います。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private void HandleNewSubscriber(UserNoticeEvent e)
    {
        try
        {
            var subscriberName = e.DisplayName;
            var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
                .Replace("{totalSubscriptionMonth}", "1");
            SendEventNotification(_secretKeys.Event.Subscription, subscriberName, message,
                $"{subscriberName}さんサブスクありがとうございます", e.UserId);
            LogController.OutputLog($"<New Subscriber> Name: {subscriberName}", TwitchEventEnum.Subscriber);
        }
        catch (Exception ex)
        {
            // イベントの中で例外を出さない（ほかのイベント・チャットの処理を止めないため）
            LogController.OutputLog($"<Error> 新規サブスクの処理に失敗しました: {ex.Message}", TwitchEventEnum.Subscriber);
        }
    }

    /// <summary>
    /// Primeサブスクライブ時の処理を行います。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private void HandlePrimeSubscriber(UserNoticeEvent e)
    {
        try
        {
            var subscriberName = e.DisplayName;
            var months = e.CumulativeMonths ?? "1";
            var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
                .Replace("{totalSubscriptionMonth}", months);
            SendEventNotification(_secretKeys.Event.Subscription, subscriberName, message,
                $"{subscriberName}さんサブスクありがとうございます", e.UserId);
            LogController.OutputLog($"<Prime Subscriber> Name: {subscriberName}", TwitchEventEnum.Subscriber);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> Primeサブスクの処理に失敗しました: {ex.Message}", TwitchEventEnum.Subscriber);
        }
    }

    /// <summary>
    /// 継続サブスクライブ（再サブスク）時の処理を行います。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private void HandleReSubscriber(UserNoticeEvent e)
    {
        try
        {
            // debugのために、Logを残しておく
            var subscriberName = e.DisplayName;
            LogController.OutputLog($"<Subscriber> Name: {subscriberName}",
                TwitchEventEnum.Subscriber);
            var totalSubscriptionMonth = e.CumulativeMonths ?? "1";
            LogController.OutputLog($"<Subscriber> total: {totalSubscriptionMonth} time.",
                TwitchEventEnum.Subscriber);
            var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
                .Replace("{totalSubscriptionMonth}", totalSubscriptionMonth);
            LogController.OutputLog($"<Subscriber> Message: {message}",
                TwitchEventEnum.Subscriber);
            SendEventNotification(_secretKeys.Event.Subscription, subscriberName, message,
                $"{subscriberName}さん{totalSubscriptionMonth}か月目のサブスクありがとうございます", e.UserId);
            LogController.OutputLog($"<Subscriber> Name: {subscriberName}, total: {totalSubscriptionMonth} time.",
                TwitchEventEnum.Subscriber);
        }
        catch (Exception ex)
        {
            BouyomiChanController.AddEventTalkTask("イベントの処理に失敗しました", _secretKeys.BouyomiChan.Checked);
            LogController.OutputLog($"<Subscriber> Error: {ex.Message}", TwitchEventEnum.Subscriber);
        }
    }

    /// <summary>
    /// サブスクギフト受信時の処理を行います。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private void HandleGiftedSubscription(UserNoticeEvent e)
    {
        try
        {
            var giftedUserName = e.DisplayName;
            var url = $"https://twitch.tv/{giftedUserName}";
            var message = _secretKeys.Event.Gift.Message.Replace("{giftedUserName}", giftedUserName);
            SendEventNotification(_secretKeys.Event.Gift, giftedUserName, message,
                $"{giftedUserName}さんGiftありがとうございます");
            LogController.OutputLog($"<Gift> Name: {giftedUserName} URL: {url}", TwitchEventEnum.Gift);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> ギフトの処理に失敗しました: {ex.Message}", TwitchEventEnum.Gift);
        }
    }

    /// <summary>
    /// メッセージ受信時の処理を行います（ポケモンコマンド・翻訳・読み上げ）。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IRC のイベント・チャット処理の最上位。例外が他のイベント・チャットの処理を止めないよう、ここで受けてログに残す")]
    private async Task HandleMessageAsync(ChatMessageEvent e)
    {
        if (_irc == null) return;
        try
        {
            var userName = e.Username;
            var displayName = e.DisplayName;
            var sourceMessage = e.Message;
            if (await TrySendPokemonCommandAsync(userName, sourceMessage, e.UserId,
                    e.IsBroadcaster, e.IsModerator))
                return;

            await SendMessageTranslationAsync(userName, displayName, sourceMessage, false, e.UserId);
        }
        catch (Exception ex)
        {
            LogController.OutputLog(e.Message);
            LogController.OutputLog($"<Error> {ex.Message}");
        }
    }

    /// <summary>
    /// ポケモンコマンド（!poke 系）であれば実行し、結果をチャットに送信します。
    /// </summary>
    /// <returns>ポケモンコマンドとして処理した場合は true</returns>
    private async Task<bool> TrySendPokemonCommandAsync(string userName, string sourceMessage, string userId,
        bool isBroadcaster, bool isModerator)
    {
        var prefix = $"[{Settings.Default.BotName}] ";
        // Twitch の 1 メッセージ 500 文字制限に収める
        var (handled, reply) = await _pokemonCommandController.HandleAsync(sourceMessage, userName, isBroadcaster,
            isModerator, 480 - prefix.Length);
        if (!handled) return false;
        if (!string.IsNullOrEmpty(reply)) SendMessage(userName, prefix + reply, userId);
        return true;
    }

    /// <summary>
    /// ポケモンの登録している対戦形式（"Singles" / "Doubles"。null は未登録でシングル扱い）を切り替えます。
    /// </summary>
    /// <param name="format">対戦形式の文字列（未登録は null）</param>
    public void SetPokemonBattleFormat(string? format)
    {
        _pokemonCommandController.RegisteredFormat = PokemonCommandController.ParseRegisteredFormat(format);
    }

    /// <summary>
    /// ポケモンの登録している対戦形式（"Singles" / "Doubles"。未登録は null）を返します。
    /// </summary>
    public string? PokemonBattleFormat => _pokemonCommandController.RegisteredFormat?.ToString();

    /// <summary>
    /// ポケモンのデータの取得元（"Production" / "Develop"）を切り替えます。
    /// </summary>
    /// <param name="dataSource">取得元の文字列</param>
    public void SetPokemonDataSource(string dataSource)
    {
        _pokemonCommandController.DataSource = PokemonCommandController.ParseDataSource(dataSource);
    }

    /// <summary>
    /// ポケモンの登録データのアクセスキーを切り替えます（空にすると登録系のコマンドは使えなくなります）。
    /// </summary>
    /// <param name="syncKey">アクセスキー</param>
    public void SetPokemonSyncKey(string syncKey)
    {
        _pokemonCommandController.SyncKey = syncKey;
    }

    /// <summary>
    /// メッセージの翻訳と読み上げタスクの追加を行います。
    /// </summary>
    private async Task SendMessageTranslationAsync(string userName, string displayName, string sourceMessage,
        bool isAnnouncement = false, string userId = "")
    {
        if (!TwitchTranslationController.IsTargetTranslationWord(sourceMessage)) return;

        var beatSaberRegexMessage = TextRegexController.LoadBsrChat(sourceMessage);
        if (sourceMessage != beatSaberRegexMessage && !isAnnouncement)
        {
            // BeatSaber関連は読み上げだけ行う（空文字の場合は読み上げもスキップ）
            if (!string.IsNullOrEmpty(beatSaberRegexMessage))
            {
                BouyomiChanController.AddTalkTask(userName, beatSaberRegexMessage, _secretKeys.BouyomiChan.Checked);
            }
            return;
        }

        await MessageTranslationProcessAsync(sourceMessage, userName, displayName, isAnnouncement, userId);
    }

    /// <summary>
    /// 実際の翻訳処理（DeepL）と、翻訳結果の送信および読み上げ処理を行います。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "翻訳（DeepL）の失敗でチャットの処理を止めない。失敗はチャットとログで知らせる")]
    private async Task MessageTranslationProcessAsync(string sourceMessage, string userName, string displayName,
        bool isAnnouncement = false, string userId = "")
    {
        try
        {
            // URLのみは翻訳しない
            var message = sourceMessage;
            if (!TwitchTranslationController.IsOnlyUrl(message))
            {
                var targetLanguage = TwitchTranslationController.IsJapanese(sourceMessage)
                    ? "EN-US"
                    : "JA";

            // Emote文字列は翻訳と読み上げで使わないので削除する
                var sourceMessageForTranslation = _irc != null
                    ? TwitchTranslationController.RemoveEmotes(sourceMessage, _irc.Emotes)
                    : sourceMessage;
                if (string.IsNullOrEmpty(sourceMessageForTranslation))
                {
                    BouyomiChanController.AddTalkTask(displayName, "", _secretKeys.BouyomiChan.Checked);
                    return;
                }

                var text = await _twitchTranslationController.TranslateAsync(sourceMessageForTranslation, targetLanguage);
                if (text == null)
                {
                    BouyomiChanController.AddTalkTask(displayName, sourceMessage, _secretKeys.BouyomiChan.Checked);
                    return;
                }
                var sourceLanguage = text.DetectedSourceLanguageCode.ToUpper();
                // 誤検知対策: 日本語なのに他言語と判定された場合
                if (sourceLanguage != "JA" && TwitchTranslationController.IsJapanese(sourceMessageForTranslation))
                {
                    sourceLanguage = "JA";
                }

                var translateMessage = (isAnnouncement ? "☆☆☆Announcement☆☆☆ " : "") + text.Text;
                SendMessage(userName,
                    $"[{Settings.Default.BotName} {sourceLanguage}->{targetLanguage}] {translateMessage} (by {displayName})", userId);

                message = sourceLanguage == "JA" ? sourceMessage : translateMessage;
            }

            // 母国語で読み上げ
            if (!isAnnouncement)
            {
                BouyomiChanController.AddTalkTask(displayName, message, _secretKeys.BouyomiChan.Checked);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = "DeepL翻訳に失敗しています。";
            if (_irc != null)
            {
                SendMessage(displayName,
                    $"[{Settings.Default.BotName}] @{_twitchUserName} {errorMessage}", userId);
                if (_secretKeys.BouyomiChan.Checked && !isAnnouncement)
                {
                    BouyomiChanController.AddTalkTask(_twitchUserName, errorMessage,
                        _secretKeys.BouyomiChan.Checked);
                }
            }

            LogController.OutputLog($"<Error> {ex.Message}");
        }
    }
}