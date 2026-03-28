using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
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

    /// <summary>
    /// Twitch API経由の操作をするControllerのコンストラクタ
    /// </summary>
    /// <param name="secretKeyModel">設定情報モデル</param>
    public TwitchApiController(SecretKeyModel secretKeyModel)
    {
        _secretKeyModel = secretKeyModel;
    }

    /// <summary>
    /// Twitch API クライアントを初期化し、自身のユーザー情報・配信情報を取得します。
    /// </summary>
    public async Task InitializeAsync()
    {
        _twitchApi = new TwitchAPI
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
            return findUserList.Users.Length > 0 ? findUserList.Users[0] : null;
        }
        catch (Exception ex)
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
            return findUserList.Users.Length > 0 ? findUserList.Users[0] : null;
        }
        catch (Exception ex)
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
            
            if (raidUser.Users.Length > 0)
            {
                await _twitchApi.Helix.Chat.SendShoutoutAsync(_myUserInfo.Id, raidUser.Users[0].Id, _myUserInfo.Id, Settings.Default.AccessToken);
            }
        }
        catch (Exception ex)
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
            return streamingInformations.Streams.Length > 0 ? streamingInformations.Streams[0] : new Stream();
        }
        catch (Exception ex)
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
        try
        {
            var userIds = new List<string>();
            var userLoginNames = new List<string> {_secretKeyModel.Twitch.Client.UserName};
            var gameIds = new List<string>();
            if (_myStreamInfo.GameId != null)
            {
                gameIds.Add(_myStreamInfo.GameId);
            }
            else
            {
                userLoginNames.Clear();
            }

            var sameGameUsers = await _twitchApi.Helix.Streams.GetStreamsAsync(userIds: userIds, userLogins: userLoginNames, gameIds: gameIds,
                    type: "live");
            
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
        catch (Exception ex)
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
        catch (Exception ex)
        {
            LogController.OutputLog($@"<Error> GetStreamingFollowerUsersAsync failed: {ex.Message}");
            return new List<StreamingUserModel>();
        }
    }

    /// <summary>
    /// EventSub サブスクリプションを作成します。
    /// </summary>
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
    public bool ValidateToken()
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
            var validation = Task.Run(() => _twitchApi.Auth.ValidateAccessTokenAsync(accessToken)).Result;
            // _twitchApi.Settings.Scopes = validation.Scopes;
            LogController.OutputLog(
                $"Token is valid. Client ID: {validation.ClientId}, User ID: {validation.UserId}, Expires in: {validation.ExpiresIn} seconds");
            return true;
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"Token validation failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                LogController.OutputLog($"Inner Exception: {ex.InnerException.Message}");
            }
            return false;
        }
    }
}