using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using FaraBotModerator.Models;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 設定情報（APIキーやメッセージ設定など）の保存と読み込みを管理するクラス
/// </summary>
public static class SecretKeyController
{
    private static readonly string SecretFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "secrets.json");

    /// <summary>
    /// 設定ファイルから設定情報を読み込みます。ファイルが存在しない場合は新規作成します。
    /// </summary>
    /// <returns>読み込まれた設定情報モデル</returns>
    public static SecretKeyModel LoadKeys()
    {
        if (!File.Exists(SecretFile)) CreateKeys();

        SecretKeyModel? secretKeys;
        using (var file = File.OpenText(SecretFile))
        {
            var jsonData = file.ReadToEnd();
            secretKeys = JsonSerializer.Deserialize<SecretKeyModel>(jsonData);
        }

        if (secretKeys is not null) return secretKeys;

        const string message = "Secret Keys don't initialize.";
        LogController.OutputLog(message);
        throw new FileFormatException(message);
    }

    /// <summary>
    /// 設定情報をファイルに保存します。
    /// </summary>
    /// <param name="secretKeys">保存する設定情報モデル</param>
    public static void SaveKeys(SecretKeyModel secretKeys)
    {
        using var writer = new StreamWriter(SecretFile, false, Encoding.UTF8);
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };
        var jsonData = JsonSerializer.Serialize(secretKeys, options);
        // \003Cだけ変換できないので手動で変換
        writer.WriteLine(jsonData.Replace("\\u003C", "<"));
    }

    /// <summary>
    /// 初期設定ファイルを作成します。
    /// </summary>
    private static void CreateKeys()
    {
        var secretKeys = new SecretKeyModel
        {
            Twitch = new TwitchSecretKeyModel
            {
                Client = new TwitchClientKeyModel
                {
                    UserName = "",
                    DisplayName = ""
                },
                Api = new TwitchApiKeyModel
                {
                    ClientId = "",
                    Secret = ""
                }
            },
            DeepL = new DeepLKeyModel
            {
                ApiKey = ""
            },
            BouyomiChan = new BouyomiChanModel
            {
                Checked = true
            },
            Event = new ReactionEventModel
            {
                Follow = new ReactionFollowEvent
                {
                    Checked = true,
                    Message = "{followerName}, thanks follow. Follower Channel URL: {followerChannelUrl}"
                },
                Raid = new ReactionRaidEvent
                {
                    Checked = true,
                    Message = "Welcome raiders, thanks raid {raiderName}. Channel URL: {raiderChannelUrl}"
                },
                Subscription = new ReactionSubscriptionEvent
                {
                    Checked = true,
                    Message = "{subscriberName}, thanks subscription {totalSubscriptionMonth} time."
                },
                Bits = new ReactionBitsEvent
                {
                    Checked = true,
                    Message = "{bitsSendUserName}, thanks {bitsAmount} bits (total {totalBitsAmount})."
                },
                Gift = new ReactionGiftEvent
                {
                    Checked = true,
                    Message = "{giftedUserName}, thanks gift present."
                },
                ChannelPoint = new ReactionChannelPointEvent
                {
                    Checked = true,
                    Message = "{channelPointUserName} use channelPoint of {channelPointTitle}."
                }
            },
            CycleMessage = new CycleMessageModel
            {
                Timer1 = new CycleTimerModel { Checked = false, Interval = 60, Message = "" },
                Timer2 = new CycleTimerModel { Checked = false, Interval = 60, Message = "" },
                Timer3 = new CycleTimerModel { Checked = false, Interval = 60, Message = "" },
                Timer4 = new CycleTimerModel { Checked = false, Interval = 60, Message = "" }
            },
            FixedMessage = new FixedMessageModel
            {
                Timer1 = new FixedTimerModel { Checked = false, DatetimeString = "2023/1/1 00:00:00", Message = "" },
                Timer2 = new FixedTimerModel { Checked = false, DatetimeString = "2023/1/1 00:00:00", Message = "" },
                Timer3 = new FixedTimerModel { Checked = false, DatetimeString = "2023/1/1 00:00:00", Message = "" },
                Timer4 = new FixedTimerModel { Checked = false, DatetimeString = "2023/1/1 00:00:00", Message = "" }
            }
        };

        SaveKeys(secretKeys);
    }
}