using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using FaraBotModerator.Models;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 設定情報（APIキーやメッセージ設定など）の保存と読み込みを管理するクラス
/// </summary>
public static class SecretKeyController
{
    private static readonly string SecretDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
    /// <summary>
    /// secrets.json の読み書きを 1 つずつにするロック（画面の保存とチャットの <c>format set</c> が同時に書いて、ファイルが壊れた状態を読まないため）
    /// </summary>
    private static readonly object FileLock = new();
    private static readonly string SecretFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "secrets.json");

    /// <summary>
    /// 設定ファイルから設定情報を読み込みます。ファイルが存在しない場合は新規作成します。
    /// </summary>
    /// <returns>読み込まれた設定情報モデル</returns>
    public static SecretKeyModel LoadKeys()
    {
        SecretKeyModel? secretKeys;
        lock (FileLock)
        {
            if (!File.Exists(SecretFile)) CreateKeys();

            using var file = File.OpenText(SecretFile);
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
        lock (FileLock)
        {
            WriteKeys(secretKeys);
        }
    }

    private static void WriteKeys(SecretKeyModel secretKeys)
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };
        var jsonData = JsonSerializer.Serialize(secretKeys, options);
        // \003Cだけ変換できないので手動で変換
        WriteAtomically(jsonData.Replace("\\u003C", "<"));
    }

    /// <summary>
    /// 一時ファイルに書いてから置き換えます。書き込みの途中で強制終了・ディスク満杯になっても、今の secrets.json は壊れません。
    /// </summary>
    private static void WriteAtomically(string jsonData)
    {
        var directory = Path.GetDirectoryName(SecretFile) ?? "";
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        var temp = SecretFile + ".tmp";
        File.WriteAllText(temp, jsonData + Environment.NewLine, Encoding.UTF8);
        File.Move(temp, SecretFile, true);
    }

    /// <summary>
    /// ポケモンの登録形式（<c>pokemon.battleFormat</c>）だけを書き換えて保存します（null は未登録）。
    /// ファイルの他の項目は、読んだ内容のまま（モデルに無い項目も）残します。
    /// </summary>
    /// <param name="battleFormat">"Singles" / "Doubles"。null で未登録に戻す</param>
    public static void SavePokemonBattleFormat(string? battleFormat)
    {
        lock (FileLock)
        {
            // 無ければ作ってから読む（LoadKeys が初期値のファイルを作る）
            LoadKeys();
            var root = JsonNode.Parse(File.ReadAllText(SecretFile)) as JsonObject
                       ?? throw new FileFormatException("secrets.json の形式が正しくありません");
            if (root["pokemon"] is not JsonObject pokemon)
            {
                pokemon = new JsonObject();
                root["pokemon"] = pokemon;
            }
            pokemon["battleFormat"] = battleFormat;

            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = true
            };
            WriteAtomically(root.ToJsonString(options).Replace("\\u003C", "<"));
        }
    }

    /// <summary>
    /// 初期設定ファイルを作成します。
    /// </summary>
    private static void CreateKeys()
    {
        if (!Directory.Exists(SecretDirectory)) Directory.CreateDirectory(SecretDirectory);
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
            },
            BeatSaber = new BeatSaberModel
            {
                UserDataPath = @"%USERPROFILE%\BSManager\BSInstances\1.29.1\UserData"
            },
            Pokemon = new PokemonModel
            {
                BattleFormat = null,
                DataSource = "Production",
                SyncKey = ""
            }
        };

        SaveKeys(secretKeys);
    }
}