using System;
using System.Threading.Tasks;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;

namespace FaraBotModerator.Controllers;

/// <summary>
/// Twitch EventSub（WebSocket）経由のイベント受信を管理するコントローラー
/// </summary>
public class TwitchEventSubController
{
    private static EventSubWebsocketClient? _eventSubClient;
    private readonly TwitchClientController _twitchClientController;
    private readonly TwitchApiController _twitchApiController;

    /// <summary>
    /// EventSub クライアントが接続されているかどうかを取得します。
    /// </summary>
    public bool IsConnected { get; private set; }

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
        // EventSub WebSocketクライアントを初期化
        _eventSubClient = new EventSubWebsocketClient();

        // 接続関連のイベントハンドラを設定
        _eventSubClient.WebsocketConnected += TwitchEventSubOnConnected;
        _eventSubClient.WebsocketDisconnected += TwitchEventSubOnDisconnected;

        // チャンネル関連のイベントハンドラを設定
        _eventSubClient.ChannelFollow += TwitchEventSubOnFollowed;
        _eventSubClient.ChannelCheer += TwitchEventSubOnCheerReceived;
        _eventSubClient.ChannelPointsCustomRewardRedemptionAdd += TwitchEventSubOnChannelPointReward;
    }

    /// <summary>
    /// EventSubクライアントを接続する
    /// </summary>
    public static async Task ConnectAsync()
    {
        if (_eventSubClient != null)
        {
            await _eventSubClient.ConnectAsync();
            LogController.OutputLog("EventSub client connected");
        }
    }

    /// <summary>
    /// EventSubクライアントを切断する
    /// </summary>
    public static async Task DisconnectAsync()
    {
        if (_eventSubClient != null)
        {
            await _eventSubClient.DisconnectAsync();
            LogController.OutputLog("EventSub client disconnected");
        }
    }

    /// <summary>
    /// WebSocket接続確立時のイベントハンドラ
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private Task TwitchEventSubOnConnected(object? sender, WebsocketConnectedArgs e)
    {
        var sessionId = _eventSubClient?.SessionId;
        if (sessionId == null) return Task.CompletedTask;
        
        LogController.OutputLog($"EventSub WebSocket {sessionId} connected.");
        IsConnected = true;
        if (e.IsRequestedReconnect) return Task.CompletedTask;
        
        _ = _twitchApiController.CreateEventSubFollowAsync(sessionId);
        _ = _twitchApiController.CreateEventSubCheerAsync(sessionId);
        _ = _twitchApiController.CreateEventSubChannelPointAsync(sessionId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// WebSocket切断時のイベントハンドラ
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private Task TwitchEventSubOnDisconnected(object? sender, EventArgs e)
    {
        LogController.OutputLog($"EventSub WebSocket disconnected.");
        IsConnected = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// フォロー発生時のイベントハンドラ
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private Task TwitchEventSubOnFollowed(object? sender, ChannelFollowArgs e)
    {
        _twitchClientController.TwitchEventSubOnFollow(e);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cheer受信時のイベントハンドラ
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private Task TwitchEventSubOnCheerReceived(object? sender, ChannelCheerArgs e)
    {
        _twitchClientController.SendBitsEventSubMessage(e);
        return Task.CompletedTask;
    }

    /// <summary>
    /// チャンネルポイント交換時のイベントハンドラ
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private Task TwitchEventSubOnChannelPointReward(object? sender, ChannelPointsCustomRewardRedemptionArgs e)
    {
        _twitchClientController.SendChannelPointEventSubMessage(e);
        return Task.CompletedTask;
    }
}