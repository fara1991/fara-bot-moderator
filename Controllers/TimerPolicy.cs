using System;
using System.Globalization;

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
    /// 日時指定の日時を、設定ファイルに保存する文字にします。OS の地域設定に依らない形（yyyy/MM/dd HH:mm:ss）。
    /// 日付・時刻が空のときは、2023/01/01・00:00:00 にします。
    /// </summary>
    /// <param name="date">日付の欄の値</param>
    /// <param name="time">時刻の欄の値</param>
    internal static string FormatFixed(DateTime? date, DateTime? time)
    {
        var d = date ?? new DateTime(2023, 1, 1);
        var t = time ?? DateTime.MinValue;
        return new DateTime(d.Year, d.Month, d.Day, t.Hour, t.Minute, t.Second).ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 保存する形（<see cref="FormatFixed"/>）と、同じ形で、先頭の 0 が無いもの（既定値の 2023/1/1 0:00:00 など）。
    /// </summary>
    private static readonly string[] FixedFormats = ["yyyy/MM/dd HH:mm:ss", "yyyy/M/d H:mm:ss", "yyyy/M/d HH:mm:ss"];

    /// <summary>
    /// 設定ファイルの日時の文字を読みます。順番は、(1) 今の保存の形（地域設定に依らない。取り違えない）、
    /// (2) 今の地域設定（以前のバージョンが、地域設定の形で保存したファイルを読むため。日と月の順などを、取り違えない）、
    /// (3) 地域設定に依らない読み方。
    /// </summary>
    /// <param name="text">保存された文字</param>
    /// <param name="value">読んだ日時</param>
    internal static bool TryParseFixed(string? text, out DateTime value)
        => DateTime.TryParseExact(text, FixedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
           || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out value)
           || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

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
