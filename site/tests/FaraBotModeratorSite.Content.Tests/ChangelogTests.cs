using FaraBotModeratorSite.Content;
using Xunit;

namespace FaraBotModeratorSite.Content.Tests;

/// <summary>更新履歴（Content/changelog.json と ContentBuilder.ParseChangelog）。</summary>
public class ChangelogTests
{
    [Fact]
    public void ParseChangelog_returns_entries_newest_first_and_keeps_order_within_a_day()
    {
        var list = ContentBuilder.ParseChangelog("""
            [
              { "date": "2026-10-01", "title": "古い" },
              { "date": "2026-10-20", "title": "同じ日の 1 つ目", "items": ["a", "b"] },
              { "date": "2026-10-20", "title": "同じ日の 2 つ目" }
            ]
            """);
        Assert.Equal(new[] { "同じ日の 1 つ目", "同じ日の 2 つ目", "古い" }, list.Select(e => e.Title));
        Assert.Equal(new[] { "a", "b" }, list[0].Items);
        Assert.Empty(list[2].Items);
        Assert.Equal(new DateOnly(2026, 10, 20), list[0].Date);
    }

    [Fact]
    public void ParseChangelog_accepts_an_empty_list() =>
        Assert.Empty(ContentBuilder.ParseChangelog("[]"));

    [Theory]
    [InlineData("""[ { "title": "日付なし" } ]""")]
    [InlineData("""[ { "date": "2026-10-20", "title": " " } ]""")]
    [InlineData("""[ { "date": "2026-10-20", "title": "x", "items": [" "] } ]""")]
    [InlineData("""[ { "date": "2026-13-45", "title": "日付が正しくない" } ]""")]
    [InlineData("""[ null ]""")]
    [InlineData("<!doctype html>")]
    public void ParseChangelog_rejects_malformed_input(string json) =>
        Assert.Throws<FormatException>(() => ContentBuilder.ParseChangelog(json));

    [Fact]
    public void Shipped_changelog_is_valid_newest_first_and_has_details()
    {
        var path = FindUp("Content/changelog.json");
        var list = ContentBuilder.ParseChangelog(File.ReadAllText(path));
        Assert.True(list.Count >= 3, "ホームに出す件数（3）より少ない");
        Assert.Equal(list.OrderByDescending(e => e.Date).Select(e => e.Date), list.Select(e => e.Date));   // 新しい順に書く
        Assert.All(list, e => Assert.True(e.Title.Length <= 60, $"{e.Date} の見出しが長い（ホームでは見出しだけ出すので短く）: {e.Title}"));
        Assert.All(list, e => Assert.NotEmpty(e.Items));                                                      // 細かい内容は items に書く
    }

    private static string FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }
}
