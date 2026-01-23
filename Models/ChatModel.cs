using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class ChatModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("chat")]
    public string Chat { get; set; } = "";
}