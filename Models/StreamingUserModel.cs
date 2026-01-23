using System;
using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class StreamingUserModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("loginId")]
    public string LoginId { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = "";

    /// <summary>
    /// 
    /// </summary>
    [JsonPropertyName("gameName")]
    public string GameName { get; set; } = "";

    /// <summary>
    /// 
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [JsonPropertyName("viewer")]
    public int Viewer { get; set; }
}