using System;
using System.Text.Json;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// トークンの更新の監視（毎秒の判断）と、認可の戻りの判定。
/// 期限が更新されないと毎秒 refresh し、失敗すると毎秒認可画面を開き直していた（そのための回帰テスト）。
/// </summary>
public class TwitchTokenMonitorTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0);

    [Fact]
    public void Refresh_response_reads_expires_in()
    {
        const string json = """{"access_token":"a","refresh_token":"r","expires_in":14400,"scope":[],"token_type":"bearer"}""";

        var model = JsonSerializer.Deserialize<TwitchRefreshTokenModel>(json);

        Assert.NotNull(model);
        Assert.Equal(14400, model!.ExpiresIn);
    }

    [Fact]
    public void The_expiry_after_a_refresh_is_now_plus_expires_in()
    {
        // 認証コードの交換と同じ計算（期限を更新しないと、監視が毎秒 refresh し続けた）
        Assert.Equal(Now.AddSeconds(14400), TwitchAuthService.ExpiryFrom(Now, 14400));
    }

    [Fact]
    public void After_a_successful_refresh_the_token_is_not_expired_so_the_monitor_does_not_refresh_again()
    {
        var expiresAt = TwitchAuthService.ExpiryFrom(Now, 14400);

        Assert.False(TokenMonitorPolicy.ShouldRefresh(Now.AddSeconds(1), expiresAt, hasRefreshToken: true,
            nextAttemptAt: DateTime.MinValue));
    }

    [Fact]
    public void An_expired_token_with_a_refresh_token_is_refreshed_when_the_retry_time_has_come()
    {
        var expiredAt = Now.AddMinutes(-1);

        Assert.True(TokenMonitorPolicy.ShouldRefresh(Now, expiredAt, hasRefreshToken: true, nextAttemptAt: DateTime.MinValue));
    }

    [Fact]
    public void After_a_failed_refresh_the_next_attempt_waits_30_seconds()
    {
        var expiredAt = Now.AddMinutes(-1);
        var nextAttempt = Now + TokenMonitorPolicy.RefreshRetryInterval;

        Assert.Equal(TimeSpan.FromSeconds(30), TokenMonitorPolicy.RefreshRetryInterval);
        // 1 秒後・29 秒後は試さない（毎秒の再試行にしない）
        Assert.False(TokenMonitorPolicy.ShouldRefresh(Now.AddSeconds(1), expiredAt, true, nextAttempt));
        Assert.False(TokenMonitorPolicy.ShouldRefresh(Now.AddSeconds(29), expiredAt, true, nextAttempt));
        Assert.True(TokenMonitorPolicy.ShouldRefresh(Now.AddSeconds(30), expiredAt, true, nextAttempt));
    }

    [Fact]
    public void Without_a_refresh_token_the_monitor_does_not_refresh()
    {
        Assert.False(TokenMonitorPolicy.ShouldRefresh(Now, Now.AddMinutes(-1), hasRefreshToken: false, DateTime.MinValue));
    }

    [Fact]
    public void While_an_authorization_is_in_progress_the_monitor_does_not_reopen_it()
    {
        // 認可画面を開いて間もない（進行中）の間は、開き直さない（開き直すと state が変わり、進行中の認可が合わなくなる）
        var openedAt = Now;

        Assert.True(TokenMonitorPolicy.IsAuthorizationInProgress(openedAt, Now.AddSeconds(1)));
        Assert.True(TokenMonitorPolicy.IsAuthorizationInProgress(openedAt, Now.AddMinutes(9)));
    }

    [Fact]
    public void An_abandoned_authorization_stops_blocking_after_the_timeout_and_nothing_opened_does_not_block()
    {
        var openedAt = Now;

        Assert.False(TokenMonitorPolicy.IsAuthorizationInProgress(openedAt, openedAt + TokenMonitorPolicy.AuthorizationTimeout));
        Assert.False(TokenMonitorPolicy.IsAuthorizationInProgress(null, Now));
    }

    [Theory]
    [InlineData("http://localhost:30001/?code=abc&scope=bits%3Aread&state=xyz")]
    [InlineData("http://localhost:3000x/?code=abc&scope=bits%3Aread&state=xyz")]
    [InlineData("http://evil.example/?r=http://localhost:3000&code=abc&scope=bits%3Aread&state=xyz")]
    [InlineData("https://localhost:3000/?code=abc&scope=bits%3Aread&state=xyz")]
    public void Urls_on_another_host_scheme_or_port_are_not_handled(string url)
    {
        Assert.False(TwitchAuthService.ShouldHandleAuthUrl(url, "", "xyz", 3000));
    }

    [Fact]
    public void The_authorization_url_has_no_raw_spaces_and_redirects_to_the_port()
    {
        var url = TwitchAuthService.BuildAuthorizationUrl("client", 3000, "state123");

        Assert.DoesNotContain(" ", url);
        Assert.Contains("%20", url);
        Assert.Contains("redirect_uri=http://localhost%3A3000", url);
        Assert.Contains("&state=state123", url);
    }
}
