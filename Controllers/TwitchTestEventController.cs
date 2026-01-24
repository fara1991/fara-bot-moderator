using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using FaraBotModerator.Enums;

namespace FaraBotModerator.Controllers;

/// <summary>
/// テストイベント送信を担当するコントローラー
/// </summary>
public class TwitchTestEventController
{
    private readonly TwitchClientController _twitchClientController;
    private readonly BouyomiChanController _bouyomiChanController;
    private readonly SecretKeyModel _secretKeys;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="twitchClientController"></param>
    /// <param name="bouyomiChanController"></param>
    /// <param name="secretKeys"></param>
    public TwitchTestEventController(TwitchClientController twitchClientController, BouyomiChanController bouyomiChanController, SecretKeyModel secretKeys)
    {
        _twitchClientController = twitchClientController;
        _bouyomiChanController = bouyomiChanController;
        _secretKeys = secretKeys;
    }

    /// <summary>
    /// テスト用のフォローイベントを送信
    /// </summary>
    public void TestFollowEvent()
    {
        const string followerName = "game_fara_dev";
        const string followerChannelUrl = "https://twitch.tv/game_fara_dev";
        var message = _secretKeys.Event.Follow.Message.Replace("{followerName}", followerName)
            .Replace("{followerChannelUrl}", followerChannelUrl);

        _twitchClientController.SendTestMessage(followerName, $"[{Settings.Default.BotName} Test] {message}");
        BouyomiChanController.AddEventTalkTask($"{followerName}さんがFollowしました", _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Follow Test> Name: {followerName}, URL: {followerChannelUrl}",
            TwitchEventEnum.Follow);
    }

    /// <summary>
    /// テスト用のRaidイベントを送信
    /// </summary>
    public void TestRaidEvent()
    {
        const string raiderName = "game_fara_dev";
        const string raiderChannelUrl = "https://twitch.tv/game_fara_dev";
        var message = _secretKeys.Event.Raid.Message.Replace("{raiderName}", raiderName)
            .Replace("{raiderChannelUrl}", raiderChannelUrl);
        _twitchClientController.SendTestMessage(raiderName, $"[{Settings.Default.BotName} Test] {message}");
        BouyomiChanController.AddEventTalkTask($"{raiderName}さんにRaidされました", _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Raid Test> Name: {raiderName}, URL: {raiderChannelUrl}", TwitchEventEnum.Raid);
    }

    /// <summary>
    /// テスト用のサブスクイベントを送信
    /// </summary>
    public void TestSubscriptionEvent()
    {
        const string subscriberName = "game_fara_dev";
        var message = _secretKeys.Event.Subscription.Message.Replace("{subscriberName}", subscriberName)
            .Replace("{totalSubscriptionMonth}", "1");
        _twitchClientController.SendTestMessage(subscriberName, $"[{Settings.Default.BotName} Test] {message}");
        BouyomiChanController.AddEventTalkTask($"{subscriberName}さんサブスクありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Subscription Test> Name: {subscriberName}", TwitchEventEnum.Subscriber);
    }

    /// <summary>
    /// テスト用のBitsイベントを送信
    /// </summary>
    public void TestBitsEvent()
    {
        const string bitsSendUserName = "game_fara_dev";
        const int bitsAmount = 100;
        var message = _secretKeys.Event.Bits.Message.Replace("{bitsAmount}", bitsAmount.ToString())
            .Replace("{bitsSendUserName}", bitsSendUserName);

        _twitchClientController.SendTestMessage(bitsSendUserName, $"[{Settings.Default.BotName} Test] {message}");

        BouyomiChanController.AddEventTalkTask($"{bitsSendUserName}さん{bitsAmount}bitsありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Bits Test> UserName: {bitsSendUserName}, Amount: {bitsAmount}", TwitchEventEnum.Bits);
    }

    /// <summary>
    /// テスト用のギフトイベントを送信
    /// </summary>
    public void TestGiftEvent()
    {
        const string giftedUserName = "game_fara_dev";
        var message = _secretKeys.Event.Gift.Message.Replace("{giftedUserName}", giftedUserName);
        _twitchClientController.SendTestMessage(giftedUserName, $"[{Settings.Default.BotName} Test] {message}");
        BouyomiChanController.AddEventTalkTask($"{giftedUserName}さんGiftありがとうございます",
                _secretKeys.BouyomiChan.Checked);
        LogController.OutputLog($"<Gift Test> Name: {giftedUserName}", TwitchEventEnum.Gift);
    }

    /// <summary>
    /// テスト用のチャンネルポイントイベントを送信
    /// </summary>
    public void TestChannelPointEvent()
    {
        const string channelPointTitle = "Test Reward";
        const int channelPointCost = 100;
        const string channelPointUserName = "game_fara_dev";
        var message = _secretKeys.Event.ChannelPoint.Message
            .Replace("{channelPointCost}", channelPointCost.ToString())
            .Replace("{channelPointTitle}", channelPointTitle)
            .Replace("{channelPointUserName}", channelPointUserName);

        _twitchClientController.SendTestMessage(channelPointUserName, $"[{Settings.Default.BotName} Test] {message}");
        LogController.OutputLog($"<ChannelPoint Test> UserName: {channelPointUserName}, Title: {channelPointTitle}",
            TwitchEventEnum.ChannelPoint);
    }
}
