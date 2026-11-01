using System;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// 定期メッセージ・日時指定メッセージを送る時刻の判断。
/// </summary>
public class TimerPolicyTests
{
    private static readonly DateTime T = new(2026, 11, 2, 12, 0, 0);

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(61, true)]
    public void A_cycle_message_is_due_after_the_interval_in_real_time(int elapsedSeconds, bool expected)
    {
        Assert.Equal(expected, TimerPolicy.IsCycleDue(T.AddSeconds(elapsedSeconds), T, intervalMinutes: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_cycle_message_with_no_interval_is_never_due(int intervalMinutes)
    {
        Assert.False(TimerPolicy.IsCycleDue(T.AddHours(1), T, intervalMinutes));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(5, false)]
    public void A_fixed_message_is_due_at_the_target_with_a_grace_period(int secondsAfterTarget, bool expected)
    {
        Assert.Equal(expected, TimerPolicy.IsFixedDue(T.AddSeconds(secondsAfterTarget), T, lastFiredFor: null));
    }

    [Fact]
    public void A_fixed_message_is_sent_only_once_for_the_same_target()
    {
        Assert.False(TimerPolicy.IsFixedDue(T.AddSeconds(2), T, lastFiredFor: T));
    }

    [Fact]
    public void A_fixed_message_is_sent_again_when_the_target_is_changed()
    {
        var newTarget = T.AddHours(1);

        Assert.True(TimerPolicy.IsFixedDue(newTarget, newTarget, lastFiredFor: T));
    }

    [Fact]
    public void The_date_and_time_fields_are_combined_to_the_second()
    {
        var combined = TimerPolicy.Combine(new DateTime(2026, 11, 2, 9, 9, 9), new DateTime(2000, 1, 1, 18, 30, 45, 500));

        Assert.Equal(new DateTime(2026, 11, 2, 18, 30, 45), combined);
        Assert.Null(TimerPolicy.Combine(null, new DateTime(2000, 1, 1, 18, 30, 45)));
        Assert.Null(TimerPolicy.Combine(new DateTime(2026, 11, 2), null));
    }
}
