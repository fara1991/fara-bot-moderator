using System;
using System.Text.RegularExpressions;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 同じ通知を、続けて出さないための間隔の管理（DeepL の失敗を、視聴者のメッセージのたびに、チャットへ出し続けないため）。
/// </summary>
internal sealed class NotificationThrottle(TimeSpan interval)
{
    private readonly object _lock = new();
    private DateTime? _lastAt;

    /// <summary>
    /// 今、通知してよいか。よいときは、通知した時刻として覚える（続けて呼ぶと、間隔が経つまで false）。
    /// </summary>
    /// <param name="now">今の時刻</param>
    internal bool TryAcquire(DateTime now)
    {
        lock (_lock)
        {
            if (_lastAt is { } last && now - last < interval) return false;

            _lastAt = now;
            return true;
        }
    }
}

/// <summary>
/// 続けて失敗した回数の管理。1 回の失敗（一時的な混雑・時間切れ）では動かず、続けて決まった回数、失敗したときだけ動くため。
/// </summary>
internal sealed class ConsecutiveFailureCounter(int threshold)
{
    private int _failures;

    /// <summary>
    /// 結果を記録します。成功なら数え直し。失敗が続けて決まった回数に届いたら true（そのとき、数え直す）。
    /// </summary>
    /// <param name="success">成功したか</param>
    internal bool Record(bool success)
    {
        if (success)
        {
            _failures = 0;
            return false;
        }

        _failures++;
        if (_failures < threshold) return false;

        _failures = 0;
        return true;
    }

    /// <summary>
    /// 数え直します。
    /// </summary>
    internal void Reset() => _failures = 0;
}

/// <summary>
/// 翻訳（DeepL）の失敗を、チャットの視聴者・配信者に伝える文にする。
/// </summary>
internal static class TranslationFailure
{
    /// <summary>
    /// 失敗の理由に合った文を返します（API キーが正しくない・利用上限に達した・そのほか）。例外の中身（キーや本文を含みうる）は、文に入れない。
    /// </summary>
    /// <param name="error">失敗の例外</param>
    internal static string Describe(Exception error) => error switch
    {
        DeepL.AuthorizationException => "DeepLのAPIキーが正しくありません。設定を確認してください。",
        DeepL.QuotaExceededException => "DeepLの利用上限に達しました。翻訳できません。",
        _ => "DeepL翻訳に失敗しています。",
    };
}

/// <summary>
/// Twitch のログイン名の形の確認（URL に入れる前に、確かめるため）。
/// </summary>
internal static partial class TwitchLoginName
{
    /// <summary>
    /// Twitch のログイン名として使える形か（英数字と「_」、1〜25 文字）。
    /// </summary>
    /// <param name="name">ログイン名</param>
    internal static bool IsValid(string? name) => !string.IsNullOrEmpty(name) && LoginRegex().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_]{1,25}$")]
    private static partial Regex LoginRegex();
}
