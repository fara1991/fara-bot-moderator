using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FaraBotModerator.Models;

/// <summary>
/// </summary>
public class TwitchOAuthTokenModel
{
    /// <summary>
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>
    /// </summary>
    [JsonPropertyName("scope")]
    public List<string> Scope { get; init; } = [];

    /// <summary>
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "";
}