using System;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 定期メッセージ・日時指定メッセージを、いつ送るかの判断。画面（WPF）に依らない純粋な関数だけを置いて、テストする。
/// </summary>
internal static class TimerPolicy
{
    /// <summary>
    /// 日時指定のメッセージを、指定の時刻を過ぎてから送ってよい猶予。周期処理が少し遅れても、取りこぼさない（送るのは 1 回だけ）。
    /// </summary>
    internal static readonly TimeSpan FixedTimerGrace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 定期メッセージを送る時刻か（始めてから、間隔の分が経ったか）。
    /// </summary>
    /// <param name="now">今の時刻</param>
    /// <param name="startedAt">数え始めた（前回送った）時刻</param>
    /// <param name="intervalMinutes">間隔（分）。0 以下なら送らない</param>
    internal static bool IsCycleDue(DateTime now, DateTime startedAt, int intervalMinutes)
        => intervalMinutes > 0 && now - startedAt >= TimeSpan.FromMinutes(intervalMinutes);

    /// <summary>
    /// 日時指定のメッセージを送る時刻か。指定の時刻を過ぎていて、猶予の中で、まだその時刻の分を送っていないとき。
    /// </summary>
    /// <param name="now">今の時刻</param>
    /// <param name="target">指定の日時</param>
    /// <param name="lastFiredFor">最後に送った指定の日時（まだ送っていなければ null）</param>
    internal static bool IsFixedDue(DateTime now, DateTime target, DateTime? lastFiredFor)
        => lastFiredFor != target && now >= target && now - target < FixedTimerGrace;

    /// <summary>
    /// 日付の欄と時刻の欄から、指定の日時（秒まで）を作ります。どちらかが空なら null。
    /// </summary>
    /// <param name="date">日付の欄の値</param>
    /// <param name="time">時刻の欄の値</param>
    internal static DateTime? Combine(DateTime? date, DateTime? time)
        => date is null || time is null
            ? null
            : new DateTime(date.Value.Year, date.Value.Month, date.Value.Day, time.Value.Hour, time.Value.Minute, time.Value.Second);
}
