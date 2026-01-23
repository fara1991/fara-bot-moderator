using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class TwitchRefreshTokenModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("scope")]
    public List<string> Scope { get; set; } = new();

    /// <summary>
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "";
}