using System.Collections.Generic;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using FaraBotModerator.Enums;

namespace FaraBotModerator.Controllers;

/// <summary>
/// テストイベント送信を担当するコントローラー
/// </summary>
public class TwitchTestEventController
{
    private const string TestUserName = "game_fara_dev";
    private const string TestChannelUrl = "https://twitch.tv/game_fara_dev";

    private readonly TwitchClientController _twitchClientController;
    private readonly BouyomiChanController _bouyomiChanController;
    private readonly SecretKeyModel _secretKeys;

    /// <summary>
    /// TwitchTestEventController のコンストラクタ
    /// </summary>
    /// <param name="twitchClientController">Twitchクライアントコントローラー</param>
    /// <param name="bouyomiChanController">棒読みちゃんコントローラー</param>
    /// <param name="secretKeys">設定情報</param>
    public TwitchTestEventController(TwitchClientController twitchClientController, BouyomiChanController bouyomiChanController, SecretKeyModel secretKeys)
    {
        _twitchClientController = twitchClientController;
        _bouyomiChanController = bouyomiChanController;
        _secretKeys = secretKeys;
    }

    /// <summary>
    /// テストイベントを送信する共通メソッド
    /// </summary>
    /// <param name="userName">テストユーザー名</param>
    /// <param name="messageTemplate">メッセージテンプレート</param>
    /// <param name="replacements">プレースホルダー置換辞書</param>
    /// <param name="bouyomiMessage">棒読みちゃん用メッセージ（nullの場合は読み上げなし）</param>
    /// <param name="logMessage">ログ出力メッセージ</param>
    /// <param name="eventType">イベントタイプ</param>
    private void SendTestEvent(string userName, string messageTemplate, Dictionary<string, string> replacements,
        string? bouyomiMessage, string logMessage, TwitchEventEnum eventType)
    {
        var message = messageTemplate;
        foreach (var (key, value) in replacements)
        {
            message = message.Replace(key, value);
        }

        _twitchClientController.SendTestMessage(userName, $"[{Settings.Default.BotName} Test] {message}");

        if (bouyomiMessage != null)
        {
            BouyomiChanController.AddEventTalkTask(bouyomiMessage, _secretKeys.BouyomiChan.Checked);
        }

        LogController.OutputLog(logMessage, eventType);
    }

    /// <summary>
    /// テスト用のフォローイベントを送信
    /// </summary>
    public void TestFollowEvent()
    {
        SendTestEvent(
            TestUserName,
            _secretKeys.Event.Follow.Message,
            new Dictionary<string, string>
            {
                { "{followerName}", TestUserName },
                { "{followerChannelUrl}", TestChannelUrl }
            },
            $"{TestUserName}さんがFollowしました",
            $"<Follow Test> Name: {TestUserName}, URL: {TestChannelUrl}",
            TwitchEventEnum.Follow
        );
    }

    /// <summary>
    /// テスト用のRaidイベントを送信
    /// </summary>
    public void TestRaidEvent()
    {
        SendTestEvent(
            TestUserName,
            _secretKeys.Event.Raid.Message,
            new Dictionary<string, string>
            {
                { "{raiderName}", TestUserName },
                { "{raiderChannelUrl}", TestChannelUrl }
            },
            $"{TestUserName}さんにRaidされました",
            $"<Raid Test> Name: {TestUserName}, URL: {TestChannelUrl}",
            TwitchEventEnum.Raid
        );
    }

    /// <summary>
    /// テスト用のサブスクイベントを送信
    /// </summary>
    public void TestSubscriptionEvent()
    {
        SendTestEvent(
            TestUserName,
            _secretKeys.Event.Subscription.Message,
            new Dictionary<string, string>
            {
                { "{subscriberName}", TestUserName },
                { "{totalSubscriptionMonth}", "1" }
            },
            $"{TestUserName}さんサブスクありがとうございます",
            $"<Subscription Test> Name: {TestUserName}",
            TwitchEventEnum.Subscriber
        );
    }

    /// <summary>
    /// テスト用のBitsイベントを送信
    /// </summary>
    public void TestBitsEvent()
    {
        const int bitsAmount = 100;
        SendTestEvent(
            TestUserName,
            _secretKeys.Event.Bits.Message,
            new Dictionary<string, string>
            {
                { "{bitsSendUserName}", TestUserName },
                { "{bitsAmount}", bitsAmount.ToString() }
            },
            $"{TestUserName}さん{bitsAmount}bitsありがとうございます",
            $"<Bits Test> UserName: {TestUserName}, Amount: {bitsAmount}",
            TwitchEventEnum.Bits
        );
    }

    /// <summary>
    /// テスト用のギフトイベントを送信
    /// </summary>
    public void TestGiftEvent()
    {
        SendTestEvent(
            TestUserName,
            _secretKeys.Event.Gift.Message,
            new Dictionary<string, string>
            {
                { "{giftedUserName}", TestUserName }
            },
            $"{TestUserName}さんGiftありがとうございます",
            $"<Gift Test> Name: {TestUserName}",
            TwitchEventEnum.Gift
        );
    }

    /// <summary>
    /// テスト用のチャンネルポイントイベントを送信
    /// </summary>
    public void TestChannelPointEvent()
    {
        const string channelPointTitle = "Test Reward";
        const int channelPointCost = 100;
        SendTestEvent(
            TestUserName,
            _secretKeys.Event.ChannelPoint.Message,
            new Dictionary<string, string>
            {
                { "{channelPointUserName}", TestUserName },
                { "{channelPointTitle}", channelPointTitle },
                { "{channelPointCost}", channelPointCost.ToString() }
            },
            null, // ChannelPointは読み上げしない
            $"<ChannelPoint Test> UserName: {TestUserName}, Title: {channelPointTitle}",
            TwitchEventEnum.ChannelPoint
        );
    }
}
