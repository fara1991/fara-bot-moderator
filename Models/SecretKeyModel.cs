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
    public TwitchSecretKeyModel Twitch { get; set; } = new();

    /// <summary>
    /// DeepL APIキー情報
    /// </summary>
    [JsonPropertyName("deepL")]
    public DeepLKeyModel DeepL { get; set; } = new();

    /// <summary>
    /// 棒読みちゃん連携設定
    /// </summary>
    [JsonPropertyName("bouyomiChan")]
    public BouyomiChanModel BouyomiChan { get; set; } = new();

    /// <summary>
    /// 各種リアクションイベントの設定
    /// </summary>
    [JsonPropertyName("event")]
    public ReactionEventModel Event { get; set; } = new();

    /// <summary>
    /// 定期実行メッセージの設定
    /// </summary>
    [JsonPropertyName("cycleMessage")]
    public CycleMessageModel CycleMessage { get; set; } = new();

    /// <summary>
    /// 日時指定メッセージの設定
    /// </summary>
    [JsonPropertyName("fixedMessage")]
    public FixedMessageModel FixedMessage { get; set; } = new();
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
    public TwitchClientKeyModel Client { get; set; } = new();

    /// <summary>
    /// API連携用設定
    /// </summary>
    [JsonPropertyName("api")]
    public TwitchApiKeyModel Api { get; set; } = new();
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
    public string UserName { get; set; } = "";

    /// <summary>
    /// 表示名
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";
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
    public string ClientId { get; set; } = "";

    /// <summary>
    /// クライアントシークレット
    /// </summary>
    [JsonPropertyName("secret")]
    public string Secret { get; set; } = "";
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
    public string ApiKey { get; set; } = "";
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
    public bool Checked { get; set; } = true;
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
    public ReactionFollowEvent Follow { get; set; } = new();

    /// <summary>
    /// レイドイベント設定
    /// </summary>
    [JsonPropertyName("raid")]
    public ReactionRaidEvent Raid { get; set; } = new();

    /// <summary>
    /// サブスクライブイベント設定
    /// </summary>
    [JsonPropertyName("subscription")]
    public ReactionSubscriptionEvent Subscription { get; set; } = new();

    /// <summary>
    /// Bitsイベント設定
    /// </summary>
    [JsonPropertyName("bits")]
    public ReactionBitsEvent Bits { get; set; } = new();

    /// <summary>
    /// サブスクギフトイベント設定
    /// </summary>
    [JsonPropertyName("gift")]
    public ReactionGiftEvent Gift { get; set; } = new();

    /// <summary>
    /// チャンネルポイントイベント設定
    /// </summary>
    [JsonPropertyName("channelPoint")]
    public ReactionChannelPointEvent ChannelPoint { get; set; } = new();
}

/// <summary>
/// 通知イベントモデルの共通インターフェース
/// </summary>
public interface IEventModel
{
    /// <summary>
    /// 通知が有効かどうか
    /// </summary>
    bool Checked { get; set; }
    /// <summary>
    /// 送信するメッセージ内容
    /// </summary>
    string Message { get; set; }
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
    public CycleTimerModel Timer1 { get; set; } = new();

    /// <summary>
    /// タイマー2
    /// </summary>
    [JsonPropertyName("timer2")]
    public CycleTimerModel Timer2 { get; set; } = new();

    /// <summary>
    /// タイマー3
    /// </summary>
    [JsonPropertyName("timer3")]
    public CycleTimerModel Timer3 { get; set; } = new();

    /// <summary>
    /// タイマー4
    /// </summary>
    [JsonPropertyName("timer4")]
    public CycleTimerModel Timer4 { get; set; } = new();
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
    public bool Checked { get; set; } = false;

    /// <summary>
    /// 実行間隔（分）
    /// </summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 60;

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
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
    public FixedTimerModel Timer1 { get; set; } = new();

    /// <summary>
    /// タイマー2
    /// </summary>
    [JsonPropertyName("timer2")]
    public FixedTimerModel Timer2 { get; set; } = new();

    /// <summary>
    /// タイマー3
    /// </summary>
    [JsonPropertyName("timer3")]
    public FixedTimerModel Timer3 { get; set; } = new();

    /// <summary>
    /// タイマー4
    /// </summary>
    [JsonPropertyName("timer4")]
    public FixedTimerModel Timer4 { get; set; } = new();
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
    public bool Checked { get; set; } = false;

    /// <summary>
    /// 実行日時（文字列形式）
    /// </summary>
    [JsonPropertyName("datetime")]
    public string DatetimeString { get; set; } = "2023/1/1 00:00:00";

    /// <summary>
    /// 送信するメッセージ
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}