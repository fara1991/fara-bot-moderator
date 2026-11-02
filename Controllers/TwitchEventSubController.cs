using System;
using System.Threading.Tasks;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch EventSub（WebSocket）経由のイベント受信を管理するコントローラー
/// </summary>
public sealed class TwitchEventSubController : IDisposable
{
    private readonly EventSubClient _eventSubClient;
    private readonly TwitchClientController _twitchClientController;
    private readonly TwitchApiController _twitchApiController;

    /// <summary>
    /// EventSub クライアントが接続されているかどうかを取得します。
    /// </summary>
    public bool IsConnected => _eventSubClient.IsConnected;

    /// <summary>
    /// TwitchEventSubController のコンストラクタ
    /// </summary>
    /// <param name="twitchClientController">Twitchクライアントコントローラー</param>
    /// <param name="twitchApiController">Twitch APIコントローラー</param>
    public TwitchEventSubController(TwitchClientController twitchClientController,
        TwitchApiController twitchApiController)
    {
        _twitchClientController = twitchClientController;
        _twitchApiController = twitchApiController;
        _eventSubClient = new EventSubClient();

        // 接続関連のイベントハンドラを設定
        _eventSubClient.SessionStarted += OnSessionStartedAsync;
        _eventSubClient.Disconnected += OnDisconnectedAsync;

        // チャンネル関連のイベントハンドラを設定
        _eventSubClient.Followed += OnFollowedAsync;
        _eventSubClient.Cheered += OnCheerReceivedAsync;
        _eventSubClient.Redeemed += OnChannelPointRewardAsync;
    }

    /// <summary>
    /// EventSubクライアントを接続する（最初のセッションが始まるまで待つ。つながらなければ例外）
    /// </summary>
    public async Task ConnectAsync()
    {
        await _eventSubClient.ConnectAsync();
        LogController.OutputLog("EventSub client connected");
    }

    /// <summary>
    /// EventSubクライアントを切断する（つなぎ直しも止める）
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _eventSubClient.DisconnectAsync();
        LogController.OutputLog("EventSub client disconnected");
    }

    /// <summary>
    /// 後始末をします。
    /// </summary>
    public void Dispose()
    {
        _eventSubClient.Dispose();
    }

    /// <summary>
    /// 新しいセッションが始まったときのイベントハンドラ。購読を作る（結果は、購読ごとにログに出る）。
    /// </summary>
    /// <param name="sessionId">WebSocketセッションID</param>
    private Task OnSessionStartedAsync(string sessionId)
    {
        _ = _twitchApiController.CreateEventSubFollowAsync(sessionId);
        _ = _twitchApiController.CreateEventSubCheerAsync(sessionId);
        _ = _twitchApiController.CreateEventSubChannelPointAsync(sessionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// WebSocket切断時のイベントハンドラ
    /// </summary>
    private static Task OnDisconnectedAsync()
    {
        LogController.OutputLog("EventSub WebSocket disconnected.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// フォロー発生時のイベントハンドラ
    /// </summary>
    /// <param name="e">フォローのイベント</param>
    private Task OnFollowedAsync(FollowEvent e)
    {
        _twitchClientController.TwitchEventSubOnFollow(e);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cheer受信時のイベントハンドラ
    /// </summary>
    /// <param name="e">ビッツのイベント</param>
    private Task OnCheerReceivedAsync(CheerEvent e)
    {
        return _twitchClientController.SendBitsEventSubMessageAsync(e);
    }

    /// <summary>
    /// チャンネルポイント交換時のイベントハンドラ
    /// </summary>
    /// <param name="e">チャンネルポイント交換のイベント</param>
    private Task OnChannelPointRewardAsync(RedemptionEvent e)
    {
        _twitchClientController.SendChannelPointEventSubMessage(e);
        return Task.CompletedTask;
    }
}
