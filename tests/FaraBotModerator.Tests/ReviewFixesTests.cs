using System;
using System.Globalization;
using System.IO;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// コードレビューの指摘への対応（通知の間隔・失敗の連続の数え方・翻訳の失敗の文・ログイン名の確認・日時の保存の形・設定ファイルの書き出し）。
/// </summary>
public class ReviewFixesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"));

    public ReviewFixesTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // テスト後の掃除だけなので、消せなくても失敗にはしない
        }
    }

    [Fact]
    public void A_notification_is_allowed_once_per_interval()
    {
        var throttle = new NotificationThrottle(TimeSpan.FromMinutes(5));

        Assert.True(throttle.TryAcquire(Now));
        Assert.False(throttle.TryAcquire(Now.AddMinutes(1)));
        Assert.False(throttle.TryAcquire(Now.AddMinutes(4).AddSeconds(59)));
        Assert.True(throttle.TryAcquire(Now.AddMinutes(5)));
        Assert.False(throttle.TryAcquire(Now.AddMinutes(6)));
    }

    [Fact]
    public void Only_consecutive_failures_reach_the_threshold()
    {
        var counter = new ConsecutiveFailureCounter(3);

        Assert.False(counter.Record(false));
        Assert.False(counter.Record(false));
        // 成功が挟まると、数え直し
        Assert.False(counter.Record(true));
        Assert.False(counter.Record(false));
        Assert.False(counter.Record(false));
        Assert.True(counter.Record(false));
        // 届いたら、数え直す
        Assert.False(counter.Record(false));
    }

    [Fact]
    public void A_single_failure_never_reaches_a_threshold_above_one_and_reset_starts_over()
    {
        var counter = new ConsecutiveFailureCounter(2);

        Assert.False(counter.Record(false));
        counter.Reset();
        Assert.False(counter.Record(false));
        Assert.True(counter.Record(false));
    }

    [Fact]
    public void A_translation_failure_is_described_by_its_reason_without_the_exception_text()
    {
        Assert.Contains("APIキー", TranslationFailure.Describe(new DeepL.AuthorizationException("secret-key-details")));
        Assert.Contains("上限", TranslationFailure.Describe(new DeepL.QuotaExceededException("secret-details")));
        var other = TranslationFailure.Describe(new InvalidOperationException("secret-details"));
        Assert.Equal("DeepL翻訳に失敗しています。", other);
        Assert.DoesNotContain("secret", TranslationFailure.Describe(new DeepL.AuthorizationException("secret-key-details")));
    }

    [Theory]
    [InlineData("game_fara", true)]
    [InlineData("A", true)]
    [InlineData("abc123_xyz", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space", false)]
    [InlineData("a/b", false)]
    [InlineData("a?b=c", false)]
    [InlineData("../x", false)]
    [InlineData("ファーラ", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz", false)]
    public void A_twitch_login_name_is_letters_digits_and_underscore_up_to_25(string? name, bool expected)
    {
        Assert.Equal(expected, TwitchLoginName.IsValid(name));
    }

    [Fact]
    public void A_fixed_timer_date_is_saved_in_a_form_that_does_not_depend_on_the_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // 日付の形が違う地域設定でも、同じ文字になる
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var text = TimerPolicy.FormatFixed(new DateTime(2026, 11, 2), new DateTime(2000, 1, 1, 18, 30, 45));

            Assert.Equal("2026/11/02 18:30:45", text);
            Assert.Equal("2023/01/01 00:00:00", TimerPolicy.FormatFixed(null, null));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("2026/11/02 18:30:45")]
    [InlineData("2023/1/1 00:00:00")]
    [InlineData("2023/1/1 0:00:00")]
    public void Saved_fixed_timer_dates_are_read_back_even_in_the_old_format_and_other_cultures(string text)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.True(TimerPolicy.TryParseFixed(text, out var value));
            Assert.Equal(text.StartsWith("2026", StringComparison.Ordinal) ? 2026 : 2023, value.Year);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_date_saved_in_the_old_culture_format_is_still_read_with_that_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.True(TimerPolicy.TryParseFixed("02.11.2026 18:30:45", out var value));
            Assert.Equal(new DateTime(2026, 11, 2, 18, 30, 45), value);
            Assert.False(TimerPolicy.TryParseFixed("garbage", out _));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_message_that_contains_an_escape_looking_text_survives_saving_and_loading()
    {
        var path = Path.Combine(_directory, "secrets.json");
        var model = SecretKeyController.LoadKeys(path, out _);
        // 「<」という文字そのもの、「<」「>」「&」「+」を含む文
        const string tricky = "a \\u003C b < c > d & e + f";
        model.Event.Follow.Message = tricky;

        SecretKeyController.WriteKeys(model, path);
        var loaded = SecretKeyController.LoadKeys(path, out var quarantined);

        Assert.Null(quarantined);
        Assert.Equal(tricky, loaded.Event.Follow.Message);
    }
}
