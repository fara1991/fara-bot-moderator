using System;
using System.Threading.Tasks;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Channel;

namespace FaraBotModerator.Controllers;

/// <summary>
///     Twitch EventSub経由の操作をするController
/// </summary>
public class TwitchEventSubController
{
    private static EventSubWebsocketClient? _eventSubClient;
    private readonly TwitchClientController _twitchClientController;
    private readonly TwitchApiController _twitchApiController;

    /// <summary>
    /// 
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// </summary>
    /// <param name="twitchClientController"></param>
    /// <param name="twitchApiController"></param>
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
    public void Connect()
    {
        Task.Run(() => _eventSubClient?.ConnectAsync());
        LogController.OutputLog("EventSub client connected");
    }

    /// <summary>
    /// EventSubクライアントを切断する
    /// </summary>
    public void Disconnect()
    {
        Task.Run(() => _eventSubClient?.DisconnectAsync());
        LogController.OutputLog("EventSub client disconnected");
    }

    /// <summary>
    /// WebSocket接続確立時のイベントハンドラ
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private Task TwitchEventSubOnConnected(object? sender, WebsocketConnectedArgs e)
    {
        var sessionId = _eventSubClient?.SessionId;
        if (sessionId != null)
        {
            LogController.OutputLog($"EventSub WebSocket {sessionId} connected.");
            IsConnected = true;
            if (!e.IsRequestedReconnect)
            {
                _ = _twitchApiController.CreateEventSubFollowAsync(sessionId);
                _ = _twitchApiController.CreateEventSubCheerAsync(sessionId);
                _ = _twitchApiController.CreateEventSubChannelPointAsync(sessionId);
            }
        }

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