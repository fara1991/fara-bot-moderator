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
/// </summary>
public class TwitchApiController
{
    private readonly TwitchAPI _twitchApi;
    private readonly SecretKeyModel _secretKeyModel;
    private readonly User _myUserInfo;
    private readonly Stream _myStreamInfo;

    /// <summary>
    ///     Twitch API経由の操作をするController
    /// </summary>
    /// <param name="secretKeyModel"></param>
    public TwitchApiController(SecretKeyModel secretKeyModel)
    {
        _secretKeyModel = secretKeyModel;
        _twitchApi = new TwitchAPI
        {
            Settings =
            {
                ClientId = _secretKeyModel.Twitch.Api.ClientId,
                Secret = _secretKeyModel.Twitch.Api.Secret,
                AccessToken = Settings.Default.AccessToken
            }
        };
        _myUserInfo = GetTwitchChannelByLogin(_secretKeyModel.Twitch.Client.UserName);
        _myStreamInfo = GetTwitchStreaming(_secretKeyModel.Twitch.Client.UserName);
    }

    /// <summary>
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    public string GetTwitchIconUrlById(string userId)
    {
        var user = GetTwitchChannelById(userId);
        return user?.ProfileImageUrl ?? "";
    }

    /// <summary>
    /// </summary>
    /// <param name="userName"></param>
    /// <returns></returns>
    public string GetTwitchIconUrlByLogin(string userName)
    {
        var user = GetTwitchChannelByLogin(userName);
        return user?.ProfileImageUrl ?? "";
    }

    /// <summary>
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    private User GetTwitchChannelById(string userId)
    {
        var userIds = new List<string> {userId};
        var userLoginNames = new List<string>();
        var findUserList = Task.Run(() => _twitchApi.Helix.Users.GetUsersAsync(userIds, userLoginNames)).Result;
        return findUserList.Users.Length > 0 ? findUserList.Users[0] : null;
    }

    /// <summary>
    /// </summary>
    /// <param name="userName"></param>
    /// <returns></returns>
    private User GetTwitchChannelByLogin(string userName)
    {
        var userIds = new List<string>();
        var userLoginNames = new List<string> {userName};
        var findUserList = Task.Run(() => _twitchApi.Helix.Users.GetUsersAsync(userIds, userLoginNames)).Result;
        return findUserList.Users.Length > 0 ? findUserList.Users[0] : null;
    }

    /// <summary>
    /// </summary>
    /// <param name="myUserNam"></param>
    /// <param name="raiderUserName"></param>
    /// <returns></returns>
    public void SendShoutout(string raiderUserName)
    {
        var raidUserIds = new List<string>();
        var raidUserNames = new List<string> {raiderUserName};
        var raidUser = Task.Run(() => _twitchApi.Helix.Users.GetUsersAsync(raidUserIds, raidUserNames)).Result;
        
        Task.Run(() => _twitchApi.Helix.Chat.SendShoutoutAsync(_myUserInfo.Id, raidUser.Users[0].Id, _myUserInfo.Id, Settings.Default.AccessToken));
    }

    /// <summary>
    /// 指定ユーザーの配信中情報を取得します。
    /// </summary>
    /// <param name="userName"></param>
    /// <returns></returns>
    private Stream GetTwitchStreaming(string userName)
    {
        var userIds = new List<string>();
        var userLoginNames = new List<string> {userName};
        var streamingInformations = Task.Run(() =>
                _twitchApi.Helix.Streams.GetStreamsAsync(userIds: userIds, userLogins: userLoginNames, type: "live"))
            .Result;
        return streamingInformations.Streams.Length > 0 ? streamingInformations.Streams[0] : new Stream();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public List<StreamingUserModel> GetStreamingSameGameUsers()
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

        var sameGameUsers = Task.Run(() =>
            _twitchApi.Helix.Streams.GetStreamsAsync(userIds: userIds, userLogins: userLoginNames, gameIds: gameIds,
                type: "live")).Result;
        var streamingUsers = sameGameUsers.Streams.Select(user => new StreamingUserModel
        {
            Icon = GetTwitchIconUrlById(user.UserId),
            Name = user.UserName,
            LoginId = user.UserLogin,
            GameId = user.GameId,
            GameName = user.GameName,
            StartedAt = user.StartedAt,
            Viewer = user.ViewerCount
        }).ToList();
        return streamingUsers;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public List<StreamingUserModel> GetStreamingFollowerUsers()
    {
        var followingUsers = Task.Run(() => _twitchApi.Helix.Streams.GetFollowedStreamsAsync(_myUserInfo.Id)).Result;
        var streamingUsers = followingUsers.Data.Select(user => new StreamingUserModel
        {
            Icon = GetTwitchIconUrlById(user.UserId),
            Name = user.UserName,
            LoginId = user.UserLogin,
            GameId = user.GameId,
            GameName = user.GameName,
            StartedAt = user.StartedAt,
            Viewer = user.ViewerCount
        }).ToList();
        return streamingUsers;
    }

    private async Task CreateEventSubSubscriptionAsync(string subscriptionType, string version,
        Dictionary<string, string> conditions, string sessionId)
    {
        try
        {
            await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync(
                subscriptionType, version, conditions, EventSubTransportMethod.Websocket, sessionId);
        }
        catch (Exception e)
        {
            LogController.OutputLog(e.ToString());
            throw;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sessionId"></param>
    public async Task CreateEventSubFollowAsync(string sessionId)
    {
        var conditions = new Dictionary<string, string>
        {
            {"broadcaster_user_id", _myUserInfo.Id},
            {"moderator_user_id", _myUserInfo.Id}
        };
        await CreateEventSubSubscriptionAsync("channel.follow", "2", conditions, sessionId);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sessionId"></param>
    public async Task CreateEventSubCheerAsync(string sessionId)
    {
        // bits:read
        var conditions = new Dictionary<string, string>
        {
            {"broadcaster_user_id", _myUserInfo.Id}
        };
        await CreateEventSubSubscriptionAsync("channel.cheer", "1", conditions, sessionId);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sessionId"></param>
    public async Task CreateEventSubChannelPointAsync(string sessionId)
    {
        var conditions = new Dictionary<string, string>
        {
            {"broadcaster_user_id", _myUserInfo.Id}
        };
        await CreateEventSubSubscriptionAsync("channel.channel_points_custom_reward_redemption.add", "1", conditions,
            sessionId);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public bool ValidateToken()
    {
        try
        {
            var validation = Task.Run(() => _twitchApi.Auth.ValidateAccessTokenAsync()).Result;
            // _twitchApi.Settings.Scopes = validation.Scopes;
            LogController.OutputLog(
                $"Token is valid. Client ID: {validation.ClientId}, User ID: {validation.UserId}, Expires in: {validation.ExpiresIn} seconds");
            return true;
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"Token validation failed: {ex.Message}");
            return false;
        }
    }
}