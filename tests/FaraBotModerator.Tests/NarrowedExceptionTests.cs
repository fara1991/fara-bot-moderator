using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using TwitchLib.Api.Core.Exceptions;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// CA1031（例外を広く握る）を絞った箇所の判定。起こりうる失敗（通信・時間切れ・API の応答）は握って続け、
/// コードの誤り（NullReferenceException など）は握らずに上位へ出す。
/// </summary>
public class NarrowedExceptionTests
{
    public static TheoryData<Exception> ExpectedApiFailures => new()
    {
        new HttpRequestException("接続できない"),
        new TaskCanceledException("時間切れ"),
        new OperationCanceledException(),
        new Newtonsoft.Json.JsonReaderException("壊れた応答"),
        new BadParameterException("引数が足りない"),
        new ClientIdAndOAuthTokenRequired("認証情報が無い"),
        new InvalidCredentialException("認証情報が不正"),
        new BadRequestException("400", null!),
        new BadTokenException("401", null!),
        new TooManyRequestsException("429", "0", null!),
        new InternalServerErrorException("500", null!),
    };

    public static TheoryData<Exception> UnexpectedFailures => new()
    {
        new NullReferenceException(),
        new InvalidOperationException(),
        new ArgumentNullException("x"),
        new IndexOutOfRangeException(),
        new InvalidCastException(),
        new TimeoutException(),
    };

    [Theory]
    [MemberData(nameof(ExpectedApiFailures))]
    public void Twitch_api_failures_are_caught_and_logged(Exception ex)
    {
        Assert.True(TwitchApiController.IsExpectedApiFailure(ex));
    }

    [Theory]
    [MemberData(nameof(UnexpectedFailures))]
    public void Twitch_api_code_bugs_are_not_caught(Exception ex)
    {
        Assert.False(TwitchApiController.IsExpectedApiFailure(ex));
    }

    [Fact]
    public void Bouyomi_connection_failures_are_only_network_errors_and_timeouts()
    {
        Assert.True(BouyomiChanController.IsConnectionFailure(new HttpRequestException()));
        Assert.True(BouyomiChanController.IsConnectionFailure(new TaskCanceledException()));
        Assert.True(BouyomiChanController.IsConnectionFailure(new OperationCanceledException()));
        Assert.False(BouyomiChanController.IsConnectionFailure(new NullReferenceException()));
        Assert.False(BouyomiChanController.IsConnectionFailure(new InvalidOperationException()));
    }

    [Fact]
    public async Task Bouyomi_is_running_when_the_server_answers_even_with_an_error_status()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.MethodNotAllowed)));
        using var client = new HttpClient(handler);
        Assert.True(await BouyomiChanController.IsRunningAsync(client, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Bouyomi_is_not_running_when_the_connection_is_refused()
    {
        using var handler = new FakeHandler((_, _) => throw new HttpRequestException("refused"));
        using var client = new HttpClient(handler);
        Assert.False(await BouyomiChanController.IsRunningAsync(client, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Bouyomi_is_not_running_when_the_request_times_out()
    {
        using var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        });
        using var client = new HttpClient(handler);
        Assert.False(await BouyomiChanController.IsRunningAsync(client, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task Bouyomi_check_does_not_swallow_a_code_bug()
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("bug"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BouyomiChanController.IsRunningAsync(client, TimeSpan.FromSeconds(5)));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
