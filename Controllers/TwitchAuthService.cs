using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
        var parameter =
            $"client_id={clientId}" +
            $"&client_secret={clientSecret}" +
            "&grant_type=refresh_token" +
            $"&refresh_token={Settings.Default.RefreshToken}";

        try
        {
            var (responseBody, option) = await PostRequestAsync(parameter, "https://id.twitch.tv/oauth2/token");
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
        if (expectedState != actualState)
        {
            var message = "The token differs between request and response.";
            LogController.OutputLog(message);
            throw new HttpRequestException(message);
        }

        var parameter =
            $"client_id={clientId}" +
            $"&client_secret={clientSecret}" +
            $"&code={authorizationCode}" +
            "&grant_type=authorization_code" +
            $"&redirect_uri=http://localhost:{Settings.Default.Port}";

        var (responseBody, option) = await PostRequestAsync(parameter, "https://id.twitch.tv/oauth2/token");

        if (responseBody.Contains("Invalid authorization code")) return false;

        var tokenResponse = JsonSerializer.Deserialize<TwitchOAuthTokenModel>(responseBody, option);
        if (tokenResponse?.AccessToken == null) return false;

        Settings.Default.AccessToken = tokenResponse.AccessToken;
        Settings.Default.RefreshToken = tokenResponse.RefreshToken;
        Settings.Default.expiresDateTime = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn);
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
               "&scope=bits%3Aread " +
               "channel%3Amanage%3Apredictions " +
               "channel%3Amanage%3Araids " +
               "channel%3amanage%3Aredemptions " +
               "channel%3Amanage%3Aschedule " +
               "channel%3Aread%3Ahype_train " +
               "channel%3Aread%3Apolls " +
               "channel%3Aread%3Apredictions " +
               "channel%3Aread%3Aredemptions " +
               "channel%3Aread%3Astream_key " +
               "channel%3Aread%3Asubscriptions " +
               "channel%3Aread%3Avips " +
               "moderation%3Aread " +
               "moderator%3Amanage%3Aannouncements " +
               "user%3Aread%3Abroadcast " +
               "user%3Aread%3Afollows " +
               "user%3Aread%3Asubscriptions " +
               "user%3Aread%3Aemail " +
               "channel%3Amoderate " +
               "chat%3Aedit " +
               "chat%3Aread " +
               "whispers%3Aread " +
               "channel%3Amanage%3Araids " +
               "moderator%3Amanage%3Ashoutouts " +
               "moderator%3Aread%3Afollowers" +
               $"&state={state}";
    }

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
    /// POSTリクエストを送信します。
    /// </summary>
    private static async Task<(string responseBody, JsonSerializerOptions option)> PostRequestAsync(string parameter, string url)
    {
        var content = new StringContent(parameter, Encoding.Default, "application/x-www-form-urlencoded");
        var response = await HttpClient.PostAsync(url, content);

        var responseBody = await response.Content.ReadAsStringAsync();
        var option = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return (responseBody, option);
    }
}
