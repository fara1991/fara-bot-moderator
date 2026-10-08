using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// 設定情報全体を保持するルートモデル
/// </summary>
public class SecretKeyModel
{
    /// <summary>
    /// Twitch関連の認証情報
    /// </summary>
    [JsonPropertyName("twitch")]
    public TwitchSecretKeyModel Twitch { get; init; } = new();

    /// <summary>
    /// DeepL APIキー情報
    /// </summary>
    [JsonPropertyName("deepL")]
    public DeepLKeyModel DeepL { get; init; } = new();

    /// <summary>
    /// 棒読みちゃん連携設定
    /// </summary>
    [JsonPropertyName("bouyomiChan")]
    public BouyomiChanModel BouyomiChan { get; init; } = new();

    /// <summary>
    /// 各種リアクションイベントの設定
    /// </summary>
    [JsonPropertyName("event")]
    public ReactionEventModel Event { get; init; } = new();

    /// <summary>
    /// 定期実行メッセージの設定
    /// </summary>
    [JsonPropertyName("cycleMessage")]
    public CycleMessageModel CycleMessage { get; init; } = new();

    /// <summary>
    /// 日時指定メッセージの設定
    /// </summary>
    [JsonPropertyName("fixedMessage")]
    public FixedMessageModel FixedMessage { get; init; } = new();

    /// <summary>
    /// BeatSaber関連の設定
    /// </summary>
    [JsonPropertyName("beatSaber")]
    public BeatSaberModel BeatSaber { get; init; } = new();
}

/// <summary>
/// BeatSaber関連の設定を保持するモデル
/// </summary>
public class BeatSaberModel
{
    /// <summary>
    /// BeatSaberのUserDataディレクトリパス
    /// </summary>
    [JsonPropertyName("userDataPath")]
    public string UserDataPath { get; init; } = "";
}

/// <summary>
/// Twitch関連の認証情報をまとめたモデル
/// </summary>
public class TwitchSecretKeyModel
{
    /// <summary>
    /// チャットクライアント用設定
    /// </summary>
    [JsonPropertyName("client")]
    public TwitchClientKeyModel Client { get; init; } = new();

    /// <summary>
    /// API連携用設定
    /// </summary>
    [JsonPropertyName("api")]
    public TwitchApiKeyModel Api { get; init; } = new();
}

/// <summary>
/// Twitchチャットクライアントの基本設定を保持するモデル
/// </summary>
public class TwitchClientKeyModel
{
    /// <summary>
    /// ユーザー名（ID）
    /// </summary>
    [JsonPropertyName("userName")]
    public string UserName { get; init; } = "";

    /// <summary>
    /// 表示名
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// Twitch APIの認証情報を保持するモデル
/// </summary>
public class TwitchApiKeyModel
{
    /// <summary>
    /// クライアントID
    /// </summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = "";

    /// <summary>
    /// クライアントシークレット
    /// </summary>
    [JsonPropertyName("secret")]
    public string Secret { get; init; } = "";
}

/// <summary>
/// DeepL APIのキー情報を保持するモデル
/// </summary>
public class DeepLKeyModel
{
    /// <summary>
    /// DeepL APIキー
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = "";
}

/// <summary>
/// 棒読みちゃんの接続設定を保持するモデル
/// </summary>
public class BouyomiChanModel
{
    /// <summary>
    /// 棒読みちゃん連携が有効かどうか
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool Checked { get; init; } = true;
}

/// <summary>
/// 各種通知イベント設定をまとめたモデル
/// </summary>
public class ReactionEventModel
{
    /// <summary>
    /// フォローイベント設定
    /// </summary>
    [JsonPropertyName("follow")]
    public ReactionFollowEvent Follow { get; init; } = new();

    /// <summary>
    /// レイドイベント設定
    /// </summary>
    [JsonPropertyName("raid")]
    public ReactionRaidEvent Raid { get; init; } = new();

    /// <summary>
    /// サブスクライブイベント設定
    /// </summary>
    [JsonPropertyName("subscription")]
    public ReactionSubscriptionEvent Subscription { get; init; } = new();

    /// <summary>
    /// Bitsイベント設定
    /// </summary>
    [JsonPropertyName("bits")]
    public ReactionBitsEvent Bits { get; init; } = new();

    /// <summary>
    /// サブスクギフトイベント設定
    /// </summary>
    [JsonPropertyName("gift")]
    public ReactionGiftEvent Gift { get; init; } = new();

    /// <summary>
    /// チャンネルポイントイベント設定
    /// </summary>
    [JsonPropertyName("channelPoint")]
    public ReactionChannelPointEvent ChannelPoint { get; init; } = new();
}

/// <summary>
/// 通知イベントモデルの共通インターフェース
/// </summary>
public interface IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    bool Checked { get; }
    /// <summary>
    /// 送信するメッセージ内容
    /// </summary>
    string Message { get; }
}

/// <summary>
/// フォロー通知の設定を保持するモデル
/// </summary>
public class ReactionFollowEvent : IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// レイド通知の設定を保持するモデル
/// </summary>
public class ReactionRaidEvent : IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// サブスク通知の設定を保持するモデル
/// </summary>
public class ReactionSubscriptionEvent : IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// Bits通知の設定を保持するモデル
/// </summary>
public class ReactionBitsEvent : IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// サブスクギフト通知の設定を保持するモデル
/// </summary>
public class ReactionGiftEvent : IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// チャンネルポイント通知の設定を保持するモデル
/// </summary>
public class ReactionChannelPointEvent : IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// 4つの定期メッセージタイマーを管理するモデル
/// </summary>
public class CycleMessageModel
{
    /// <summary>
    /// タイマー1
    /// </summary>
    [JsonPropertyName("timer1")]
    public CycleTimerModel Timer1 { get; init; } = new();

    /// <summary>
    /// タイマー2
    /// </summary>
    [JsonPropertyName("timer2")]
    public CycleTimerModel Timer2 { get; init; } = new();

    /// <summary>
    /// タイマー3
    /// </summary>
    [JsonPropertyName("timer3")]
    public CycleTimerModel Timer3 { get; init; } = new();

    /// <summary>
    /// タイマー4
    /// </summary>
    [JsonPropertyName("timer4")]
    public CycleTimerModel Timer4 { get; init; } = new();
}

/// <summary>
/// 定期メッセージタイマーの個別設定を保持するモデル
/// </summary>
public class CycleTimerModel
{
    /// <summary>
    /// 有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; init; }

    /// <summary>
    /// 実行間隔（分）
    /// </summary>
    [JsonPropertyName("interval")]
    public int Interval { get; init; } = 60;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

/// <summary>
/// 4つの日時指定タイマーを管理するモデル
/// </summary>
public class FixedMessageModel
{
    /// <summary>
    /// タイマー1
    /// </summary>
    [JsonPropertyName("timer1")]
    public FixedTimerModel Timer1 { get; init; } = new();

    /// <summary>
    /// タイマー2
    /// </summary>
    [JsonPropertyName("timer2")]
    public FixedTimerModel Timer2 { get; init; } = new();

    /// <summary>
    /// タイマー3
    /// </summary>
    [JsonPropertyName("timer3")]
    public FixedTimerModel Timer3 { get; init; } = new();

    /// <summary>
    /// タイマー4
    /// </summary>
    [JsonPropertyName("timer4")]
    public FixedTimerModel Timer4 { get; init; } = new();
}

/// <summary>
/// 日時指定タイマーの個別設定を保持するモデル
/// </summary>
public class FixedTimerModel
{
    /// <summary>
    /// 有効かどうか
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; init; }

    /// <summary>
    /// 実行日時（文字列形式）
    /// </summary>
    [JsonPropertyName("datetime")]
    public string DatetimeString { get; init; } = "2023/1/1 00:00:00";

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}