using System;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// 接続の状態（接続中・つなぎ直し中・切断）の判断。
/// </summary>
public class ConnectionStateTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);
    private static readonly DateTime Valid = Now.AddHours(1);

    [Fact]
    public void Connected_when_everything_is_up()
    {
        Assert.Equal(TwitchConnectionState.Connected, ConnectionPolicy.StateOf(true, true, true, Valid, Now));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Reconnecting_when_the_connection_exists_but_a_part_is_down(bool chat, bool eventSub)
    {
        Assert.Equal(TwitchConnectionState.Reconnecting, ConnectionPolicy.StateOf(true, chat, eventSub, Valid, Now));
    }

    [Fact]
    public void Reconnecting_when_the_token_has_expired_while_the_connection_exists()
    {
        Assert.Equal(TwitchConnectionState.Reconnecting, ConnectionPolicy.StateOf(true, true, true, Now.AddSeconds(-1), Now));
    }

    [Fact]
    public void Disconnected_when_there_is_no_connection()
    {
        Assert.Equal(TwitchConnectionState.Disconnected, ConnectionPolicy.StateOf(false, false, false, Valid, Now));
    }
}
