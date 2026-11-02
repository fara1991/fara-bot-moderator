using System;

namespace FaraBotModerator.Controllers;

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
}
