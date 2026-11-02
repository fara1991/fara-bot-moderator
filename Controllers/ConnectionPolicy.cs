using System;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 接続の状態（画面の表示と、ボタンの有効・無効を決める）。
/// </summary>
internal enum TwitchConnectionState
{
    /// <summary>
    /// つないでいない（接続の仕組みが、作られていない）。
    /// </summary>
    Disconnected,

    /// <summary>
    /// つなぎ直し中（接続の仕組みは動いているが、チャットか EventSub がつながっていない。回線が切れたときなど）。
    /// </summary>
    Reconnecting,

    /// <summary>
    /// つながっている。
    /// </summary>
    Connected,
}

/// <summary>
/// Twitch に接続しているかの判断。画面（WPF）に依らない純粋な関数だけを置いて、テストする。
/// 画面の表示・Connect ボタンの動き・棒読みちゃんの確認は、どれもこの判断を使う（ラベルの文字列では判断しない）。
/// </summary>
internal static class ConnectionPolicy
{
    /// <summary>
    /// 接続している状態か。チャットと EventSub の両方がつながっていて、アクセストークンが期限内のとき。
    /// </summary>
    /// <param name="chatConnected">チャットがつながっているか</param>
    /// <param name="eventSubConnected">EventSub がつながっているか</param>
    /// <param name="tokenExpiresAt">アクセストークンの期限</param>
    /// <param name="now">今の時刻</param>
    internal static bool IsFullyConnected(bool chatConnected, bool eventSubConnected, DateTime tokenExpiresAt, DateTime now)
        => chatConnected && eventSubConnected && now <= tokenExpiresAt;

    /// <summary>
    /// 接続の状態。つながっていれば Connected。つながっていなくても、接続の仕組みが動いていれば（自動でつなぎ直している）Reconnecting。
    /// </summary>
    /// <param name="hasConnection">接続の仕組み（チャットか EventSub の controller）が、作られているか</param>
    /// <param name="chatConnected">チャットがつながっているか</param>
    /// <param name="eventSubConnected">EventSub がつながっているか</param>
    /// <param name="tokenExpiresAt">アクセストークンの期限</param>
    /// <param name="now">今の時刻</param>
    internal static TwitchConnectionState StateOf(bool hasConnection, bool chatConnected, bool eventSubConnected,
        DateTime tokenExpiresAt, DateTime now)
        => IsFullyConnected(chatConnected, eventSubConnected, tokenExpiresAt, now)
            ? TwitchConnectionState.Connected
            : hasConnection ? TwitchConnectionState.Reconnecting : TwitchConnectionState.Disconnected;
}
