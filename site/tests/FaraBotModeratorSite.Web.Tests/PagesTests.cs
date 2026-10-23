using FaraBotModeratorSite.Content;
using FaraBotModeratorSite.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FaraBotModeratorSite.Web.Tests;

/// <summary>ページの部品（Pages）の中身。「コマンド実行」の入口とゲームのページのつながり、プライバシーポリシーの話題、ホームの更新履歴。</summary>
public class PagesTests
{
    private static readonly string SiteDir = Path.GetDirectoryName(Path.GetDirectoryName(FindUp("Content/pages.json")))!;
    private static readonly SiteContent Content = new(ContentBuilder.LoadFrom(SiteDir));

    private static string FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }

    private static async Task<string> RenderAsync<T>(IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Content);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            return output.ToHtmlString();
        });
        // Razor は日本語を &#x...; にするので、読みやすい形に戻して調べる
        return System.Net.WebUtility.HtmlDecode(html);
    }

    private static string Text(string html) =>
        System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");

    private static Dictionary<string, object?> Slug(string slug) => new() { ["Slug"] = slug };

    [Fact]
    public async Task Beat_Saber_page_has_breadcrumb_to_the_hub_and_its_sections()
    {
        var html = await RenderAsync<Doc>(Slug("beatsaber"));
        Assert.Contains("href=\"/docs/commands\"", html);
        Assert.Contains("!bsr", html);
        Assert.Contains("!bombreset", html);
        Assert.Contains("1.29.1", html);
        Assert.Contains("1.40.8", html);
        Assert.Contains("SiraUtil", html);
        Assert.Contains("BeatSaberMarkupLanguage", html);
        Assert.Contains("(ランダム)", html.Replace("（", "(").Replace("）", ")"));
        Assert.Contains("Play Mode", html);
        Assert.DoesNotContain("BombLineCount を", html);
        Assert.DoesNotContain("href=\"https://github.com/fara1991/FaraBombRush\"", html);
    }

    [Fact]
    public async Task Hub_lists_each_game_with_a_link_to_its_page()
    {
        var html = await RenderAsync<Doc>(Slug("commands"));
        Assert.Contains("href=\"/docs/pokemon\"", html);
        Assert.Contains("href=\"/docs/beatsaber\"", html);
        Assert.DoesNotContain("href=\"/docs/pokemon-my\"", html);   // 子の子は、入口ではなく、ポケモンのページに出す
    }

    [Fact]
    public async Task Game_page_shows_breadcrumb_back_to_the_hub_and_its_sub_pages()
    {
        var html = await RenderAsync<Doc>(Slug("pokemon-my"));
        Assert.Contains("href=\"/docs/commands\"", html);
        Assert.Contains("href=\"/docs/pokemon\"", html);
        Assert.Contains("href=\"/docs/pokemon-team\"", html);
    }

    [Fact]
    public async Task Doc_page_has_one_h1_a_table_of_contents_and_a_pager()
    {
        var html = await RenderAsync<Doc>(Slug("main-settings"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("aria-label=\"このページの目次\"", html);
        Assert.Contains("aria-label=\"前後のページ\"", html);
        Assert.Contains("href=\"/docs/main-settings#", html);   // 目次は、ページの URL + #見出し
    }

    [Fact]
    public async Task Privacy_page_covers_the_topics_and_does_not_claim_services_the_site_does_not_use()
    {
        var text = Text(await RenderAsync<Privacy>());
        foreach (var topic in new[] { "運営者", "ブラウザに保存する情報", "アクセス解析・広告", "Web Analytics", "問い合わせで送る情報", "Turnstile", "Discord", "外部のサービス", "第三者への提供", "Windows アプリ" })
            Assert.Contains(topic, text);
        // このサイトは外部のフォント・CDN を使わない（CSP が同じオリジンだけ）ので、載せない
        Assert.DoesNotContain("Google Fonts", text);
        Assert.DoesNotContain("jsDelivr", text);
    }

    [Fact]
    public async Task Home_shows_the_three_newest_changelog_titles_and_the_download_link()
    {
        var html = await RenderAsync<Home>();
        foreach (var entry in Content.Changelog.Take(3)) Assert.Contains((entry.Title), html);
        Assert.DoesNotContain((Content.Changelog[3].Title), html);
        Assert.Contains(SiteInfo.ReleasesUrl, html);
    }

    [Fact]
    public async Task Changelog_page_lists_every_entry_with_its_details()
    {
        var html = await RenderAsync<Changelog>();
        foreach (var entry in Content.Changelog)
        {
            Assert.Contains((entry.Title), html);
            foreach (var item in entry.Items) Assert.Contains((item), html);
        }
    }

    [Fact]
    public void Page_titles_follow_the_page_name_pipe_site_name_form()
    {
        Assert.Equal("ドキュメント | FaraBotModerator", PageMeta.DocsTitle);
        Assert.Equal("問い合わせ | FaraBotModerator", PageMeta.ReportTitle);
        Assert.Equal("FaraBotModerator | Twitch 配信者向けチャットボット", PageMeta.HomeTitle);
    }
}
