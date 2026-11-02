using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// EventSub（WebSocket）のメッセージの読み取りと、受け取ったメッセージの振り分け。
/// </summary>
public class EventSubTests
{
    private const string Welcome =
        "{\"metadata\":{\"message_id\":\"w1\",\"message_type\":\"session_welcome\"},\"payload\":{\"session\":{\"id\":\"SESS\",\"status\":\"connected\",\"keepalive_timeout_seconds\":30,\"reconnect_url\":null}}}";

    private const string Keepalive =
        "{\"metadata\":{\"message_id\":\"k1\",\"message_type\":\"session_keepalive\"},\"payload\":{}}";

    private const string Reconnect =
        "{\"metadata\":{\"message_id\":\"r1\",\"message_type\":\"session_reconnect\"},\"payload\":{\"session\":{\"id\":\"SESS\",\"reconnect_url\":\"wss://example.invalid/ws?x=1\"}}}";

    private static string Notification(string id, string type, string eventJson) =>
        "{\"metadata\":{\"message_id\":\"" + id + "\",\"message_type\":\"notification\",\"subscription_type\":\"" + type + "\"},\"payload\":{\"subscription\":{\"type\":\"" + type + "\"},\"event\":" + eventJson + "}}";

    private const string FollowJson = "{\"user_id\":\"7\",\"user_login\":\"dev\",\"user_name\":\"Dev\",\"followed_at\":\"2026-11-02T08:00:00Z\"}";

    [Fact]
    public void A_welcome_message_has_the_session_id_and_the_keepalive_time()
    {
        var message = EventSubParser.Parse(Welcome);

        Assert.Equal(EventSubMessageKind.Welcome, message.Kind);
        Assert.Equal("SESS", message.SessionId);
        Assert.Equal(30, message.KeepAliveTimeoutSeconds);
    }

    [Fact]
    public void A_reconnect_message_has_the_new_url()
    {
        var message = EventSubParser.Parse(Reconnect);

        Assert.Equal(EventSubMessageKind.Reconnect, message.Kind);
        Assert.Equal("wss://example.invalid/ws?x=1", message.ReconnectUrl);
    }

    [Fact]
    public void A_keepalive_and_an_unknown_message_are_told_apart()
    {
        Assert.Equal(EventSubMessageKind.KeepAlive, EventSubParser.Parse(Keepalive).Kind);
        Assert.Equal(EventSubMessageKind.Unknown, EventSubParser.Parse("{\"metadata\":{\"message_type\":\"something_new\"},\"payload\":{}}").Kind);
    }

    [Fact]
    public void A_notification_has_the_subscription_type_and_the_event()
    {
        var message = EventSubParser.Parse(Notification("n1", "channel.follow", FollowJson));

        Assert.Equal(EventSubMessageKind.Notification, message.Kind);
        Assert.Equal("channel.follow", message.SubscriptionType);
        var follow = EventSubParser.ParseFollow(message.Event!.Value);
        Assert.Equal(new FollowEvent("dev", "Dev"), follow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void A_message_that_is_not_an_eventsub_message_is_an_error(string json)
    {
        Assert.ThrowsAny<JsonException>(() => EventSubParser.Parse(json));
    }

    [Fact]
    public void A_cheer_has_the_user_and_the_bits_and_an_anonymous_cheer_has_no_user()
    {
        using var named = JsonDocument.Parse("{\"is_anonymous\":false,\"user_id\":\"5\",\"user_name\":\"Fan\",\"bits\":100}");
        using var anonymous = JsonDocument.Parse("{\"is_anonymous\":true,\"user_id\":null,\"user_name\":null,\"bits\":50}");

        Assert.Equal(new CheerEvent("5", "Fan", 100), EventSubParser.ParseCheer(named.RootElement));
        Assert.Equal(new CheerEvent(null, null, 50), EventSubParser.ParseCheer(anonymous.RootElement));
    }

    [Fact]
    public void A_redemption_has_the_user_and_the_reward()
    {
        using var json = JsonDocument.Parse("{\"user_login\":\"fan\",\"reward\":{\"title\":\"Hydrate\",\"cost\":500}}");

        Assert.Equal(new RedemptionEvent("fan", "Hydrate", 500), EventSubParser.ParseRedemption(json.RootElement));
    }

    [Fact]
    public async Task A_follow_notification_raises_the_followed_event()
    {
        using var client = new EventSubClient();
        var followers = new List<FollowEvent>();
        client.Followed += e => { followers.Add(e); return Task.CompletedTask; };

        await client.HandleMessageAsync(Notification("n1", "channel.follow", FollowJson), resumedSession: false);

        Assert.Equal(new FollowEvent("dev", "Dev"), Assert.Single(followers));
    }

    [Fact]
    public async Task The_same_notification_twice_is_handled_once()
    {
        using var client = new EventSubClient();
        var count = 0;
        client.Followed += _ => { count++; return Task.CompletedTask; };
        var json = Notification("same", "channel.follow", FollowJson);

        await client.HandleMessageAsync(json, resumedSession: false);
        await client.HandleMessageAsync(json, resumedSession: false);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Each_subscription_type_raises_its_own_event()
    {
        using var client = new EventSubClient();
        var seen = new List<string>();
        client.Followed += _ => { seen.Add("follow"); return Task.CompletedTask; };
        client.Cheered += _ => { seen.Add("cheer"); return Task.CompletedTask; };
        client.Redeemed += _ => { seen.Add("redeem"); return Task.CompletedTask; };

        await client.HandleMessageAsync(Notification("a", "channel.cheer", "{\"is_anonymous\":false,\"user_id\":\"5\",\"user_name\":\"Fan\",\"bits\":1}"), false);
        await client.HandleMessageAsync(Notification("b", "channel.channel_points_custom_reward_redemption.add", "{\"user_login\":\"fan\",\"reward\":{\"title\":\"T\",\"cost\":1}}"), false);
        await client.HandleMessageAsync(Notification("c", "channel.something_else", "{}"), false);

        Assert.Equal(["cheer", "redeem"], seen);
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_the_other_messages()
    {
        using var client = new EventSubClient();
        var count = 0;
        client.Followed += _ => throw new System.InvalidOperationException("bug in the handler");

        await client.HandleMessageAsync(Notification("a", "channel.follow", FollowJson), false);
        client.Cheered += _ => { count++; return Task.CompletedTask; };
        await client.HandleMessageAsync(Notification("b", "channel.cheer", "{\"is_anonymous\":false,\"user_id\":\"5\",\"user_name\":\"Fan\",\"bits\":1}"), false);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task A_new_session_starts_the_subscriptions_but_a_resumed_session_does_not()
    {
        using var client = new EventSubClient();
        var sessions = new List<string>();
        client.SessionStarted += id => { sessions.Add(id); return Task.CompletedTask; };

        await client.HandleMessageAsync(Welcome, resumedSession: false);
        Assert.True(client.IsConnected);
        await client.HandleMessageAsync(Welcome, resumedSession: true);

        Assert.Equal(["SESS"], sessions);
    }

    [Fact]
    public async Task A_message_that_cannot_be_read_is_dropped_without_an_exception()
    {
        using var client = new EventSubClient();

        var message = await client.HandleMessageAsync("garbage", resumedSession: false);

        Assert.Equal(EventSubMessageKind.Unknown, message.Kind);
    }
}
