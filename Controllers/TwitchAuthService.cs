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
/// リフレッシュトークンによる更新の結果。認可画面を開いてよいのは <see cref="TokenInvalid"/> のときだけ。
/// </summary>
public enum RefreshOutcome
{
    /// <summary>更新できた（新しいアクセストークンを保存した）</summary>
    Succeeded,

    /// <summary>リフレッシュトークンが無効（Twitch が 400 / 401 を返した）。再認可が必要</summary>
    TokenInvalid,

    /// <summary>一時的な失敗（回線断・タイムアウト・5xx・429 など）。トークンは無効とは限らない。再認可はしない</summary>
    TemporaryFailure,
}

/// <summary>
/// 認可の戻りの state が、認可のときに作った state と一致しなかったときの例外（CSRF の検査）。
/// 通信の失敗（<see cref="HttpRequestException"/>）とは区別して、画面には state の不一致として出す。
/// </summary>
public sealed class AuthorizationStateMismatchException : Exception
{
    /// <summary>
    /// state が一致しないことを表す例外を作ります（メッセージはログ用の英語の文）。
    /// </summary>
    public AuthorizationStateMismatchException()
        : base("The token differs between request and response.")
    {
    }
}

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
    /// <returns>成功・トークン無効・一時的な失敗のどれか（認可画面を開くのは <see cref="RefreshOutcome.TokenInvalid"/> のときだけ）</returns>
    public async Task<RefreshOutcome> RefreshAccessTokenAsync(string clientId, string clientSecret)
    {
        var form = new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", Settings.Default.RefreshToken ?? "")
        };

        var statusCode = 0;
        try
        {
            string responseBody;
            JsonSerializerOptions option;
            (statusCode, responseBody, option) = await PostRequestAsync(form, "https://id.twitch.tv/oauth2/token");

            // 2xx のときだけ中身を読む（400 / 401 の本文は、判定には使わない）
            var hasAccessToken = false;
            TwitchRefreshTokenModel? tokenResponse = null;
            if (statusCode is >= 200 and < 300)
            {
                tokenResponse = JsonSerializer.Deserialize<TwitchRefreshTokenModel>(responseBody, option);
                hasAccessToken = !string.IsNullOrEmpty(tokenResponse?.AccessToken);
            }

            var outcome = ClassifyRefreshResponse(statusCode, hasAccessToken);
            if (outcome != RefreshOutcome.Succeeded) return outcome;

            Settings.Default.AccessToken = tokenResponse!.AccessToken;
            // 新しいリフレッシュトークンが返ったときだけ置き換える（空で上書きして、次から refresh できなくならないように）
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken)) Settings.Default.RefreshToken = tokenResponse.RefreshToken;
            // 期限を更新しないと、監視が毎秒 refresh し続けるので、認証コードの交換と同じ計算で更新する
            Settings.Default.expiresDateTime = ExpiryFrom(DateTime.Now, tokenResponse.ExpiresIn);
            Settings.Default.Save();
            return RefreshOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            // 例外の Message には URL などが入りうるので、ログには型名と状態コードだけを出す（トークン・本文は出さない）
            LogController.OutputLog($"<Error> トークンの更新に失敗しました（{ex.GetType().Name}、状態コード {statusCode}）。次の周期で再試行します");
            return ClassifyRefreshException(ex);
        }
    }

    /// <summary>
    /// refresh の応答（状態コード・アクセストークンの有無）から結果を決めます。本文の文言では判定しません（Twitch の無効応答は 400 で、文言が変わりうるため）。
    /// </summary>
    /// <param name="statusCode">HTTP の状態コード</param>
    /// <param name="hasAccessToken">2xx の応答に、アクセストークンが入っていたか</param>
    internal static RefreshOutcome ClassifyRefreshResponse(int statusCode, bool hasAccessToken)
    {
        if (statusCode is 400 or 401) return RefreshOutcome.TokenInvalid;
        if (statusCode is >= 200 and < 300) return hasAccessToken ? RefreshOutcome.Succeeded : RefreshOutcome.TemporaryFailure;
        // 5xx・429・408・403 などの 4xx、状態コードが無い（通信の失敗）は、トークンが無効とは言えないので一時的な失敗
        return RefreshOutcome.TemporaryFailure;
    }

    /// <summary>
    /// refresh の途中で例外が起きたときの結果。回線断・タイムアウト・応答の解析の失敗は、いずれも一時的な失敗として扱う（認可画面は開かない）。
    /// </summary>
    internal static RefreshOutcome ClassifyRefreshException(Exception ex) => RefreshOutcome.TemporaryFailure;

    /// <summary>
    /// 認証コードを使用してアクセストークンを取得します。
    /// </summary>
    /// <param name="authorizationCode">認証コード</param>
    /// <param name="clientId">クライアントID</param>
    /// <param name="clientSecret">クライアントシークレット</param>
    /// <param name="expectedState">期待されるstateパラメータ</param>
    /// <param name="actualState">実際のstateパラメータ</param>
    /// <returns>成功した場合はtrue</returns>
    /// <exception cref="AuthorizationStateMismatchException">state が一致しない（空同士も含む）。通信は行わない</exception>
    public async Task<bool> ExchangeAuthorizationCodeAsync(string authorizationCode, string clientId,
        string clientSecret, string expectedState, string actualState)
    {
        if (!StatesMatch(expectedState, actualState))
        {
            var exception = new AuthorizationStateMismatchException();
            LogController.OutputLog(exception.Message);
            throw exception;
        }

        var form = new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code", authorizationCode),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("redirect_uri", $"http://localhost:{Settings.Default.Port}")
        };

        var (_, responseBody, option) = await PostRequestAsync(form, "https://id.twitch.tv/oauth2/token");

        if (responseBody.Contains("Invalid authorization code")) return false;

        var tokenResponse = JsonSerializer.Deserialize<TwitchOAuthTokenModel>(responseBody, option);
        if (tokenResponse?.AccessToken == null) return false;

        Settings.Default.AccessToken = tokenResponse.AccessToken;
        Settings.Default.RefreshToken = tokenResponse.RefreshToken;
        Settings.Default.expiresDateTime = ExpiryFrom(DateTime.Now, tokenResponse.ExpiresIn);
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

        return BuildAuthorizationUrl(clientId, Settings.Default.Port, state);
    }

    /// <summary>
    /// 認可の URL を組み立てます（保存はしない）。スコープは空白を %20 にして、URL の中に生の空白を残さない。
    /// </summary>
    /// <param name="clientId">クライアントID</param>
    /// <param name="port">認可の戻り先（http://localhost:ポート）</param>
    /// <param name="state">認可の state</param>
    internal static string BuildAuthorizationUrl(string clientId, int port, string state)
    {
        return "https://id.twitch.tv/oauth2/authorize" +
               $"?client_id={clientId}" +
               $"&redirect_uri=http://localhost%3A{port}" +
               "&response_type=code" +
               "&scope=" + string.Join("%20", Scopes.Select(Uri.EscapeDataString)) +
               $"&state={state}";
    }

    /// <summary>
    /// 期限（今から有効秒数後）を求めます。認証コードの交換と refresh の両方で同じ計算を使う。
    /// </summary>
    internal static DateTime ExpiryFrom(DateTime now, int expiresInSeconds) => now.AddSeconds(expiresInSeconds);

    /// <summary>
    /// 認可を求める権限（スコープ）。実装している機能（チャット・EventSub・Helix の呼び出し）に必要なものだけを並べる。
    /// 同じ権限を 2 度書かない。URL の組み立ては <see cref="BuildAuthorizationUrl"/> が行う（空白を %20、権限名を Uri.EscapeDataString で %3A 形式にする）。
    /// </summary>
    internal static readonly string[] Scopes =
    [
        "bits:read",
        "channel:moderate",
        "channel:read:redemptions",
        "chat:edit",
        "chat:read",
        "moderator:manage:shoutouts",
        "moderator:read:followers",
        "user:read:follows",
    ];

    /// <summary>
    /// 戻りの state が、認証のときに作った state と一致するか。どちらかが空なら一致しない（空同士を一致とみなさない）。
    /// </summary>
    /// <param name="expectedState">認証のときに作った state（設定に保存したもの）</param>
    /// <param name="actualState">戻りの URL の state</param>
    internal static bool StatesMatch(string expectedState, string actualState) =>
        !string.IsNullOrEmpty(expectedState) && string.Equals(expectedState, actualState, StringComparison.Ordinal);

    /// <summary>
    /// 監視している画面の URL を、認証の戻りとして処理するか。認証を始めていない（state が空）・処理済みの URL・
    /// 戻りの形（http://localhost:ポート の、code・scope・state を含む URL）でないものは処理しない。
    /// </summary>
    /// <param name="url">画面の今の URL</param>
    /// <param name="lastHandled">最後に処理した戻りの URL（同じ URL を 2 度処理しないため）</param>
    /// <param name="expectedState">認証のときに作った state（設定に保存したもの）</param>
    /// <param name="port">認証の戻り先のポート</param>
    internal static bool ShouldHandleAuthUrl(string url, string lastHandled, string expectedState, int port)
    {
        if (string.IsNullOrEmpty(expectedState)) return false;
        if (url == lastHandled) return false;
        // 部分一致では、localhost:30001 や、クエリの中に localhost:3000 を入れた別の URL も通ってしまうので、スキーム・ホスト・ポートを比べる
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp || uri.Port != port) return false;
        if (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return url.Contains("code=") && url.Contains("scope=") && url.Contains("state=");
    }

    /// <summary>
    /// 認可（認証コードの交換）が失敗したときに画面に出す文。理由は大まかに書き、通信の例外の中身（URL・応答の本文など）は出さない。
    /// </summary>
    /// <param name="error">例外で失敗したときの例外（コードが無効などで false を返したときは null）</param>
    internal static string AuthorizationFailureStatus(Exception? error) => error switch
    {
        null => "認可に失敗しました（認可コードが無効、または期限切れ）。Authorize からもう一度認可してください。",
        AuthorizationStateMismatchException => "認可の検証（state）が一致しませんでした。Authorize からもう一度認可してください。",
        _ => "認可に失敗しました（Twitch との通信・応答の処理に失敗）。Authorize からもう一度認可してください。",
    };

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
    private static async Task<(int statusCode, string responseBody, JsonSerializerOptions option)> PostRequestAsync(
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

        return ((int)response.StatusCode, responseBody, option);
    }
}
