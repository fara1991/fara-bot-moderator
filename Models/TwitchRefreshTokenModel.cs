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
    public string AccessToken { get; init; } = "";

    /// <summary>
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = "";

    /// <summary>
    /// アクセストークンの有効秒数。refresh の後の期限の計算に使う（認証コードの交換と同じ計算）
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