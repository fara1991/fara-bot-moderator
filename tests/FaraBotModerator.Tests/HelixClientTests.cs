using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// Twitch の API を呼ぶ薄いクライアント（HelixClient）。偽の HttpClient で、送るリクエストの形（URL・ヘッダー・本文）と、
/// 応答・失敗の扱いを確かめる。
/// </summary>
public class HelixClientTests
{
    private sealed class Recorder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "テスト用の HttpClient。破棄するタイミングが無い")]
    private static (HelixClient Client, Recorder Recorder) Make(HttpStatusCode code, string body, string? token = "tok", string clientId = "cid")
    {
        var recorder = new Recorder(_ => Json(code, body));
        return (new HelixClient(new HttpClient(recorder), clientId, () => token), recorder);
    }

    [Fact]
    public async Task A_user_is_requested_by_id_with_client_id_and_bearer_token()
    {
        var (client, recorder) = Make(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"1\",\"login\":\"me\",\"display_name\":\"Me\",\"profile_image_url\":\"https://example.invalid/u.png\"}]}");

        var user = await client.GetUserAsync("1", null);

        Assert.Equal("https://api.twitch.tv/helix/users?id=1", recorder.Request!.RequestUri!.ToString());
        Assert.Equal("cid", recorder.Request.Headers.GetValues("Client-Id").Single());
        Assert.Equal("Bearer", recorder.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", recorder.Request.Headers.Authorization.Parameter);
        Assert.Equal("1", user!.Id);
        Assert.Equal("https://example.invalid/u.png", user.ProfileImageUrl);
    }

    [Fact]
    public async Task A_login_name_is_escaped_in_the_query()
    {
        var (client, recorder) = Make(HttpStatusCode.OK, "{\"data\":[]}");

        var user = await client.GetUserAsync(null, "a b&c=d");

        Assert.Equal("https://api.twitch.tv/helix/users?login=a%20b%26c%3Dd", recorder.Request!.RequestUri!.AbsoluteUri);
        Assert.Null(user);
    }

    [Fact]
    public async Task Live_streams_are_filtered_by_login_and_game()
    {
        var (client, recorder) = Make(HttpStatusCode.OK,
            "{\"data\":[{\"user_id\":\"1\",\"user_login\":\"me\",\"user_name\":\"Me\",\"game_id\":\"9\",\"game_name\":\"G\",\"viewer_count\":3,\"started_at\":\"2026-10-29T00:00:00Z\"}]}");

        var streams = await client.GetLiveStreamsAsync("me", "9");

        Assert.Equal("https://api.twitch.tv/helix/streams?type=live&user_login=me&game_id=9", recorder.Request!.RequestUri!.ToString());
        var stream = Assert.Single(streams);
        Assert.Equal("9", stream.GameId);
        Assert.Equal(3, stream.ViewerCount);
        Assert.Equal(new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), stream.StartedAt);
    }

    [Fact]
    public async Task Followed_streams_are_requested_for_the_user()
    {
        var (client, recorder) = Make(HttpStatusCode.OK, "{\"data\":[]}");

        Assert.Empty(await client.GetFollowedStreamsAsync("1"));
        Assert.Equal("https://api.twitch.tv/helix/streams/followed?user_id=1&first=100", recorder.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_shoutout_is_a_post_with_the_ids_in_the_query()
    {
        var (client, recorder) = Make(HttpStatusCode.NoContent, "");

        await client.SendShoutoutAsync("1", "2", "1");

        Assert.Equal(HttpMethod.Post, recorder.Request!.Method);
        Assert.Equal("https://api.twitch.tv/helix/chat/shoutouts?from_broadcaster_id=1&to_broadcaster_id=2&moderator_id=1",
            recorder.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task The_bits_score_comes_from_the_all_time_leaderboard_of_the_user()
    {
        var (client, recorder) = Make(HttpStatusCode.OK, "{\"data\":[{\"user_id\":\"5\",\"rank\":1,\"score\":1500}]}");

        Assert.Equal(1500, await client.GetBitsScoreAsync("5"));
        Assert.Equal("https://api.twitch.tv/helix/bits/leaderboard?count=1&period=all&user_id=5", recorder.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task The_bits_score_is_null_when_the_user_is_not_in_the_leaderboard()
    {
        var (client, _) = Make(HttpStatusCode.OK, "{\"data\":[]}");

        Assert.Null(await client.GetBitsScoreAsync("5"));
    }

    [Fact]
    public async Task An_eventsub_subscription_is_posted_as_json_with_the_websocket_transport()
    {
        var (client, recorder) = Make(HttpStatusCode.Accepted, "{}");

        await client.CreateEventSubSubscriptionAsync("channel.follow", "2",
            new Dictionary<string, string> { { "broadcaster_user_id", "1" }, { "moderator_user_id", "1" } }, "sess");

        Assert.Equal(HttpMethod.Post, recorder.Request!.Method);
        Assert.Equal("https://api.twitch.tv/helix/eventsub/subscriptions", recorder.Request.RequestUri!.ToString());
        Assert.Equal("application/json", recorder.Request.Content!.Headers.ContentType!.MediaType);
        using var json = System.Text.Json.JsonDocument.Parse(recorder.RequestBody!);
        Assert.Equal("channel.follow", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("2", json.RootElement.GetProperty("version").GetString());
        Assert.Equal("1", json.RootElement.GetProperty("condition").GetProperty("broadcaster_user_id").GetString());
        Assert.Equal("websocket", json.RootElement.GetProperty("transport").GetProperty("method").GetString());
        Assert.Equal("sess", json.RootElement.GetProperty("transport").GetProperty("session_id").GetString());
    }

    [Fact]
    public async Task Token_validation_uses_the_oauth_scheme_and_returns_null_for_401()
    {
        var (client, recorder) = Make(HttpStatusCode.Unauthorized, "{\"status\":401}");

        Assert.Null(await client.ValidateAccessTokenAsync("tok"));
        Assert.Equal("https://id.twitch.tv/oauth2/validate", recorder.Request!.RequestUri!.ToString());
        Assert.Equal("OAuth", recorder.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", recorder.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Token_validation_returns_the_result_for_a_valid_token()
    {
        var (client, _) = Make(HttpStatusCode.OK, "{\"client_id\":\"cid\",\"login\":\"me\",\"user_id\":\"1\",\"scopes\":[],\"expires_in\":3600}");

        var validation = await client.ValidateAccessTokenAsync("tok");

        Assert.Equal("1", validation!.UserId);
        Assert.Equal(3600, validation.ExpiresIn);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData((HttpStatusCode) 429)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_failure_status_code_becomes_an_exception_with_the_code_and_without_the_body(HttpStatusCode code)
    {
        var (client, _) = Make(code, "{\"message\":\"SECRET-BODY\"}");

        var ex = await Assert.ThrowsAsync<HelixHttpException>(() => client.GetUserAsync("1", null));

        Assert.Equal(code, ex.StatusCode);
        Assert.DoesNotContain("SECRET-BODY", ex.Message);
    }

    [Theory]
    [InlineData(null, "cid")]
    [InlineData("", "cid")]
    [InlineData("tok", "")]
    public async Task Missing_credentials_are_reported_without_sending_a_request(string? token, string clientId)
    {
        var (client, recorder) = Make(HttpStatusCode.OK, "{}", token, clientId);

        await Assert.ThrowsAsync<HelixCredentialsException>(() => client.GetUserAsync("1", null));
        Assert.Null(recorder.Request);
    }

    [Fact]
    public async Task The_latest_access_token_is_used_for_each_call()
    {
        var token = "first";
        using var recorder = new Recorder(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));
        using var http = new HttpClient(recorder);
        var client = new HelixClient(http, "cid", () => token);

        await client.GetUserAsync("1", null);
        Assert.Equal("first", recorder.Request!.Headers.Authorization!.Parameter);

        token = "second";
        await client.GetUserAsync("1", null);
        Assert.Equal("second", recorder.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task The_eventsub_subscription_list_has_the_type_status_and_session()
    {
        var (client, recorder) = Make(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"a\",\"status\":\"enabled\",\"type\":\"channel.follow\",\"version\":\"2\",\"transport\":{\"method\":\"websocket\",\"session_id\":\"S1\"}}],\"total\":1}");

        var subscriptions = await client.GetEventSubSubscriptionsAsync();

        Assert.Equal("https://api.twitch.tv/helix/eventsub/subscriptions", recorder.Request!.RequestUri!.ToString());
        var subscription = Assert.Single(subscriptions);
        Assert.Equal("channel.follow", subscription.Type);
        Assert.Equal("enabled", subscription.Status);
        Assert.Equal("S1", subscription.Transport!.SessionId);
    }
}
