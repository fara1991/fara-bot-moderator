using System;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// Twitch に接続しているかの判断。
/// </summary>
public class ConnectionPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);

    [Fact]
    public void Connected_when_chat_and_eventsub_are_up_and_the_token_is_valid()
    {
        Assert.True(ConnectionPolicy.IsFullyConnected(true, true, Now.AddHours(1), Now));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Not_connected_when_either_chat_or_eventsub_is_down(bool chat, bool eventSub)
    {
        Assert.False(ConnectionPolicy.IsFullyConnected(chat, eventSub, Now.AddHours(1), Now));
    }

    [Fact]
    public void Not_connected_when_the_token_has_expired()
    {
        Assert.False(ConnectionPolicy.IsFullyConnected(true, true, Now.AddSeconds(-1), Now));
    }
}
