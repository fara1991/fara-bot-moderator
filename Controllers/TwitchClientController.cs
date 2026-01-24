using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FaraBotModerator.Enums;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Client.Extensions;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Enums;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Models;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;
using DeepL.Model;
using OnLogArgs = TwitchLib.Client.Events.OnLogArgs;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch チャットクライアント経由の操作（メッセージ送信、イベント受信など）を管理するコントローラー
/// </summary>
public class TwitchClientController
{
    private readonly BouyomiChanController _bouyomiChanController = new();
    private readonly Queue<ChatModel> _chatDataQueue = new();
    private readonly SecretKeyModel _secretKeys;
    private readonly TwitchApiController _twitchApiController;
    private readonly TwitchClient _twitchClient;
    private readonly UniqueChannelPointController _uniqueChannelPointController = new();
    private readonly TwitchTranslationController _twitchTranslationController;
    private readonly string _twitchUserName;
    private readonly string _twitchUserDisplayName;

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

        var client = _secretKeys.Twitch.Client;
        var credentials = new ConnectionCredentials(client.UserName, Settings.Default.AccessToken);
        var clientOptions = new ClientOptions
        {
            MessagesAllowedInPeriod = 750,
            ThrottlingPeriod = TimeSpan.FromSeconds(30),
            ClientType = ClientType.Chat
        };
        var customClient = new WebSocketClient(clientOptions);
        _twitchClient = new TwitchClient(customClient);
        _twitchClient.Initialize(credentials, client.UserName);

        _twitchClient.OnLog += TwitchClientOnLog;
        _twitchClient.OnJoinedChannel += TwitchClientOnJoinedChannel;
        _twitchClient.OnUserJoined += TwitchClientOnUserJoined;
        _twitchClient.OnMessageReceived += TwitchClientOnMessageReceived;
        _twitchClient.OnNewSubscriber += TwitchClientOnNewSubscriber;
        _twitchClient.OnPrimePaidSubscriber += TwitchClientOnPrimePaidSubscriber;
        _twitchClient.OnReSubscriber += TwitchClientOnReSubscriber;
        _twitchClient.OnGiftedSubscription += TwitchClientOnGiftedSubscription;
        _twitchClient.OnRaidNotification += TwitchClientOnRaidNotification;
        _twitchClient.OnConnected += TwitchClientOnConnected;
        _twitchClient.OnDisconnected += TwitchClientOnDisconnected;
        _twitchClient.OnAnnouncement += TwitchClientOnAnnouncement;
        // OnDisconnectedはタイムラグの関係で実装しない

        _twitchUserName = client.UserName;
        _twitchUserDisplayName = client.DisplayName;
    }

    /// <summary>
    /// Twitch チャットクライアントを接続します。
    /// </summary>
    public void Connect()
    {
        _twitchClient.Connect();
    }

    /// <summary>
    /// Twitch チャットクライアントを切断します。
    /// </summary>
    public void Disconnect()
    {
        SendMessage(_twitchUserName, $"Logout {Settings.Default.BotName}.");
        LogController.OutputLog($"Logout {Settings.Default.BotName}.");

        _twitchTranslationController.Dispose();
        _twitchClient.Disconnect();
    }

    /// <summary>
    /// アプリケーションからTwitchへチャットメッセージを送信し、翻訳処理も行います。
    /// </summary>
    /// <param name="message">送信するメッセージ</param>
    public async void SendApplicationMessage(string message)
    {
        if (message.Equals("")) return;
        SendMessage(_twitchUserName, message);
        await SendMessageTranslationAsync(_twitchUserName, _twitchUserDisplayName, message);
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
        _twitchClient.SendMessage(_twitchUserName, message);
        AddChatListData(userName, message, userId);
    }

    /// <summary>
    /// キューからチャットデータを1件取り出します。
    /// </summary>
    /// <returns>チャットデータ。キューが空の場合はnull</returns>
    public ChatModel? PickChatData()
    {
        return _chatDataQueue.Count > 0 ? _chatDataQueue.Dequeue() : null;
    }

    /// <summary>
    /// チャットデータをUI表示用キューに追加します。アイコンURLの取得も行います。
    /// </summary>
    /// <param name="userName">ユーザー名</param>
    /// <param name="message">メッセージ内容</param>
    /// <param name="userId">ユーザーID</param>
    private async void AddChatListData(string userName, string message, string userId = "")
    {
        var chatUserIconUrl = userId != ""
            ? await _twitchApiController.GetTwitchIconUrlByIdAsync(userId)
            : await _twitchApiController.GetTwitchIconUrlByLoginAsync(userName);
        var chatModel = new ChatModel {Name = userName, Chat = message, Icon = chatUserIconUrl};
        _chatDataQueue.Enqueue(chatModel);
    }

    /// <summary>
    /// TwitchClientのログ出力イベントハンドラ
    /// </summary>
    private void TwitchClientOnLog(object? sender, OnLogArgs e)
    {
        LogController.OutputLog($@"{e.BotUsername} - {e.Data}");
    }

    /// <summary>
    /// Twitch接続完了時のイベントハンドラ
    /// </summary>
    private void TwitchClientOnConnected(object? sender, OnConnectedArgs e)
    {
        IsConnected = true;
        LogController.OutputLog($@"Connected to {_twitchUserName}");
    }

    /// <summary>
    /// Twitch切断時のイベントハンドラ
    /// </summary>
    private void TwitchClientOnDisconnected(object? sender, OnDisconnectedEventArgs e)
    {
        IsConnected = false;
        LogController.OutputLog($"Disconnected to {_twitchUserName}");
    }

    /// <summary>
    /// /announcement を実行したときの処理
    /// </summary>
    private async void TwitchClientOnAnnouncement(object? sender, OnAnnouncementArgs e)
    {
        try
        {
            var userName = e.Channel;
            var displayName = _secretKeys.Twitch.Client.DisplayName;
            var sourceMessage = e.Announcement.Message;
            await SendMessageTranslationAsync(userName, displayName, sourceMessage, true);
        }
        catch (Exception ex)
        {
            LogController.OutputLog(e.Announcement.Message);
            LogController.OutputLog($"<Error> {ex.Message}");
        }
    }

    /// <summary>
    /// チャンネル参加時のイベントハンドラ
    /// </summary>
    private void TwitchClientOnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        SendMessage(e.Channel, $"Login {Settings.Default.BotName}.");
        LogController.OutputLog($"Login {Settings.Default.BotName}.");

        SendMessage(e.Channel,
            Settings.Default.BotName + (_secretKeys.BouyomiChan.Checked ? " " : " Not ") + "Connecting BouyomiChan.");
    }

    /// <summary>
    /// ユーザーがチャンネルに参加した時のイベントハンドラ
    /// </summary>
    private void TwitchClientOnUserJoined(object? sender, OnUserJoinedArgs e)
    {
        LogController.OutputLog($"<Join> {e.Username}", TwitchEventEnum.Join);
    }

    /// <summary>
    /// EventSubからのフォローイベントを処理し、メッセージ送信と読み上げを行います。
    /// </summary>
    /// <param name="e">フォローイベント引数</param>
    public async void TwitchEventSubOnFollow(ChannelFollowArgs e)
    {
        var followEvent = e.Notification.Payload.Event;
        var followerName = followEvent.UserName;
        var followerChannelUrl = $"https://twitch.tv/{followEvent.UserLogin}";
        var message = _secretKeys.Event.Follow.Message.Replace("{followerName}", followerName)
            .Replace("{followerChannelUrl}", followerChannelUrl);

        SendMessage(followerName, $"[{Settings.Default.BotName}] {message}");
        _bouyomiChanController.AddEventTalkTask($"{followerName}さんがFollowしました", _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Follow> Name: {followerName}, URL: {followerChannelUrl}",
            TwitchEventEnum.Follow);
    }


    /// <summary>
    /// EventSubからのBitsイベントを処理し、メッセージ送信と読み上げを行います。
    /// </summary>
    /// <param name="e">Bitsイベント引数</param>
    public void SendBitsEventSubMessage(ChannelCheerArgs e)
    {
        var bitsEvent = e.Notification.Payload.Event;
        var bitsSendUserName = bitsEvent.UserName;
        if (bitsSendUserName == null) return;

        var bitsAmount = bitsEvent.Bits;
        var message = _secretKeys.Event.Bits.Message.Replace("{bitsAmount}", bitsAmount.ToString())
            .Replace("{bitsSendUserName}", bitsSendUserName);
        SendMessage(bitsSendUserName, $"[{Settings.Default.BotName}] {message}");

        _bouyomiChanController.AddEventTalkTask($"{bitsSendUserName}さん{bitsAmount}bitsありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Bits> UserName: {bitsSendUserName}, Amount: {bitsAmount}", TwitchEventEnum.Bits);
    }

    /// <summary>
    /// EventSubからのチャンネルポイント交換イベントを処理します。
    /// </summary>
    /// <param name="e">チャンネルポイント交換イベント引数</param>
    public void SendChannelPointEventSubMessage(ChannelPointsCustomRewardRedemptionArgs e)
    {
        var channelPointEvent = e.Notification.Payload.Event;
        var channelPointTitle = channelPointEvent.Reward.Title;
        var channelPointCost = channelPointEvent.Reward.Cost;
        var channelPointUserId = channelPointEvent.UserLogin;
        var message = _secretKeys.Event.ChannelPoint.Message
            .Replace("{channelPointCost}", channelPointCost.ToString())
            .Replace("{channelPointTitle}", channelPointTitle)
            .Replace("{channelPointUserName}", channelPointUserId);

        // ChannelPointは読み上げしない
        SendMessage(channelPointUserId, $"[{Settings.Default.BotName}] {message}");
        LogController.OutputLog($"<ChannelPoint> UserName: {channelPointUserId}, Title: {channelPointTitle}",
            TwitchEventEnum.ChannelPoint);

        // チャンネルポイント固有の処理は別で行う
        SendModeratorMessage(_uniqueChannelPointController.Exec(channelPointUserId, channelPointTitle));
    }

    /// <summary>
    /// 別チャンネルからRaidが来たときの処理を行います。
    /// </summary>
    public async void TwitchClientOnRaidNotification(object? sender, OnRaidNotificationArgs e)
    {
        var raiderName = e.RaidNotification.MsgParamLogin;
        var raiderChannelUrl = $"https://twitch.tv/{raiderName}";
        var message = _secretKeys.Event.Raid.Message.Replace("{raiderName}", raiderName)
            .Replace("{raiderChannelUrl}", raiderChannelUrl);
        SendMessage(raiderName, $"[{Settings.Default.BotName}] {message}");
        _bouyomiChanController.AddEventTalkTask($"{raiderName}さんにRaidされました", _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Raid> Name: {raiderName}, URL: {raiderChannelUrl}", TwitchEventEnum.Raid);

        await _twitchApiController.SendShoutoutAsync(raiderName);
    }

    /// <summary>
    /// 新規サブスクライブ時の処理を行います。
    /// </summary>
    public void TwitchClientOnNewSubscriber(object? sender, OnNewSubscriberArgs e)
    {
        var subscriberName = e.Subscriber.DisplayName;
        var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
            .Replace("{totalSubscriptionMonth}", "1");
        SendMessage(subscriberName, $"[{Settings.Default.BotName}] {message}", e.Subscriber.UserId);
        _bouyomiChanController.AddEventTalkTask($"{subscriberName}さんサブスクありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<New Subscriber> Name: {subscriberName}", TwitchEventEnum.Subscriber);
    }

    /// <summary>
    /// Primeサブスクライブ時の処理を行います。
    /// </summary>
    private void TwitchClientOnPrimePaidSubscriber(object? sender, OnPrimePaidSubscriberArgs e)
    {
        var subscriberName = e.PrimePaidSubscriber.DisplayName;
        var months = e.PrimePaidSubscriber.MsgParamCumulativeMonths ?? "1";
        var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
            .Replace("{totalSubscriptionMonth}", months);
        SendMessage(subscriberName, $"[{Settings.Default.BotName}] {message}", e.PrimePaidSubscriber.UserId);
        _bouyomiChanController.AddEventTalkTask($"{subscriberName}さんサブスクありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Prime Subscriber> Name: {subscriberName}", TwitchEventEnum.Subscriber);
    }

    /// <summary>
    /// 継続サブスクライブ（再サブスク）時の処理を行います。
    /// </summary>
    private void TwitchClientOnReSubscriber(object? sender, OnReSubscriberArgs e)
    {
        try
        {
            // debugのために、Logを残しておく
            var subscriberName = e.ReSubscriber.DisplayName;
            LogController.OutputLog($"<Subscriber> Name: {subscriberName}",
                TwitchEventEnum.Subscriber);
            var totalSubscriptionMonth = e.ReSubscriber.MsgParamCumulativeMonths ?? "1";
            LogController.OutputLog($"<Subscriber> total: {totalSubscriptionMonth} time.",
                TwitchEventEnum.Subscriber);
            var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
                .Replace("{totalSubscriptionMonth}", totalSubscriptionMonth);
            LogController.OutputLog($"<Subscriber> Message: {message}",
                TwitchEventEnum.Subscriber);
            SendMessage(subscriberName, $"[{Settings.Default.BotName}] {message}", e.ReSubscriber.UserId);
            _bouyomiChanController.AddEventTalkTask($"{subscriberName}さん{totalSubscriptionMonth}か月目のサブスクありがとうございます",
                    _secretKeys.BouyomiChan.Checked);
            LogController.OutputLog($"<Subscriber> Name: {subscriberName}, total: {totalSubscriptionMonth} time.",
                TwitchEventEnum.Subscriber);
        }
        catch (Exception ex)
        {
            _bouyomiChanController.AddEventTalkTask("翻訳に失敗しました", _secretKeys.BouyomiChan.Checked);
            LogController.OutputLog($"<Subscriber> Error: {ex.Message}", TwitchEventEnum.Subscriber);
        }
    }

    /// <summary>
    /// サブスクギフト受信時の処理を行います。
    /// </summary>
    public void TwitchClientOnGiftedSubscription(object? sender, OnGiftedSubscriptionArgs e)
    {
        var giftedUserName = e.GiftedSubscription.DisplayName;
        var url = $"https://twitch.tv/{giftedUserName}";
        var message = _secretKeys.Event.Gift.Message.Replace("{giftedUserName}", giftedUserName);
        SendMessage(giftedUserName, $"[{Settings.Default.BotName}] {message}");
        _bouyomiChanController.AddEventTalkTask($"{giftedUserName}さんGiftありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Gift> Name: {giftedUserName} URL: {url}", TwitchEventEnum.Gift);
    }

    /// <summary>
    /// メッセージ受信時の処理を行います。バッドワードのタイムアウトや翻訳処理を含みます。
    /// </summary>
    private async void TwitchClientOnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        // Twitchチャット上のコメントのみ受け取れる
        if (e.ChatMessage.Message.Contains(@"badword"))
            _twitchClient.TimeoutUser(e.ChatMessage.Channel, e.ChatMessage.Username, TimeSpan.FromMinutes(30),
                "Bad word! 30 minute timeout!");
        try
        {
            var userName = e.ChatMessage.Username;
            var displayName = e.ChatMessage.DisplayName;
            var sourceMessage = e.ChatMessage.Message;
            await SendMessageTranslationAsync(userName, displayName, sourceMessage, false, e.ChatMessage.UserId);
        }
        catch (Exception ex)
        {
            LogController.OutputLog(e.ChatMessage.Message);
            LogController.OutputLog($"<Error> {ex.Message}");
        }
    }

    /// <summary>
    /// メッセージの翻訳と読み上げタスクの追加を行います。
    /// </summary>
    private async Task SendMessageTranslationAsync(string userName, string displayName, string sourceMessage,
        bool isAnnouncement = false, string userId = "")
    {
        if (!_twitchTranslationController.IsTargetTranslationWord(sourceMessage)) return;

        var beatSaberRegexMessage = TextRegexController.LoadBsrChat(sourceMessage);
        if (sourceMessage != beatSaberRegexMessage && !isAnnouncement)
        {
            // BeatSaber関連は読み上げだけ行う
            _bouyomiChanController.AddTalkTask(userName, beatSaberRegexMessage, _secretKeys.BouyomiChan.Checked);
            return;
        }

        await MessageTranslationProcessAsync(sourceMessage, userName, displayName, isAnnouncement, userId);
    }

    /// <summary>
    /// 実際の翻訳処理（DeepL）と、翻訳結果の送信および読み上げ処理を行います。
    /// </summary>
    private async Task MessageTranslationProcessAsync(string sourceMessage, string userName, string displayName,
        bool isAnnouncement = false, string userId = "")
    {
        try
        {
            // URLのみは翻訳しない
            var message = sourceMessage;
            if (!_twitchTranslationController.IsOnlyUrl(message))
            {
                var targetLanguage = _twitchTranslationController.IsJapanese(sourceMessage)
                    ? "EN-US"
                    : "JA";

            // Emote文字列は翻訳と読み上げで使わないので削除する
                var sourceMessageForTranslation = _twitchTranslationController.RemoveEmotes(sourceMessage, _twitchClient);
                if (string.IsNullOrEmpty(sourceMessageForTranslation))
                {
                    _bouyomiChanController.AddTalkTask(displayName, "", _secretKeys.BouyomiChan.Checked);
                    return;
                }

                var text = await _twitchTranslationController.TranslateAsync(sourceMessageForTranslation, targetLanguage);
                var sourceLanguage = text.DetectedSourceLanguageCode.ToUpper();
                // 誤検知対策: 日本語なのに他言語と判定された場合
                if (sourceLanguage != "JA" && _twitchTranslationController.IsJapanese(sourceMessageForTranslation))
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
                _bouyomiChanController.AddTalkTask(displayName, message, _secretKeys.BouyomiChan.Checked);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = "DeepL翻訳に失敗しています。";
            SendMessage(displayName,
                $"[{Settings.Default.BotName}] @{_twitchClient.TwitchUsername} {errorMessage}", userId);
            if (_secretKeys.BouyomiChan.Checked && !isAnnouncement)
            {
                _bouyomiChanController.AddTalkTask(_twitchClient.TwitchUsername, errorMessage,
                        _secretKeys.BouyomiChan.Checked);
            }

            LogController.OutputLog($"<Error> {ex.Message}");
        }
    }
}