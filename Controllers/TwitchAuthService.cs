using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch OAuth認証を管理するサービスクラス
/// </summary>
public class TwitchAuthService
{
    private static readonly HttpClient HttpClient = new();

    /// <summary>
    /// リフレッシュトークンを使用してアクセストークンを更新します。
    /// </summary>
    /// <param name="clientId">クライアントID</param>
    /// <param name="clientSecret">クライアントシークレット</param>
    /// <returns>成功した場合はtrue</returns>
    public async Task<bool> RefreshAccessTokenAsync(string clientId, string clientSecret)
    {
        var form = new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", Settings.Default.RefreshToken ?? "")
        };

        try
        {
            var (responseBody, option) = await PostRequestAsync(form, "https://id.twitch.tv/oauth2/token");
            var tokenResponse = JsonSerializer.Deserialize<TwitchRefreshTokenModel>(responseBody, option);

            if (tokenResponse?.AccessToken == null) return false;

            Settings.Default.AccessToken = tokenResponse.AccessToken;
            Settings.Default.RefreshToken = tokenResponse.RefreshToken;
            Settings.Default.Save();
            return true;
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"Token refresh failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 認証コードを使用してアクセストークンを取得します。
    /// </summary>
    /// <param name="authorizationCode">認証コード</param>
    /// <param name="clientId">クライアントID</param>
    /// <param name="clientSecret">クライアントシークレット</param>
    /// <param name="expectedState">期待されるstateパラメータ</param>
    /// <param name="actualState">実際のstateパラメータ</param>
    /// <returns>成功した場合はtrue</returns>
    public async Task<bool> ExchangeAuthorizationCodeAsync(string authorizationCode, string clientId,
        string clientSecret, string expectedState, string actualState)
    {
        if (!StatesMatch(expectedState, actualState))
        {
            var message = "The token differs between request and response.";
            LogController.OutputLog(message);
            throw new HttpRequestException(message);
        }

        var form = new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code", authorizationCode),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", $"http://localhost:{Settings.Default.Port}")
        };

        var (responseBody, option) = await PostRequestAsync(form, "https://id.twitch.tv/oauth2/token");

        if (responseBody.Contains("Invalid authorization code")) return false;

        var tokenResponse = JsonSerializer.Deserialize<TwitchOAuthTokenModel>(responseBody, option);
        if (tokenResponse?.AccessToken == null) return false;

        Settings.Default.AccessToken = tokenResponse.AccessToken;
        Settings.Default.RefreshToken = tokenResponse.RefreshToken;
        Settings.Default.expiresDateTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn);
        // 使い終わった state は消す（同じ認証の戻りを、もう一度受け付けないように）
        Settings.Default.OAuth2State = "";
        Settings.Default.Save();

        return true;
    }

    /// <summary>
    /// OAuth認証URLを生成します。
    /// </summary>
    /// <param name="clientId">クライアントID</param>
    /// <returns>認証URL</returns>
    public string GenerateAuthorizationUrl(string clientId)
    {
        var state = GenerateRandomState();
        Settings.Default.OAuth2State = state;
        Settings.Default.Save();

        return "https://id.twitch.tv/oauth2/authorize" +
               $"?client_id={clientId}" +
               $"&redirect_uri=http://localhost%3A{Settings.Default.Port}" +
               "&response_type=code" +
               "&scope=" + string.Join(" ", Scopes) +
               $"&state={state}";
    }

    /// <summary>
    /// 認可を求める権限（スコープ）。同じ権限を 2 度書かない（<see cref="GenerateAuthorizationUrl"/> が使う）。
    /// </summary>
    internal static readonly string[] Scopes =
    [
        "bits%3Aread",
        "channel%3Amanage%3Apredictions",
        "channel%3Amanage%3Araids",
        "channel%3Amanage%3Aredemptions",
        "channel%3Amanage%3Aschedule",
        "channel%3Aread%3Ahype_train",
        "channel%3Aread%3Apolls",
        "channel%3Aread%3Apredictions",
        "channel%3Aread%3Aredemptions",
        "channel%3Aread%3Astream_key",
        "channel%3Aread%3Asubscriptions",
        "channel%3Aread%3Avips",
        "moderation%3Aread",
        "moderator%3Amanage%3Aannouncements",
        "user%3Aread%3Abroadcast",
        "user%3Aread%3Afollows",
        "user%3Aread%3Asubscriptions",
        "user%3Aread%3Aemail",
        "channel%3Amoderate",
        "chat%3Aedit",
        "chat%3Aread",
        "whispers%3Aread",
        "moderator%3Amanage%3Ashoutouts",
        "moderator%3Aread%3Afollowers",
    ];

    /// <summary>
    /// 戻りの state が、認証のときに作った state と一致するか。どちらかが空なら一致しない（空同士を一致とみなさない）。
    /// </summary>
    /// <param name="expectedState">認証のときに作った state（設定に保存したもの）</param>
    /// <param name="actualState">戻りの URL の state</param>
    internal static bool StatesMatch(string expectedState, string actualState) =>
        !string.IsNullOrEmpty(expectedState) && string.Equals(expectedState, actualState, StringComparison.Ordinal);

    /// <summary>
    /// トークンの有効期限が切れているかどうかを確認します。
    /// </summary>
    /// <returns>期限切れの場合はtrue</returns>
    public bool IsTokenExpired()
    {
        return DateTime.Now > Settings.Default.expiresDateTime;
    }

    /// <summary>
    /// ランダムなstateパラメータを生成します。
    /// </summary>
    private static string GenerateRandomState()
    {
        var buffer = RandomNumberGenerator.GetBytes(50);
        var stateChars = buffer.Select(x => x % 62)
            .Select(x =>
            {
                return x switch
                {
                    < 10 => (char)('0' + x),
                    < 36 => (char)('A' + x - 10),
                    _ => (char)('a' + x - 36)
                };
            }).ToArray();
        return new string(stateChars);
    }

    /// <summary>
    /// 戻りの URL（?code=…&amp;scope=…&amp;state=… の形）から、パラメーターの値を取り出します（URL デコード済み）。無ければ空。
    /// </summary>
    /// <param name="url">戻りの URL</param>
    /// <param name="name">パラメーターの名前（code・state など）</param>
    internal static string ExtractQueryValue(string url, string name)
    {
        var match = Regex.Match(url, "[?&]" + Regex.Escape(name) + "=([^&#]*)");
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : string.Empty;
    }

    /// <summary>
    /// POSTリクエストを送信します。
    /// </summary>
    private static async Task<(string responseBody, JsonSerializerOptions option)> PostRequestAsync(
        IEnumerable<KeyValuePair<string, string>> form, string url)
    {
        // 値（secret・code）は URL エンコードされる（連結した文字列だと、記号を含むときに壊れる）
        using var content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.PostAsync(url, content);

        var responseBody = await response.Content.ReadAsStringAsync();
        var option = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return (responseBody, option);
    }
}
