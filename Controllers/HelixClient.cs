using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch の API（Helix と、トークンの検証）を呼ぶ薄いクライアント。TwitchLib.Api の代わりに、HttpClient で直接呼ぶ。
/// 認証情報は、呼び出しのたびに渡された関数から取る（トークンを更新したあとも、最新のものを使う）。
/// 失敗は、状態コードが成功でなければ <see cref="HelixHttpException"/>、認証情報が無ければ <see cref="HelixCredentialsException"/>、
/// 応答が JSON として読めなければ <see cref="JsonException"/> で知らせる。
/// </summary>
internal sealed class HelixClient
{
    /// <summary>
    /// Helix API の URL の先頭。
    /// </summary>
    internal const string ApiBaseUrl = "https://api.twitch.tv/helix/";

    /// <summary>
    /// アクセストークンの検証の URL。
    /// </summary>
    internal const string ValidateUrl = "https://id.twitch.tv/oauth2/validate";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = false };

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly Func<string?> _getAccessToken;

    /// <summary>
    /// クライアントを作ります。
    /// </summary>
    /// <param name="http">通信に使う HttpClient（テストでは、偽の応答を返すものを渡す）</param>
    /// <param name="clientId">Twitch の Client ID</param>
    /// <param name="getAccessToken">今のアクセストークンを返す関数</param>
    internal HelixClient(HttpClient http, string clientId, Func<string?> getAccessToken)
    {
        _http = http;
        _clientId = clientId;
        _getAccessToken = getAccessToken;
    }

    /// <summary>
    /// ユーザーを、ID またはログイン名で 1 人取得します。
    /// </summary>
    /// <param name="id">ユーザー ID（ログイン名で探すときは null）</param>
    /// <param name="login">ログイン名（ID で探すときは null）</param>
    /// <returns>ユーザー。見つからないときは null</returns>
    internal async Task<HelixUser?> GetUserAsync(string? id, string? login)
    {
        var query = id is not null ? $"id={Uri.EscapeDataString(id)}" : $"login={Uri.EscapeDataString(login ?? "")}";
        var body = await SendAsync(HttpMethod.Get, $"{ApiBaseUrl}users?{query}");
        return Parse<HelixList<HelixUser>>(body)?.Data is { Length: > 0 } users ? users[0] : null;
    }

    /// <summary>
    /// 配信中のストリームを取得します。
    /// </summary>
    /// <param name="userLogin">ユーザーのログイン名で絞り込む（絞り込まないときは null）</param>
    /// <param name="gameId">ゲーム ID で絞り込む（絞り込まないときは null）</param>
    /// <returns>配信中のストリーム（空なら空の配列）</returns>
    internal async Task<HelixStream[]> GetLiveStreamsAsync(string? userLogin, string? gameId)
    {
        var query = new List<string> { "type=live" };
        if (!string.IsNullOrEmpty(userLogin)) query.Add($"user_login={Uri.EscapeDataString(userLogin)}");
        if (!string.IsNullOrEmpty(gameId)) query.Add($"game_id={Uri.EscapeDataString(gameId)}");
        var body = await SendAsync(HttpMethod.Get, $"{ApiBaseUrl}streams?{string.Join('&', query)}");
        return Parse<HelixList<HelixStream>>(body)?.Data ?? [];
    }

    /// <summary>
    /// ユーザーがフォローしているチャンネルのうち、配信中のものを取得します（必要な権限: user:read:follows）。
    /// </summary>
    /// <param name="userId">ユーザー ID</param>
    /// <returns>配信中のストリーム（空なら空の配列）</returns>
    internal async Task<HelixStream[]> GetFollowedStreamsAsync(string userId)
    {
        var body = await SendAsync(HttpMethod.Get, $"{ApiBaseUrl}streams/followed?user_id={Uri.EscapeDataString(userId)}&first=100");
        return Parse<HelixList<HelixStream>>(body)?.Data ?? [];
    }

    /// <summary>
    /// シャウトアウトを送ります（必要な権限: moderator:manage:shoutouts）。
    /// </summary>
    /// <param name="fromBroadcasterId">シャウトアウトを送る配信者の ID</param>
    /// <param name="toBroadcasterId">シャウトアウトの相手の配信者の ID</param>
    /// <param name="moderatorId">送るモデレーターの ID</param>
    internal async Task SendShoutoutAsync(string fromBroadcasterId, string toBroadcasterId, string moderatorId)
    {
        var url = $"{ApiBaseUrl}chat/shoutouts?from_broadcaster_id={Uri.EscapeDataString(fromBroadcasterId)}" +
                  $"&to_broadcaster_id={Uri.EscapeDataString(toBroadcasterId)}&moderator_id={Uri.EscapeDataString(moderatorId)}";
        await SendAsync(HttpMethod.Post, url);
    }

    /// <summary>
    /// ユーザーの、このチャンネルでの Bits の合計（全期間のランキングのスコア）を取得します（必要な権限: bits:read）。
    /// </summary>
    /// <param name="userId">ユーザー ID</param>
    /// <returns>スコア。ランキングに無いときは null</returns>
    internal async Task<int?> GetBitsScoreAsync(string userId)
    {
        var body = await SendAsync(HttpMethod.Get, $"{ApiBaseUrl}bits/leaderboard?count=1&period=all&user_id={Uri.EscapeDataString(userId)}");
        return Parse<HelixList<HelixBitsLeader>>(body)?.Data is { Length: > 0 } leaders ? leaders[0].Score : null;
    }

    /// <summary>
    /// EventSub（WebSocket）の購読を作ります。
    /// </summary>
    /// <param name="type">購読の種類（例: channel.follow）</param>
    /// <param name="version">購読の版</param>
    /// <param name="condition">条件</param>
    /// <param name="sessionId">WebSocket のセッション ID</param>
    internal async Task CreateEventSubSubscriptionAsync(string type, string version, IReadOnlyDictionary<string, string> condition, string sessionId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type,
            version,
            condition,
            transport = new { method = "websocket", session_id = sessionId }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        await SendAsync(HttpMethod.Post, $"{ApiBaseUrl}eventsub/subscriptions", content);
    }

    /// <summary>
    /// アクセストークンを検証します。認証に使う Client ID は要りません。
    /// </summary>
    /// <param name="accessToken">検証するアクセストークン</param>
    /// <returns>有効なら検証の結果。無効・期限切れ（401）なら null</returns>
    internal async Task<HelixValidation?> ValidateAccessTokenAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ValidateUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode) throw new HelixHttpException(response.StatusCode);
        return Parse<HelixValidation>(body);
    }

    /// <summary>
    /// Helix の API を呼びます。Client-Id とアクセストークンを付け、成功以外の状態コードは例外にします。
    /// </summary>
    private async Task<string> SendAsync(HttpMethod method, string url, HttpContent? content = null)
    {
        var accessToken = _getAccessToken();
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(accessToken))
            throw new HelixCredentialsException("Client ID またはアクセストークンがありません。");

        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Add("Client-Id", _clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HelixHttpException(response.StatusCode);
        return body;
    }

    /// <summary>
    /// 応答の本文を JSON として読みます。本文が空・JSON でないときは <see cref="JsonException"/>。本文が null の JSON のときは null。
    /// </summary>
    private static T? Parse<T>(string body) where T : class
        => JsonSerializer.Deserialize<T>(body, JsonOptions);
}

/// <summary>
/// Twitch の API が、成功以外の状態コードを返したときの例外。本文は含めない（応答の中身をログに出さないため）。
/// </summary>
internal sealed class HelixHttpException : HttpRequestException
{
    internal HelixHttpException(HttpStatusCode statusCode)
        : base($"Twitch API が状態コード {(int) statusCode} を返しました。", null, statusCode)
    {
    }
}

/// <summary>
/// Client ID またはアクセストークンが無くて、API を呼べないときの例外。
/// </summary>
internal sealed class HelixCredentialsException : Exception
{
    internal HelixCredentialsException(string message) : base(message)
    {
    }
}

/// <summary>
/// Helix の応答の、一覧の外側（data の配列）。
/// </summary>
/// <typeparam name="T">一覧の項目の型</typeparam>
internal sealed class HelixList<T>
{
    [JsonPropertyName("data")]
    public T[]? Data { get; set; }
}

/// <summary>
/// Twitch のユーザー。
/// </summary>
internal sealed class HelixUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("profile_image_url")]
    public string ProfileImageUrl { get; set; } = "";
}

/// <summary>
/// 配信中のストリーム。
/// </summary>
internal sealed class HelixStream
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("user_login")]
    public string UserLogin { get; set; } = "";

    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = "";

    [JsonPropertyName("game_id")]
    public string GameId { get; set; } = "";

    [JsonPropertyName("game_name")]
    public string GameName { get; set; } = "";

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("viewer_count")]
    public int ViewerCount { get; set; }
}

/// <summary>
/// Bits のランキングの 1 件。
/// </summary>
internal sealed class HelixBitsLeader
{
    [JsonPropertyName("score")]
    public int Score { get; set; }
}

/// <summary>
/// アクセストークンの検証の結果。
/// </summary>
internal sealed class HelixValidation
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
