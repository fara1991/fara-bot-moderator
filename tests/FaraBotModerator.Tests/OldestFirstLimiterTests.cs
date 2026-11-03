using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// チャットの行の上限（追加した順で、一番古い行から外す。表の並べ替えには依らない）。
/// </summary>
public class OldestFirstLimiterTests
{
    [Fact]
    public void Nothing_is_removed_until_the_limit_is_exceeded()
    {
        var limiter = new OldestFirstLimiter<string>(3);

        Assert.Empty(limiter.Add("a"));
        Assert.Empty(limiter.Add("b"));
        Assert.Empty(limiter.Add("c"));
        Assert.Equal(3, limiter.Count);
    }

    [Fact]
    public void The_oldest_added_item_is_removed_first()
    {
        var limiter = new OldestFirstLimiter<string>(3);
        foreach (var item in new[] { "a", "b", "c" }) limiter.Add(item);

        Assert.Equal(["a"], limiter.Add("d"));
        Assert.Equal(["b"], limiter.Add("e"));
        Assert.Equal(3, limiter.Count);
    }

    [Fact]
    public void The_order_added_decides_not_the_value_so_a_sorted_view_does_not_matter()
    {
        // 表を値で並べ替えていても、消えるのは、追加した順の一番古いもの（値が小さい・大きいに依らない）
        var limiter = new OldestFirstLimiter<int>(2);
        limiter.Add(9);
        limiter.Add(1);

        Assert.Equal([9], limiter.Add(5));
    }

    [Fact]
    public void Several_items_over_the_limit_are_returned_oldest_first()
    {
        var limiter = new OldestFirstLimiter<string>(1);
        limiter.Add("a");

        Assert.Equal(["a"], limiter.Add("b"));
        Assert.Equal(["b"], limiter.Add("c"));
    }
}
