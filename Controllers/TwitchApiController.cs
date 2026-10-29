using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Core.Interfaces;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch API 経由の操作（ユーザー情報取得、シャウトアウト、配信情報取得など）を管理するコントローラー
/// </summary>
public class TwitchApiController
{
    private TwitchAPI? _twitchApi;
    private readonly SecretKeyModel _secretKeyModel;
    private User? _myUserInfo;
    private Stream? _myStreamInfo;
    private readonly IHttpCallHandler? _httpCallHandler;

    /// <summary>
    /// Twitch API経由の操作をするControllerのコンストラクタ
    /// </summary>
    /// <param name="secretKeyModel">設定情報モデル</param>
    public TwitchApiController(SecretKeyModel secretKeyModel) : this(secretKeyModel, null)
    {
    }

    /// <summary>
    /// 通信の部分を差し替えられるコンストラクタ（テスト用）
    /// </summary>
    /// <param name="secretKeyModel">設定情報モデル</param>
    /// <param name="httpCallHandler">TwitchLib の通信処理（null なら既定）</param>
    internal TwitchApiController(SecretKeyModel secretKeyModel, IHttpCallHandler? httpCallHandler)
    {
        _secretKeyModel = secretKeyModel;
        _httpCallHandler = httpCallHandler;
    }

    /// <summary>
    /// Twitch API クライアントを初期化し、自身のユーザー情報・配信情報を取得します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "起動時（Loaded・接続）の最上位。配信中の接続を最優先し、応答の中身の不備（null 参照など）でも、空の User・Stream で続行する従来の挙動を保つ。失敗はログに残す")]
    public async Task InitializeAsync()
    {
        _twitchApi = new TwitchAPI(http: _httpCallHandler)
        {
            Settings =
            {
                ClientId = _secretKeyModel.Twitch.Api.ClientId,
                Secret = _secretKeyModel.Twitch.Api.Secret,
                AccessToken = Settings.Default.AccessToken
            }
        };

        if (string.IsNullOrEmpty(_secretKeyModel.Twitch.Client.UserName))
        {
            _myUserInfo = new User();
            _myStreamInfo = new Stream();
            return;
        }

        try
        {
            _myUserInfo = await GetTwitchChannelByLoginAsync(_secretKeyModel.Twitch.Client.UserName);
            _myStreamInfo = await GetTwitchStreamingAsync(_secretKeyModel.Twitch.Client.UserName);
        }
        catch (Exception ex)
        {
            LogController.OutputLog($@"<Error> TwitchApiController initialization failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                LogController.OutputLog($@"<Error> Inner Exception: {ex.InnerException.Message}");
            }
        }
        finally
        {
            _myUserInfo ??= new User();
            _myStreamInfo ??= new Stream();
        }
    }

    /// <summary>
    /// ユーザーIDからTwitchアイコンのURLを取得します。
    /// </summary>
    /// <param name="userId">TwitchユーザーID</param>
    /// <returns>アイコンのURL</returns>
    public async Task<string> GetTwitchIconUrlByIdAsync(string userId)
    {
        var user = await GetTwitchChannelByIdAsync(userId);
        return user?.ProfileImageUrl ?? "";
    }

    /// <summary>
    /// ログイン名からTwitchアイコンのURLを取得します。
    /// </summary>
    /// <param name="userName">Twitchログイン名</param>
    /// <returns>アイコンのURL</returns>
    public async Task<string> GetTwitchIconUrlByLoginAsync(string userName)
    {
        var user = await GetTwitchChannelByLoginAsync(userName);
        return user?.ProfileImageUrl ?? "";
    }

    /// <summary>
    /// ユーザーIDからTwitchユーザー情報を取得します。
    /// </summary>
    /// <param name="userId">TwitchユーザーID</param>
    /// <returns>ユーザー情報モデル</returns>
    private async Task<User?> GetTwitchChannelByIdAsync(string userId)
    {
        if (_twitchApi == null) return null;
        try
        {
            var userIds = new List<string> {userId};
            var userLoginNames = new List<string>();
            var findUserList = await _twitchApi.Helix.Users.GetUsersAsync(userIds, userLoginNames);
            return findUserList?.Users is {Length: > 0} users ? users[0] : null;
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetTwitchChannelByIdAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ログイン名からTwitchユーザー情報を取得します。
    /// </summary>
    /// <param name="userName">Twitchログイン名</param>
    /// <returns>ユーザー情報モデル</returns>
    private async Task<User?> GetTwitchChannelByLoginAsync(string userName)
    {
        if (_twitchApi == null) return null;
        try
        {
            var userIds = new List<string>();
            var userLoginNames = new List<string> {userName};
            var findUserList = await _twitchApi.Helix.Users.GetUsersAsync(userIds, userLoginNames);
            return findUserList?.Users is {Length: > 0} users ? users[0] : null;
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetTwitchChannelByLoginAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 指定されたユーザーに対してシャウトアウトを送信します。
    /// </summary>
    /// <param name="raiderUserName">シャウトアウト対象のユーザー名</param>
    public async Task SendShoutoutAsync(string raiderUserName)
    {
        if (_twitchApi == null || _myUserInfo == null) return;
        try
        {
            var raidUserIds = new List<string>();
            var raidUserNames = new List<string> {raiderUserName};
            var raidUser = await _twitchApi.Helix.Users.GetUsersAsync(raidUserIds, raidUserNames);
            
            if (raidUser?.Users is {Length: > 0} raidUsers)
            {
                await _twitchApi.Helix.Chat.SendShoutoutAsync(_myUserInfo.Id, raidUsers[0].Id, _myUserInfo.Id, Settings.Default.AccessToken);
            }
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> SendShoutoutAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定ユーザーの配信中情報を取得します。
    /// </summary>
    /// <param name="userName">Twitchユーザー名</param>
    /// <returns>ストリーム情報モデル</returns>
    private async Task<Stream> GetTwitchStreamingAsync(string userName)
    {
        if (_twitchApi == null) return new Stream();
        try
        {
            var userIds = new List<string>();
            var userLoginNames = new List<string> {userName};
            var streamingInformations = await _twitchApi.Helix.Streams.GetStreamsAsync(userIds: userIds, userLogins: userLoginNames, type: "live");
            return streamingInformations?.Streams is {Length: > 0} streams ? streams[0] : new Stream();
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetTwitchStreamingAsync failed: {ex.Message}");
            return new Stream();
        }
    }

    /// <summary>
    /// 現在の配信と同じゲームをプレイしているユーザーの一覧を取得します。
    /// </summary>
    /// <returns>配信中ユーザー情報のリスト</returns>
    public async Task<List<StreamingUserModel>> GetStreamingSameGameUsersAsync()
    {
        if (_twitchApi == null || _myStreamInfo == null) return new List<StreamingUserModel>();
        // 配信中でない（ゲームが無い）ときは、絞り込めないので空を返す（ゲームの条件を外すと、配信中の全チャンネルが出てしまう）
        if (_myStreamInfo.GameId == null) return new List<StreamingUserModel>();
        try
        {
            var userIds = new List<string>();
            var userLoginNames = new List<string> {_secretKeyModel.Twitch.Client.UserName};
            var gameIds = new List<string> {_myStreamInfo.GameId};

            var sameGameUsers = await _twitchApi.Helix.Streams.GetStreamsAsync(userIds: userIds, userLogins: userLoginNames, gameIds: gameIds,
                    type: "live");
            
            // 応答の中身が空（本文が空・{}）のときは、一覧が無いものとして空を返す
            if (sameGameUsers?.Streams == null) return new List<StreamingUserModel>();
            var tasks = sameGameUsers.Streams.Select(async user => new StreamingUserModel
            {
                Icon = await GetTwitchIconUrlByIdAsync(user.UserId),
                Name = user.UserName,
                LoginId = user.UserLogin,
                GameId = user.GameId,
                GameName = user.GameName,
                StartedAt = user.StartedAt,
                Viewer = user.ViewerCount
            });
            
            return (await Task.WhenAll(tasks)).ToList();
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetStreamingSameGameUsersAsync failed: {ex.Message}");
            return new List<StreamingUserModel>();
        }
    }

    /// <summary>
    /// フォローしているユーザーの中で配信中のユーザー一覧を取得します。
    /// </summary>
    /// <returns>配信中ユーザー情報のリスト</returns>
    public async Task<List<StreamingUserModel>> GetStreamingFollowerUsersAsync()
    {
        if (_twitchApi == null || string.IsNullOrEmpty(_myUserInfo?.Id)) return new List<StreamingUserModel>();
        try
        {
            var followingUsers = await _twitchApi.Helix.Streams.GetFollowedStreamsAsync(_myUserInfo.Id);
            
            if (followingUsers?.Data == null) return new List<StreamingUserModel>();
            var tasks = followingUsers.Data.Select(async user => new StreamingUserModel
            {
                Icon = await GetTwitchIconUrlByIdAsync(user.UserId),
                Name = user.UserName,
                LoginId = user.UserLogin,
                GameId = user.GameId,
                GameName = user.GameName,
                StartedAt = user.StartedAt,
                Viewer = user.ViewerCount
            });
            
            return (await Task.WhenAll(tasks)).ToList();
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetStreamingFollowerUsersAsync failed: {ex.Message}");
            return new List<StreamingUserModel>();
        }
    }

    /// <summary>
    /// EventSub サブスクリプションを作成します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "呼び出し元（TwitchEventSubController）は `_ =` で結果を待たない（fire-and-forget）ため、ここで受けないと失敗がどこにも記録されない。TwitchLib の呼び出しの最上位として、種類を問わずログに残して続ける")]
    private async Task CreateEventSubSubscriptionAsync(string subscriptionType, string version,
        Dictionary<string, string> conditions, string sessionId)
    {
        if (_twitchApi == null) return;
        try
        {
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                subscriptionType, version, conditions, EventSubTransportMethod.Websocket, sessionId);
        }
        catch (Exception e)
        {
            LogController.OutputLog($@"<Error> CreateEventSubSubscriptionAsync failed ({subscriptionType}): {e.Message}");
            // throw; // 呼び出し元で処理が継続できるよう、ここでは例外を投げない
        }
    }

    /// <summary>
    /// フォローイベントの EventSub 通知を有効にします。
    /// </summary>
    /// <param name="sessionId">WebSocketセッションID</param>
    public async Task CreateEventSubFollowAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(_myUserInfo?.Id)) return;
        var conditions = new Dictionary<string, string>
        {
            {"broadcaster_user_id", _myUserInfo.Id},
            {"moderator_user_id", _myUserInfo.Id}
        };
        await CreateEventSubSubscriptionAsync("channel.follow", "2", conditions, sessionId);
    }

    /// <summary>
    /// Cheerイベントの EventSub 通知を有効にします。
    /// </summary>
    /// <param name="sessionId">WebSocketセッションID</param>
    public async Task CreateEventSubCheerAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(_myUserInfo?.Id)) return;
        // bits:read
        var conditions = new Dictionary<string, string>
        {
            {"broadcaster_user_id", _myUserInfo.Id}
        };
        await CreateEventSubSubscriptionAsync("channel.cheer", "1", conditions, sessionId);
    }

    /// <summary>
    /// チャンネルポイント交換イベントの EventSub 通知を有効にします。
    /// </summary>
    /// <param name="sessionId">WebSocketセッションID</param>
    public async Task CreateEventSubChannelPointAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(_myUserInfo?.Id)) return;
        var conditions = new Dictionary<string, string>
        {
            {"broadcaster_user_id", _myUserInfo.Id}
        };
        await CreateEventSubSubscriptionAsync("channel.channel_points_custom_reward_redemption.add", "1", conditions,
            sessionId);
    }

    /// <summary>
    /// アクセストークンが有効かどうかを検証します。
    /// </summary>
    /// <returns>有効であればtrue</returns>
    public async Task<bool> ValidateTokenAsync()
    {
        if (_twitchApi == null) return false;
        try
        {
            var accessToken = Settings.Default.AccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                LogController.OutputLog("AccessToken is empty.");
                return false;
            }
            // UI スレッドを止めないように、await で待つ（.Result で待つと、UI が固まり、例外も AggregateException で包まれる）
            var validation = await _twitchApi.Auth.ValidateAccessTokenAsync(accessToken);
            // 失効・無効なトークンのとき、Twitch（TwitchLib）は例外ではなく null を返す。無効として扱う
            if (validation == null)
            {
                LogController.OutputLog("Token validation failed: Twitch returned no validation result (the token is invalid or expired).");
                return false;
            }
            // _twitchApi.Settings.Scopes = validation.Scopes;
            LogController.OutputLog(
                $"Token is valid. Client ID: {validation.ClientId}, User ID: {validation.UserId}, Expires in: {validation.ExpiresIn} seconds");
            return true;
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($"Token validation failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                LogController.OutputLog($"Inner Exception: {ex.InnerException.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// Twitch API の呼び出しで「起こりうる失敗」か判定します。起こりうる失敗はログに残して null・空・false で続け、
    /// それ以外（NullReferenceException など、コードの誤り）は握りつぶさず上位へ出します。
    /// 含むもの: 通信の失敗（HttpRequestException）、タイムアウト・取り消し（OperationCanceledException。TaskCanceledException を含む）、
    /// 応答の解析の失敗（Newtonsoft.Json.JsonException）、TwitchLib が状態コードから投げる API 例外（HttpResponseException 系）、
    /// 引数・認証情報の不足（BadParameterException・ClientIdAndOAuthTokenRequired・InvalidCredentialException）。
    /// </summary>
    /// <param name="ex">捕まえた例外</param>
    /// <returns>起こりうる失敗ならtrue</returns>
    internal static bool IsExpectedApiFailure(Exception ex) =>
        ex is HttpRequestException
            or OperationCanceledException
            or Newtonsoft.Json.JsonException
            or HttpResponseException
            or BadParameterException
            or ClientIdAndOAuthTokenRequired
            or InvalidCredentialException;
}
