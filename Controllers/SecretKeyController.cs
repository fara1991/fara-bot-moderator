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
    private const string SecretFile = "secrets.json";

    /// <summary>
    /// 設定ファイルから設定情報を読み込みます。ファイルが存在しない場合は新規作成します。
    /// </summary>
    /// <returns>読み込まれた設定情報モデル</returns>
    public static SecretKeyModel LoadKeys()
    {
        SecretKeyModel? secretKeys;
        if (!File.Exists(SecretFile)) CreateKeys();

        using (var file = File.OpenText(SecretFile))
        {
            var jsonData = file.ReadToEnd();
            secretKeys = JsonSerializer.Deserialize<SecretKeyModel>(jsonData);
        }

        if (secretKeys is null)
        {
            var message = "Secret Keys don't initialize.";
            LogController.OutputLog(message);
            throw new FileFormatException(message);
        }

        return secretKeys;
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
                    Message = "{followerName}, thanks follow gamefa16Hi. Follower Channel URL: {followerChannelUrl}"
                },
                Raid = new ReactionRaidEvent
                {
                    Checked = true,
                    Message = "Welcome raiders, thanks raid {raiderName} gamefa16Hi. Channel URL: {raiderChannelUrl}"
                },
                Subscription = new ReactionSubscriptionEvent
                {
                    Checked = true,
                    Message = "{subscriberName}, thanks subscription {totalSubscriptionMonth} time gamefa16Hi"
                },
                Bits = new ReactionBitsEvent
                {
                    Checked = true,
                    Message = "{bitsSendUserName}, thanks {bitsAmount} bits (total {totalBitsAmount}) gamefa16Hi"
                },
                Gift = new ReactionGiftEvent
                {
                    Checked = true,
                    Message = "{giftedUserName}, thanks gift present gamefa16Hi"
                },
                ChannelPoint = new ReactionChannelPointEvent
                {
                    Checked = true,
                    Message = "{channelPointUserName} use channelPoint of {channelPointTitle} gamefa16Hi"
                }
            }
        };

        SaveKeys(secretKeys);
    }
}