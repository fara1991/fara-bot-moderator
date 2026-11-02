using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch API 経由の操作（ユーザー情報取得、シャウトアウト、配信情報取得など）を管理するコントローラー
/// </summary>
public class TwitchApiController
{
    /// <summary>
    /// 本番で共有する HttpClient（接続を使い回す）。
    /// </summary>
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private HelixClient? _helix;
    private readonly SecretKeyModel _secretKeyModel;
    private HelixUser? _myUserInfo;
    private HelixStream? _myStreamInfo;
    private readonly HttpClient _http;

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
    /// <param name="http">通信に使う HttpClient（null なら、共有のもの）</param>
    internal TwitchApiController(SecretKeyModel secretKeyModel, HttpClient? http)
    {
        _secretKeyModel = secretKeyModel;
        _http = http ?? SharedHttp;
    }

    /// <summary>
    /// Twitch API クライアントを初期化し、自身のユーザー情報・配信情報を取得します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "起動時（Loaded・接続）の最上位。配信中の接続を最優先し、応答の中身の不備（null 参照など）でも、空の User・Stream で続行する従来の挙動を保つ。失敗はログに残す")]
    public async Task InitializeAsync()
    {
        // アクセストークンは、呼び出しのたびに最新のもの（更新したあとも）を使う
        _helix = new HelixClient(_http, _secretKeyModel.Twitch.Api.ClientId, () => Settings.Default.AccessToken);

        if (string.IsNullOrEmpty(_secretKeyModel.Twitch.Client.UserName))
        {
            _myUserInfo = new HelixUser();
            _myStreamInfo = new HelixStream();
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
            _myUserInfo ??= new HelixUser();
            _myStreamInfo ??= new HelixStream();
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
    private async Task<HelixUser?> GetTwitchChannelByIdAsync(string userId)
    {
        if (_helix == null) return null;
        try
        {
            return await _helix.GetUserAsync(userId, null);
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetTwitchChannelByIdAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ユーザーが、このチャンネルに投げたビッツの合計（Twitch の Bits ランキングの、全期間の値）を取得します。
    /// ランキングへの反映が遅れると、今のチアーが入っていないことがあります。
    /// </summary>
    /// <param name="userId">ビッツを投げたユーザーの ID</param>
    /// <returns>ビッツの合計。取得できなかったとき（ランキングに無い・通信の失敗）は null</returns>
    public async Task<int?> GetBitsTotalAsync(string userId)
    {
        if (_helix == null) return null;
        try
        {
            return await _helix.GetBitsScoreAsync(userId);
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetBitsTotalAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ログイン名からTwitchユーザー情報を取得します。
    /// </summary>
    /// <param name="userName">Twitchログイン名</param>
    /// <returns>ユーザー情報モデル</returns>
    private async Task<HelixUser?> GetTwitchChannelByLoginAsync(string userName)
    {
        if (_helix == null) return null;
        try
        {
            return await _helix.GetUserAsync(null, userName);
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
        if (_helix == null || _myUserInfo == null) return;
        try
        {
            var raidUser = await _helix.GetUserAsync(null, raiderUserName);
            if (raidUser is not null)
                await _helix.SendShoutoutAsync(_myUserInfo.Id, raidUser.Id, _myUserInfo.Id);
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
    private async Task<HelixStream> GetTwitchStreamingAsync(string userName)
    {
        if (_helix == null) return new HelixStream();
        try
        {
            var streams = await _helix.GetLiveStreamsAsync(userName, null);
            return streams is { Length: > 0 } ? streams[0] : new HelixStream();
        }
        catch (Exception ex) when (IsExpectedApiFailure(ex))
        {
            LogController.OutputLog($@"<Error> GetTwitchStreamingAsync failed: {ex.Message}");
            return new HelixStream();
        }
    }

    /// <summary>
    /// 現在の配信と同じゲームをプレイしているユーザーの一覧を取得します。
    /// </summary>
    /// <returns>配信中ユーザー情報のリスト</returns>
    public async Task<List<StreamingUserModel>> GetStreamingSameGameUsersAsync()
    {
        if (_helix == null || _myStreamInfo == null) return new List<StreamingUserModel>();
        try
        {
            // 配信を始めた・ゲームを変えたあとでも合うよう、一覧を出すたびに、自分の配信を取り直す（接続した時点の配信情報のままにしない）
            var ownLogin = _secretKeyModel.Twitch.Client.UserName;
            _myStreamInfo = await GetTwitchStreamingAsync(ownLogin);

            // 配信中でない（ゲームが無い）ときは、絞り込めないので空を返す（ゲームの条件を外すと、配信中の全チャンネルが出てしまう）
            if (string.IsNullOrEmpty(_myStreamInfo.GameId)) return new List<StreamingUserModel>();

            // 同じゲームを配信中のチャンネル（自分は除く）。応答の中身が空（本文が空・{}）のときは、一覧が無いものとして空を返す
            var sameGameStreams = await _helix.GetLiveStreamsAsync(null, _myStreamInfo.GameId);
            var tasks = sameGameStreams
                .Where(user => !string.Equals(user.UserLogin, ownLogin, StringComparison.OrdinalIgnoreCase))
                .Select(async user => new StreamingUserModel
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
        if (_helix == null || string.IsNullOrEmpty(_myUserInfo?.Id)) return new List<StreamingUserModel>();
        try
        {
            var followingStreams = await _helix.GetFollowedStreamsAsync(_myUserInfo.Id);

            var tasks = followingStreams.Select(async user => new StreamingUserModel
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
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "呼び出し元（TwitchEventSubController）は `_ =` で結果を待たない（fire-and-forget）ため、ここで受けないと失敗がどこにも記録されない。Twitch API の呼び出しの最上位として、種類を問わずログに残して続ける")]
    private async Task CreateEventSubSubscriptionAsync(string subscriptionType, string version,
        Dictionary<string, string> conditions, string sessionId)
    {
        if (_helix == null) return;
        try
        {
            await _helix.CreateEventSubSubscriptionAsync(subscriptionType, version, conditions, sessionId);
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
        if (_helix == null) return false;
        try
        {
            var accessToken = Settings.Default.AccessToken;
            if (string.IsNullOrEmpty(accessToken))
            {
                LogController.OutputLog("AccessToken is empty.");
                return false;
            }
            // UI スレッドを止めないように、await で待つ（.Result で待つと、UI が固まり、例外も AggregateException で包まれる）
            var validation = await _helix.ValidateAccessTokenAsync(accessToken);
            // 失効・無効なトークン（401）のときは、null が返る。無効として扱う
            if (validation == null)
            {
                LogController.OutputLog("Token validation failed: Twitch returned no validation result (the token is invalid or expired).");
                return false;
            }
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
    /// 含むもの: 通信の失敗と、成功以外の状態コード（HttpRequestException。<see cref="HelixHttpException"/> を含む）、
    /// タイムアウト・取り消し（OperationCanceledException。TaskCanceledException を含む）、
    /// 応答の解析の失敗（System.Text.Json.JsonException）、Client ID・アクセストークンの不足（<see cref="HelixCredentialsException"/>）。
    /// </summary>
    /// <param name="ex">捕まえた例外</param>
    /// <returns>起こりうる失敗ならtrue</returns>
    internal static bool IsExpectedApiFailure(Exception ex) =>
        ex is HttpRequestException
            or OperationCanceledException
            or System.Text.Json.JsonException
            or HelixCredentialsException;
}
