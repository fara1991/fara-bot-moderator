using System.Linq;
using System.Threading.Tasks;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// OAuth の戻りの state の照合と、認可のスコープの並び。
/// </summary>
public class TwitchAuthServiceTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("", "abc")]
    [InlineData("abc", "")]
    [InlineData("abc", "abd")]
    [InlineData("abc", "ABC")]
    public void Empty_or_different_states_do_not_match(string expected, string actual)
    {
        Assert.False(TwitchAuthService.StatesMatch(expected, actual));
    }

    [Fact]
    public void The_same_non_empty_state_matches()
    {
        Assert.True(TwitchAuthService.StatesMatch("abc", "abc"));
    }

    [Fact]
    public void Scopes_have_no_duplicates_and_use_uppercase_escapes()
    {
        Assert.Equal(TwitchAuthService.Scopes.Length, TwitchAuthService.Scopes.Distinct().Count());
        Assert.All(TwitchAuthService.Scopes, scope => Assert.DoesNotContain("%3a", scope));
    }

    private const string ReturnUrl = "http://localhost:8080/?code=abc&scope=bits%3Aread&state=xyz";

    [Fact]
    public void The_same_return_url_is_not_handled_twice()
    {
        Assert.False(TwitchAuthService.ShouldHandleAuthUrl(ReturnUrl, ReturnUrl, "xyz", 8080));
    }

    [Fact]
    public void Nothing_is_handled_before_authentication_starts()
    {
        // state が空（認証を始めていない・認証の後）は、戻りの URL があっても無視する
        Assert.False(TwitchAuthService.ShouldHandleAuthUrl(ReturnUrl, "", "", 8080));
    }

    [Fact]
    public void A_url_without_a_code_is_not_handled()
    {
        const string noCode = "http://localhost:8080/?scope=bits%3Aread&state=xyz";
        Assert.False(TwitchAuthService.ShouldHandleAuthUrl(noCode, "", "xyz", 8080));
    }

    [Fact]
    public void A_new_return_url_is_handled()
    {
        Assert.True(TwitchAuthService.ShouldHandleAuthUrl(ReturnUrl, "", "xyz", 8080));
    }
}

/// <summary>
/// チャットの表示待ちの行（送った順のまま、アイコンが届いてから表示する）。
/// </summary>
public class ChatRowQueueTests
{
    [Fact]
    public void A_later_row_waits_until_the_head_icon_arrives()
    {
        var queue = new ChatRowQueue();
        var slowIcon = new TaskCompletionSource<string?>();
        queue.Enqueue(new ChatModel { Name = "A", Chat = "1" }, slowIcon.Task);
        queue.Enqueue(new ChatModel { Name = "B", Chat = "2" }, Task.FromResult<string?>("iconB"));

        // A のアイコンが届くまで、先に取れた B は出さない（表示の順を入れ替えない）
        Assert.Null(queue.TryPick());

        slowIcon.SetResult("iconA");
        var first = queue.TryPick();
        var second = queue.TryPick();
        Assert.Equal("A", first!.Name);
        Assert.Equal("iconA", first.Icon);
        Assert.Equal("B", second!.Name);
        Assert.Equal("iconB", second.Icon);
        Assert.Null(queue.TryPick());
    }

    [Fact]
    public void A_failed_or_empty_icon_still_shows_the_row_without_an_icon()
    {
        var queue = new ChatRowQueue();
        var failed = new TaskCompletionSource<string?>();
        failed.SetException(new InvalidOperationException("取得できませんでした"));
        queue.Enqueue(new ChatModel { Name = "A" }, failed.Task);
        queue.Enqueue(new ChatModel { Name = "B" }, Task.FromResult<string?>(null));

        var first = queue.TryPick();
        var second = queue.TryPick();
        Assert.Equal("", first!.Icon);
        Assert.Equal("", second!.Icon);
    }
}
