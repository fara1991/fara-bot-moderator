using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using FaraBotModerator.Properties;
using TwitchLib.Api.Core.HttpCallHandlers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// TwitchApiController の実メソッドの配線。TwitchLib の通信（HttpClient）を偽物に差し替えて、
/// 「起こりうる失敗は握って続ける」「想定外の例外は握らず外へ出す」（catch … when が効く）ことと、
/// 無効なトークン（Twitch は 401 で null を返す）を従来どおり false にすることを確かめる。
/// </summary>
public class TwitchApiControllerWiringTests
{
    private const string UserJson = "{\"data\":[{\"id\":\"1\",\"login\":\"me\",\"display_name\":\"Me\",\"profile_image_url\":\"https://example.invalid/u.png\"}]}";
    private const string LiveStreamJson = "{\"data\":[{\"id\":\"s\",\"user_id\":\"1\",\"user_login\":\"me\",\"user_name\":\"Me\",\"game_id\":\"9\",\"game_name\":\"G\",\"type\":\"live\",\"viewer_count\":3,\"started_at\":\"2026-10-29T00:00:00Z\"}],\"pagination\":{}}";

    private sealed class Route(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static SecretKeyModel Keys() => new()
    {
        Twitch = new TwitchSecretKeyModel
        {
            Client = new TwitchClientKeyModel { UserName = "me" },
            Api = new TwitchApiKeyModel { ClientId = "cid", Secret = "sec" }
        }
    };

    /// <summary>TwitchLib の既定の通信（TwitchHttpClient）の HttpClient だけを偽物にして、状態コードの解釈は本物のままにする</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "偽の HttpClient は、渡した TwitchHttpClient がテストの終わりまで持つ。テスト用で、破棄するタイミングが無い")]
    private static TwitchHttpClient FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var client = new TwitchHttpClient(null!);
        var field = typeof(TwitchHttpClient).GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(client, new HttpClient(new Route(respond)));
        return client;
    }

    private static bool Ends(string path, string suffix) => path.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>既定は「自分のユーザー・配信中」を返し、<paramref name="custom"/> が応答を返したパスだけ差し替える</summary>
    private static async Task<TwitchApiController> StartedAsync(Func<string, HttpResponseMessage?> custom)
    {
        Settings.Default.AccessToken = "token";
        var controller = new TwitchApiController(Keys(), FakeHttp(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var response = custom(path);
            if (response != null) return Task.FromResult(response);
            return Task.FromResult(Json(HttpStatusCode.OK, Ends(path, "/streams") ? LiveStreamJson : UserJson));
        }));
        await controller.InitializeAsync();
        return controller;
    }

    [Fact]
    public async Task Validate_returns_false_when_twitch_answers_401_for_an_invalid_token()
    {
        var controller = await StartedAsync(path => Ends(path, "/validate")
            ? Json(HttpStatusCode.Unauthorized, "{\"status\":401,\"message\":\"invalid access token\"}") : null);
        Assert.False(await controller.ValidateTokenAsync());
    }

    [Fact]
    public async Task Validate_returns_false_when_the_response_has_no_content()
    {
        var controller = await StartedAsync(path => Ends(path, "/validate") ? Json(HttpStatusCode.OK, "") : null);
        Assert.False(await controller.ValidateTokenAsync());
    }

    [Fact]
    public async Task Validate_returns_true_for_a_valid_token()
    {
        var controller = await StartedAsync(path => Ends(path, "/validate")
            ? Json(HttpStatusCode.OK, "{\"client_id\":\"cid\",\"login\":\"me\",\"user_id\":\"1\",\"scopes\":[],\"expires_in\":3600}") : null);
        Assert.True(await controller.ValidateTokenAsync());
    }

    [Fact]
    public async Task Validate_returns_false_on_a_server_error()
    {
        var controller = await StartedAsync(path => Ends(path, "/validate") ? Json(HttpStatusCode.ServiceUnavailable, "{}") : null);
        Assert.False(await controller.ValidateTokenAsync());
    }

    [Fact]
    public async Task Validate_lets_an_unexpected_exception_out()
    {
        var controller = await StartedAsync(path => Ends(path, "/validate") ? throw new InvalidOperationException("bug") : null);
        await Assert.ThrowsAsync<InvalidOperationException>(controller.ValidateTokenAsync);
    }

    [Fact]
    public async Task Follower_list_is_empty_for_a_503()
    {
        var controller = await StartedAsync(path => Ends(path, "/streams/followed") ? Json(HttpStatusCode.ServiceUnavailable, "{}") : null);
        Assert.Empty(await controller.GetStreamingFollowerUsersAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("<html>bad gateway</html>")]
    public async Task Follower_list_is_empty_for_an_empty_or_broken_200(string body)
    {
        var controller = await StartedAsync(path => Ends(path, "/streams/followed") ? Json(HttpStatusCode.OK, body) : null);
        Assert.Empty(await controller.GetStreamingFollowerUsersAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public async Task Same_game_list_is_empty_for_an_empty_200(string body)
    {
        var calls = 0;
        var controller = await StartedAsync(path =>
        {
            if (!Ends(path, "/streams")) return null;
            // 1 回目（InitializeAsync の自分の配信）は配信中、2 回目（同じゲームの一覧）は空の本文
            return Interlocked.Increment(ref calls) == 1 ? null : Json(HttpStatusCode.OK, body);
        });
        Assert.Empty(await controller.GetStreamingSameGameUsersAsync());
    }

    [Fact]
    public async Task Follower_list_lets_an_unexpected_exception_out()
    {
        var controller = await StartedAsync(path => Ends(path, "/streams/followed") ? throw new InvalidOperationException("bug") : null);
        await Assert.ThrowsAsync<InvalidOperationException>(controller.GetStreamingFollowerUsersAsync);
    }

    [Fact]
    public async Task Initialize_continues_with_empty_user_and_stream_even_when_the_response_is_empty()
    {
        Settings.Default.AccessToken = "token";
        var controller = new TwitchApiController(Keys(), FakeHttp(_ => Task.FromResult(Json(HttpStatusCode.OK, ""))));
        await controller.InitializeAsync();
        // 例外が出ずに終わる（空の User・Stream で続行）。ユーザー ID が無いので、一覧は空
        Assert.Empty(await controller.GetStreamingFollowerUsersAsync());
    }

    [Fact]
    public async Task Initialize_continues_even_for_an_unexpected_exception()
    {
        Settings.Default.AccessToken = "token";
        var controller = new TwitchApiController(Keys(), FakeHttp(_ => throw new InvalidOperationException("bug")));
        await controller.InitializeAsync();
        Assert.Empty(await controller.GetStreamingFollowerUsersAsync());
    }
}
