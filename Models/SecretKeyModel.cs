using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class SecretKeyModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("twitch")]
    public TwitchSecretKeyModel Twitch { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("deepL")]
    public DeepLKeyModel DeepL { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("bouyomiChan")]
    public BouyomiChanModel BouyomiChan { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("event")]
    public ReactionEventModel Event { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("cycleMessage")]
    public CycleMessageModel CycleMessage { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("fixedMessage")]
    public FixedMessageModel FixedMessage { get; set; } = new();
}

/// <summary>
/// </summary>
public class TwitchSecretKeyModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("client")]
    public TwitchClientKeyModel Client { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("api")]
    public TwitchApiKeyModel Api { get; set; } = new();
}

/// <summary>
/// </summary>
public class TwitchClientKeyModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";
}

/// <summary>
/// </summary>
public class TwitchApiKeyModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("secret")]
    public string Secret { get; set; } = "";
}

/// <summary>
/// </summary>
public class DeepLKeyModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// </summary>
public class BouyomiChanModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool Checked { get; set; } = true;
}

/// <summary>
/// </summary>
public class ReactionEventModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("follow")]
    public ReactionFollowEvent Follow { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("raid")]
    public ReactionRaidEvent Raid { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("subscription")]
    public ReactionSubscriptionEvent Subscription { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("bits")]
    public ReactionBitsEvent Bits { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("gift")]
    public ReactionGiftEvent Gift { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("channelPoint")]
    public ReactionChannelPointEvent ChannelPoint { get; set; } = new();
}

/// <summary>
/// </summary>
public class ReactionFollowEvent
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class ReactionRaidEvent
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class ReactionSubscriptionEvent
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class ReactionBitsEvent
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class ReactionGiftEvent
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class ReactionChannelPointEvent
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = true;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class CycleMessageModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("timer1")]
    public CycleTimerModel Timer1 { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("timer2")]
    public CycleTimerModel Timer2 { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("timer3")]
    public CycleTimerModel Timer3 { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("timer4")]
    public CycleTimerModel Timer4 { get; set; } = new();
}

/// <summary>
/// </summary>
public class CycleTimerModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = false;

    /// <summary>
    /// </summary>
    [JsonPropertyName("interval")]
    public int Interval { get; set; } = 60;

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>
/// </summary>
public class FixedMessageModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("timer1")]
    public FixedTimerModel Timer1 { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("timer2")]
    public FixedTimerModel Timer2 { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("timer3")]
    public FixedTimerModel Timer3 { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("timer4")]
    public FixedTimerModel Timer4 { get; set; } = new();
}

/// <summary>
/// </summary>
public class FixedTimerModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("checked")]
    public bool Checked { get; set; } = false;

    /// <summary>
    /// </summary>
    [JsonPropertyName("datetime")]
    public string DatetimeString { get; set; } = "2023/1/1 00:00:00";

    /// <summary>
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}