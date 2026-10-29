using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// リフレッシュの結果（成功・トークン無効・一時的な失敗）の判定と、それに基づく認可画面・接続の文の判断。
/// 回線断などの一時的な失敗で認可画面を開くと、有効なリフレッシュトークンでもロックしてしまうため、その回帰を防ぐ。
/// </summary>
public class TwitchRefreshOutcomeTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0);

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    public void A_400_or_401_from_twitch_means_the_refresh_token_is_invalid(int statusCode)
    {
        Assert.Equal(RefreshOutcome.TokenInvalid, TwitchAuthService.ClassifyRefreshResponse(statusCode, hasAccessToken: false));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(408)]
    [InlineData(403)]
    [InlineData(0)]
    public void Server_errors_rate_limits_and_no_response_are_temporary_failures(int statusCode)
    {
        Assert.Equal(RefreshOutcome.TemporaryFailure, TwitchAuthService.ClassifyRefreshResponse(statusCode, hasAccessToken: false));
    }

    [Fact]
    public void A_2xx_with_an_access_token_is_a_success()
    {
        Assert.Equal(RefreshOutcome.Succeeded, TwitchAuthService.ClassifyRefreshResponse(200, hasAccessToken: true));
    }

    [Fact]
    public void A_2xx_without_an_access_token_is_a_temporary_failure_not_an_invalid_token()
    {
        Assert.Equal(RefreshOutcome.TemporaryFailure, TwitchAuthService.ClassifyRefreshResponse(200, hasAccessToken: false));
    }

    [Fact]
    public void Exceptions_network_errors_and_timeouts_are_temporary_failures()
    {
        Assert.Equal(RefreshOutcome.TemporaryFailure, TwitchAuthService.ClassifyRefreshException(new HttpRequestException("network")));
        Assert.Equal(RefreshOutcome.TemporaryFailure, TwitchAuthService.ClassifyRefreshException(new TaskCanceledException("timeout")));
        Assert.Equal(RefreshOutcome.TemporaryFailure, TwitchAuthService.ClassifyRefreshException(new TimeoutException()));
        Assert.Equal(RefreshOutcome.TemporaryFailure, TwitchAuthService.ClassifyRefreshException(new JsonException("bad body")));
    }

    [Fact]
    public void A_temporary_failure_never_opens_the_authorization_screen()
    {
        Assert.False(TokenMonitorPolicy.ShouldOpenAuthorization(RefreshOutcome.TemporaryFailure, null, Now));
        Assert.False(TokenMonitorPolicy.ShouldOpenAuthorization(RefreshOutcome.TemporaryFailure, null, Now.AddHours(1)));
    }

    [Fact]
    public void An_invalid_token_opens_the_authorization_screen_when_nothing_is_in_progress()
    {
        Assert.True(TokenMonitorPolicy.ShouldOpenAuthorization(RefreshOutcome.TokenInvalid, null, Now));
        // 抑止の期限（10 分）を過ぎた、放置された試行は、もう抑止しない
        Assert.True(TokenMonitorPolicy.ShouldOpenAuthorization(RefreshOutcome.TokenInvalid, Now, Now.AddMinutes(11)));
    }

    [Fact]
    public void An_invalid_token_does_not_reopen_the_authorization_screen_while_an_attempt_is_in_progress()
    {
        // PR #157 の抑止（認可の試行が進行中の間は、監視から開き直さない）は、無効のときも効く
        Assert.False(TokenMonitorPolicy.ShouldOpenAuthorization(RefreshOutcome.TokenInvalid, Now, Now.AddSeconds(30)));
    }

    [Fact]
    public void A_success_never_opens_the_authorization_screen()
    {
        Assert.False(TokenMonitorPolicy.ShouldOpenAuthorization(RefreshOutcome.Succeeded, null, Now));
    }

    [Fact]
    public void Temporary_failures_count_up_and_success_or_invalid_resets_the_count()
    {
        Assert.Equal(1, TokenMonitorPolicy.NextTemporaryFailureCount(RefreshOutcome.TemporaryFailure, 0));
        Assert.Equal(6, TokenMonitorPolicy.NextTemporaryFailureCount(RefreshOutcome.TemporaryFailure, 5));
        Assert.Equal(0, TokenMonitorPolicy.NextTemporaryFailureCount(RefreshOutcome.Succeeded, 5));
        Assert.Equal(0, TokenMonitorPolicy.NextTemporaryFailureCount(RefreshOutcome.TokenInvalid, 5));
    }

    [Fact]
    public void The_connection_warning_appears_after_ten_consecutive_temporary_failures()
    {
        Assert.Equal(10, TokenMonitorPolicy.ConnectionWarningThreshold);
        Assert.False(TokenMonitorPolicy.ShouldShowConnectionWarning(9));
        Assert.True(TokenMonitorPolicy.ShouldShowConnectionWarning(10));
    }

    [Fact]
    public void Recovering_after_an_outage_resets_the_count_so_the_warning_goes_away()
    {
        // 回線断が 12 回続き、回線が戻って成功した場合の流れ
        var count = 0;
        for (var i = 0; i < 12; i++)
            count = TokenMonitorPolicy.NextTemporaryFailureCount(RefreshOutcome.TemporaryFailure, count);
        Assert.True(TokenMonitorPolicy.ShouldShowConnectionWarning(count));

        count = TokenMonitorPolicy.NextTemporaryFailureCount(RefreshOutcome.Succeeded, count);

        Assert.Equal(0, count);
        Assert.False(TokenMonitorPolicy.ShouldShowConnectionWarning(count));
    }
}
