using System;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 監視（1 秒周期）がトークンを更新するかどうかの判断。画面（WPF）に依らない純粋な関数だけを置いて、テストする。
/// </summary>
internal static class TokenMonitorPolicy
{
    /// <summary>
    /// refresh に失敗したあと、次の refresh を試すまでの間隔（毎秒の再試行と、毎秒の認可画面を防ぐ）。
    /// </summary>
    internal static readonly TimeSpan RefreshRetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 認可画面を開いてから（認可が失敗してから）、この時間は監視から開き直さない。放置された認可の試行で、監視が止まらないようにするための期限。
    /// </summary>
    internal static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// トークンの期限が切れているか。
    /// </summary>
    internal static bool IsExpired(DateTime now, DateTime expiresAt) => now > expiresAt;

    /// <summary>
    /// 今の周期で refresh を試すか。期限切れで、リフレッシュトークンがあり、次の試行の時刻を過ぎているとき。
    /// </summary>
    /// <param name="nextAttemptAt">次に refresh を試してよい時刻（失敗したあとは now + <see cref="RefreshRetryInterval"/>）</param>
    internal static bool ShouldRefresh(DateTime now, DateTime expiresAt, bool hasRefreshToken, DateTime nextAttemptAt) =>
        IsExpired(now, expiresAt) && hasRefreshToken && now >= nextAttemptAt;

    /// <summary>
    /// 認可の試行が進行中か（認可画面を開いてから <see cref="AuthorizationTimeout"/> を過ぎていない）。
    /// 進行中の間は、監視から認可画面を開き直さない（state が書き換わり、進行中の認可が合わなくなるため）。
    /// </summary>
    /// <param name="openedAt">抑止の起点の時刻（認可画面を開いた時刻、または認可が失敗した時刻。開いていない・成功して終わったときは null）</param>
    internal static bool IsAuthorizationInProgress(DateTime? openedAt, DateTime now) =>
        openedAt is { } opened && now - opened < AuthorizationTimeout;

    /// <summary>
    /// 一時的な失敗が、この回数だけ続いたら「Twitch に接続できません」の文を出す（30 秒周期なので約 5 分）。
    /// </summary>
    internal const int ConnectionWarningThreshold = 10;

    /// <summary>
    /// refresh の結果で、連続の一時的な失敗の回数を更新する。成功とトークン無効では 0 に戻す（ネットワークの問題が解けたとみなす）。
    /// </summary>
    internal static int NextTemporaryFailureCount(RefreshOutcome outcome, int currentCount) =>
        outcome == RefreshOutcome.TemporaryFailure ? currentCount + 1 : 0;

    /// <summary>
    /// 連続の一時的な失敗が、接続できないの文を出す回数に達したか。
    /// </summary>
    internal static bool ShouldShowConnectionWarning(int temporaryFailureCount) =>
        temporaryFailureCount >= ConnectionWarningThreshold;

    /// <summary>
    /// 監視が認可画面を開いてよいか。トークン無効のときだけ開く（一時的な失敗では開かない）。認可の試行が進行中なら開かない（<see cref="IsAuthorizationInProgress"/>）。
    /// </summary>
    /// <param name="outcome">refresh の結果</param>
    /// <param name="authorizationStartedAt">認可の抑止の起点（<see cref="IsAuthorizationInProgress"/> と同じもの）</param>
    /// <param name="now">今の時刻</param>
    internal static bool ShouldOpenAuthorization(RefreshOutcome outcome, DateTime? authorizationStartedAt, DateTime now) =>
        outcome == RefreshOutcome.TokenInvalid && !IsAuthorizationInProgress(authorizationStartedAt, now);

    /// <summary>
    /// 認可の試行が終わったあとの、抑止の起点。成功したら null（抑止しない）。失敗したら、失敗した時刻を起点にする。
    /// 開いた時刻のままだと、終わり近くで失敗したとき、10 分の抑止が数十秒で切れてしまうため。
    /// </summary>
    /// <param name="succeeded">認可が成功したか</param>
    /// <param name="endedAt">試行が終わった時刻（失敗のときは抑止の起点になる）</param>
    internal static DateTime? AuthorizationAnchorAfterEnd(bool succeeded, DateTime endedAt) =>
        succeeded ? null : endedAt;
}
