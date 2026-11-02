using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// Twitch のチャット（IRC）の、行の読み取り・イベントへの変換・受け取った行の振り分け・送る側の決まり。
/// </summary>
public class IrcTests
{
    private const string Privmsg =
        "@badge-info=subscriber/65;badges=broadcaster/1,subscriber/0;display-name=ファーラ;emotes=25:0-4/1902:6-10;mod=0;user-id=402459526 :game_fara!game_fara@game_fara.tmi.twitch.tv PRIVMSG #game_fara :Kappa Keepo hello";

    [Fact]
    public void A_line_with_tags_prefix_command_params_and_trailing_is_read()
    {
        var message = IrcParser.Parse(Privmsg)!;

        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal("game_fara", message.Nick);
        Assert.Equal(["#game_fara"], message.Params);
        Assert.Equal("Kappa Keepo hello", message.Trailing);
        Assert.Equal("ファーラ", message.Tag("display-name"));
        Assert.Equal("402459526", message.Tag("user-id"));
    }

    [Fact]
    public void A_ping_without_tags_or_prefix_is_read()
    {
        var message = IrcParser.Parse("PING :tmi.twitch.tv\r\n")!;

        Assert.Equal("PING", message.Command);
        Assert.Null(message.Nick);
        Assert.Equal("tmi.twitch.tv", message.Trailing);
        Assert.Empty(message.Tags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("@tags-only")]
    [InlineData(":prefix-only")]
    public void A_line_without_a_command_is_ignored(string line)
    {
        Assert.Null(IrcParser.Parse(line));
    }

    [Theory]
    [InlineData("a\\sb", "a b")]
    [InlineData("a\\:b", "a;b")]
    [InlineData("a\\\\b", "a\\b")]
    [InlineData("a\\nb", "a\nb")]
    [InlineData("plain", "plain")]
    [InlineData("end\\", "end")]
    public void Tag_value_escapes_are_undone(string escaped, string expected)
    {
        Assert.Equal(expected, IrcParser.UnescapeTagValue(escaped));
    }

    [Fact]
    public void A_chat_message_has_the_user_the_badges_and_the_emote_names()
    {
        var chat = IrcEvents.ToChatMessage(IrcParser.Parse(Privmsg)!)!;

        Assert.Equal("game_fara", chat.Username);
        Assert.Equal("ファーラ", chat.DisplayName);
        Assert.Equal("402459526", chat.UserId);
        Assert.Equal("Kappa Keepo hello", chat.Message);
        Assert.True(chat.IsBroadcaster);
        Assert.False(chat.IsModerator);
        Assert.Equal(["Kappa", "Keepo"], chat.EmoteNames);
    }

    [Fact]
    public void A_moderator_is_found_by_the_mod_tag_or_the_badge()
    {
        var byTag = IrcEvents.ToChatMessage(IrcParser.Parse("@mod=1 :a!a@a.tmi.twitch.tv PRIVMSG #c :hi")!)!;
        var byBadge = IrcEvents.ToChatMessage(IrcParser.Parse("@badges=moderator/1 :a!a@a.tmi.twitch.tv PRIVMSG #c :hi")!)!;
        var viewer = IrcEvents.ToChatMessage(IrcParser.Parse("@badges=subscriber/0 :a!a@a.tmi.twitch.tv PRIVMSG #c :hi")!)!;

        Assert.True(byTag.IsModerator);
        Assert.True(byBadge.IsModerator);
        Assert.False(viewer.IsModerator);
        Assert.Equal("a", viewer.DisplayName);
    }

    [Fact]
    public void An_action_message_loses_its_wrapper_and_learns_no_emotes()
    {
        var chat = IrcEvents.ToChatMessage(IrcParser.Parse("@emotes=25:0-4 :a!a@a.tmi.twitch.tv PRIVMSG #c :\u0001ACTION Kappa waves\u0001")!)!;

        Assert.Equal("Kappa waves", chat.Message);
        Assert.Empty(chat.EmoteNames);
    }

    [Fact]
    public void Emote_positions_count_characters_not_utf16_units()
    {
        // 絵文字（サロゲートペア）の後ろのエモートも、位置がずれない
        var names = IrcEvents.ExtractEmoteNames("😀 Kappa", "25:2-6");

        Assert.Equal(["Kappa"], names);
    }

    [Theory]
    [InlineData("25:0-99")]
    [InlineData("25:5-2")]
    [InlineData("25:a-b")]
    [InlineData("garbage")]
    [InlineData("")]
    public void Emote_ranges_that_do_not_fit_are_skipped(string tag)
    {
        Assert.Empty(IrcEvents.ExtractEmoteNames("Kappa", tag));
    }

    [Fact]
    public void An_emote_filter_removes_only_the_emotes_it_has_learned_as_whole_words()
    {
        var filter = new EmoteFilter();
        filter.Learn(["Kappa", "Keepo"]);

        Assert.Equal("hello world", filter.Remove("Kappa hello Keepo world"));
        Assert.Equal("Kappas stay", filter.Remove("Kappas stay"));
        Assert.Equal("", filter.Remove("Kappa"));
        Assert.Equal("untouched", new EmoteFilter().Remove("  untouched "));
    }

    private static UserNoticeEvent Notice(string tags, string trailing = "")
        => IrcEvents.ToUserNotice(IrcParser.Parse($"@{tags} :tmi.twitch.tv USERNOTICE #game_fara" + (trailing.Length > 0 ? $" :{trailing}" : ""))!)!;

    [Fact]
    public void A_new_subscriber_and_a_prime_subscriber_are_told_apart()
    {
        var paid = Notice("msg-id=sub;msg-param-sub-plan=1000;display-name=Fan;user-id=5;login=fan");
        var prime = Notice("msg-id=sub;msg-param-sub-plan=Prime;display-name=Fan;user-id=5;login=fan");

        Assert.Equal(UserNoticeKind.NewSubscriber, paid.Kind);
        Assert.Equal(UserNoticeKind.PrimeSubscriber, prime.Kind);
        Assert.Equal("Fan", paid.DisplayName);
        Assert.Equal("5", paid.UserId);
        Assert.Equal("game_fara", paid.Channel);
    }

    [Fact]
    public void A_resubscriber_has_the_cumulative_months()
    {
        var resub = Notice("msg-id=resub;msg-param-cumulative-months=7;display-name=Fan;user-id=5;login=fan");

        Assert.Equal(UserNoticeKind.ReSubscriber, resub.Kind);
        Assert.Equal("7", resub.CumulativeMonths);
    }

    [Fact]
    public void A_gift_a_raid_and_an_announcement_are_recognized()
    {
        var gift = Notice("msg-id=subgift;display-name=Gifter;login=gifter");
        var raid = Notice("msg-id=raid;msg-param-login=raider;msg-param-viewerCount=10;display-name=Raider;login=raider");
        var announcement = Notice("msg-id=announcement;display-name=Fara;login=fara", "Hello everyone");

        Assert.Equal(UserNoticeKind.GiftedSubscription, gift.Kind);
        Assert.Equal("Gifter", gift.DisplayName);
        Assert.Equal(UserNoticeKind.Raid, raid.Kind);
        Assert.Equal("raider", raid.RaiderLogin);
        Assert.Equal(UserNoticeKind.Announcement, announcement.Kind);
        Assert.Equal("Hello everyone", announcement.Message);
    }

    [Fact]
    public void An_unhandled_notice_is_other_and_a_privmsg_is_not_a_notice()
    {
        Assert.Equal(UserNoticeKind.Other, Notice("msg-id=ritual;login=a").Kind);
        Assert.Null(IrcEvents.ToUserNotice(IrcParser.Parse(Privmsg)!));
        Assert.Null(IrcEvents.ToChatMessage(IrcParser.Parse("PING :tmi.twitch.tv")!));
    }

    [Fact]
    public void A_message_to_send_has_no_line_breaks_and_fits_the_limit()
    {
        Assert.Equal("a b c d", IrcClient.SanitizeMessage("a\r\nb\rc\nd"));
        // 改行のあとに、別の IRC の命令を混ぜられない
        Assert.DoesNotContain('\n', IrcClient.SanitizeMessage("hi\r\nPART #channel"));
        Assert.Equal(IrcClient.MaxMessageLength, IrcClient.SanitizeMessage(new string('x', 900)).Length);
    }

    [Fact]
    public void Sending_waits_only_when_the_window_is_full_and_forgets_old_sends()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var sent = new Queue<DateTime>();
        for (var i = 0; i < IrcClient.MaxMessagesPerWindow; i++) sent.Enqueue(now.AddSeconds(-10));

        Assert.Equal(TimeSpan.FromSeconds(20), IrcClient.WaitBeforeNextSend(sent, now));
        Assert.Equal(TimeSpan.Zero, IrcClient.WaitBeforeNextSend(sent, now.AddSeconds(25)));
        Assert.Empty(sent);
    }

    private static IrcClient Client(List<string>? written = null)
    {
        var client = new IrcClient("game_fara", "oauth:x", "game_fara");
        if (written is not null)
            client.WriteOverride = line => { written.Add(line); return Task.CompletedTask; };
        return client;
    }

    [Fact]
    public async Task A_ping_is_answered_with_a_pong()
    {
        var written = new List<string>();
        using var client = Client(written);

        await client.HandleLineAsync("PING :tmi.twitch.tv");

        Assert.Equal(["PONG :tmi.twitch.tv"], written);
    }

    [Fact]
    public async Task Welcome_marks_the_client_connected_and_raises_connected()
    {
        using var client = Client();
        var raised = 0;
        var loggedIn = 0;
        client.Connected += () => { raised++; return Task.CompletedTask; };

        await client.HandleLineAsync(":tmi.twitch.tv 001 game_fara :Welcome, GLHF!", () => loggedIn++);

        Assert.True(client.IsConnected);
        Assert.Equal(1, raised);
        Assert.Equal(1, loggedIn);
    }

    [Fact]
    public async Task Our_own_join_and_another_users_join_are_told_apart()
    {
        using var client = Client();
        var joined = new List<string>();
        var users = new List<string>();
        client.Joined += channel => { joined.Add(channel); return Task.CompletedTask; };
        client.UserJoined += user => { users.Add(user); return Task.CompletedTask; };

        await client.HandleLineAsync(":game_fara!game_fara@game_fara.tmi.twitch.tv JOIN #game_fara");
        await client.HandleLineAsync(":game_fara_dev!game_fara_dev@game_fara_dev.tmi.twitch.tv JOIN #game_fara");

        Assert.Equal(["game_fara"], joined);
        Assert.Equal(["game_fara_dev"], users);
    }

    [Fact]
    public async Task A_chat_message_raises_the_event_and_teaches_the_emotes()
    {
        using var client = Client();
        var messages = new List<ChatMessageEvent>();
        client.MessageReceived += chat => { messages.Add(chat); return Task.CompletedTask; };

        await client.HandleLineAsync(Privmsg);

        Assert.Equal("Kappa Keepo hello", Assert.Single(messages).Message);
        Assert.Equal("hello", client.Emotes.Remove("Kappa hello"));
    }

    [Fact]
    public async Task A_user_notice_raises_the_event_but_an_unhandled_one_does_not()
    {
        using var client = Client();
        var notices = new List<UserNoticeEvent>();
        client.UserNoticeReceived += notice => { notices.Add(notice); return Task.CompletedTask; };

        await client.HandleLineAsync("@msg-id=raid;msg-param-login=raider;login=raider :tmi.twitch.tv USERNOTICE #game_fara");
        await client.HandleLineAsync("@msg-id=ritual;login=a :tmi.twitch.tv USERNOTICE #game_fara");

        Assert.Equal("raider", Assert.Single(notices).RaiderLogin);
    }

    [Fact]
    public async Task An_authentication_failure_and_a_reconnect_request_close_the_connection()
    {
        using var client = Client();

        Assert.True(await client.HandleLineAsync(":tmi.twitch.tv NOTICE * :Login authentication failed"));
        Assert.True(await client.HandleLineAsync(":tmi.twitch.tv RECONNECT"));
        Assert.False(await client.HandleLineAsync(":tmi.twitch.tv NOTICE #game_fara :Slow mode is on"));
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_stop_the_other_lines()
    {
        using var client = Client();
        var count = 0;
        client.MessageReceived += _ => throw new InvalidOperationException("bug in the handler");
        client.UserJoined += _ => { count++; return Task.CompletedTask; };

        await client.HandleLineAsync(Privmsg);
        await client.HandleLineAsync(":a!a@a.tmi.twitch.tv JOIN #game_fara");

        Assert.Equal(1, count);
    }

    [Fact]
    public void Nothing_is_sent_while_not_logged_in()
    {
        using var client = Client();

        Assert.False(client.SendMessage("hello"));
    }
}
