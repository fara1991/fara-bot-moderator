using FaraBotModeratorSite.Components.Layout;
using FaraBotModeratorSite.Components.Pages;
using FaraBotModeratorSite.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FaraBotModeratorSite.Tests;

/// <summary>3 サイト共通の形のフッター（サイト名 | 問い合わせ | プライバシーポリシー | © 公開年 Fara）と、報告・プライバシーポリシーのページ。</summary>
public class FooterAndReportTests
{
    private sealed class TestNav : NavigationManager
    {
        public TestNav(string uri) => Initialize("https://bot.example/", uri);
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private static async Task<string> RenderAsync<T>(string uri = "https://bot.example/docs/overview", SiteOptions? options = null) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNav(uri));
        services.AddSingleton<IOptions<SiteOptions>>(Options.Create(options ?? new SiteOptions
        {
            Title = "FaraBotModerator",
            PublishedYear = 2026,
            PortfolioUrl = "https://fara-labs.com/",
            RepositoryUrl = "https://github.com/fara1991/fara-bot-moderator",
        }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>();
            return output.ToHtmlString();
        });
    }

    private static string Text(string html) => System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    [Fact]
    public async Task Footer_has_the_agreed_parts_in_order()
    {
        var html = await RenderAsync<SiteFooter>();
        Assert.Equal("FaraBotModerator | 問い合わせ | プライバシーポリシー | © 2026 Fara", Text(html));
    }

    [Fact]
    public async Task Footer_links_go_to_the_report_page_the_privacy_page_and_the_author_homepage()
    {
        var html = await RenderAsync<SiteFooter>();
        Assert.Contains("href=\"/report?from=%2Fdocs%2Foverview\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        Assert.Contains("href=\"https://fara-labs.com/\"", html);
        Assert.Contains(">Fara</a>", html);
    }

    [Fact]
    public async Task Footer_follows_the_site_options()
    {
        var html = await RenderAsync<SiteFooter>(options: new SiteOptions { Title = "別の名前", PublishedYear = 2030, PortfolioUrl = "https://example.org/" });
        Assert.Equal("別の名前 | 問い合わせ | プライバシーポリシー | © 2030 Fara", Text(html));
        Assert.Contains("href=\"https://example.org/\"", html);
    }

    [Theory]
    [InlineData("/docs/overview", "/report?from=%2Fdocs%2Foverview")]
    [InlineData("/", "/report?from=%2F")]
    [InlineData("/a/b_c-d.e", "/report?from=%2Fa%2Fb_c-d.e")]
    [InlineData("/report", "/report")]            // 報告ページ・プライバシーポリシー自体からは付けない
    [InlineData("/privacy", "/report")]
    [InlineData("/x y", "/report")]               // 英数字・- _ / . 以外を含むパス
    [InlineData("//evil.example", "/report")]
    [InlineData("/<script>", "/report")]
    [InlineData("", "/report")]
    public void Report_link_carries_the_current_page_only_when_it_is_safe(string path, string expected) =>
        Assert.Equal(expected, SiteFooterLinks.ReportHref(path));

    [Fact]
    public void A_too_long_path_is_not_carried() => Assert.Equal("/report", SiteFooterLinks.ReportHref("/" + new string('a', 300)));

    [Fact]
    public async Task Report_page_has_the_form_the_script_expects()
    {
        var html = await RenderAsync<Report>();
        foreach (var id in new[] { "report-form", "report-category", "report-message", "report-contact", "report-website", "report-count", "report-status", "report-submit", "report-turnstile", "report-done", "report-again" })
            Assert.Contains($"id=\"{id}\"", html);
        // 種類の ID は、サーバー（worker/report.js の CATEGORIES）と同じ
        Assert.Contains("value=\"bug\"", html);
        Assert.Contains("value=\"request\"", html);
        Assert.Contains("value=\"other\"", html);
        Assert.Contains("src=\"/js/report.js\"", html);
        // 送信後のお礼は、最初は隠れていて、フォームの外にある（report.js が入力欄と入れ替える）
        Assert.Matches("id=\"report-done\"[^>]*hidden", html);
        Assert.True(html.IndexOf("</form>", StringComparison.Ordinal) < html.IndexOf("id=\"report-done\"", StringComparison.Ordinal));
        Assert.Contains("maxlength=\"2000\"", html);
        Assert.Contains("report-trap", html);
        Assert.Contains("tabindex=\"-1\"", html);
        Assert.Contains("href=\"/privacy\"", html);
        // CSP が script-src 'self' なので、ページの中にスクリプトを書かない（外部ファイルだけ）
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("onclick=", html);
    }

    [Fact]
    public async Task Privacy_page_covers_the_topics_and_does_not_claim_services_the_site_does_not_use()
    {
        var text = Text(await RenderAsync<Privacy>());
        foreach (var topic in new[] { "運営者", "ブラウザに保存する情報", "アクセス解析・広告", "問い合わせで送る情報", "Turnstile", "Discord", "外部のサービス", "第三者への提供", "Windows アプリ" })
            Assert.Contains(topic, text);
        // このサイトは外部のフォント・CDN を使わない（CSP が同じオリジンだけ）ので、載せない
        Assert.DoesNotContain("Google Fonts", text);
        Assert.DoesNotContain("jsDelivr", text);
    }

    [Fact]
    public void Site_options_default_to_the_agreed_year() => Assert.Equal(2026, new SiteOptions().PublishedYear);

    [Fact]
    public void Shipped_settings_use_the_agreed_year_and_homepage()
    {
        var path = FindUp("appsettings.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var site = doc.RootElement.GetProperty("Site");
        Assert.Equal(2026, site.GetProperty("PublishedYear").GetInt32());
        Assert.Equal("https://fara-labs.com/", site.GetProperty("PortfolioUrl").GetString());
    }

    [Fact]
    public async Task Footer_shows_the_sns_icons_with_the_portfolio_urls_and_hides_empty_ones()
    {
        var html = await RenderAsync<SiteFooter>(options: new SiteOptions
        {
            Title = "FaraBotModerator", PublishedYear = 2026,
            GitHubUrl = "https://github.com/fara1991",
            TwitterUrl = "https://twitter.com/game_fara",
            TwitchUrl = "https://twitch.tv/game_fara",
            DiscordUrl = "https://discord.com/invite/B38eCSYyk5",
        });
        Assert.Contains("<a class=\"service-github\" href=\"https://github.com/fara1991\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"GitHub\"", html);
        Assert.Contains("<a class=\"service-twitter\" href=\"https://twitter.com/game_fara\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"Twitter\"", html);
        Assert.Contains("<a class=\"service-twitch\" href=\"https://twitch.tv/game_fara\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"Twitch\"", html);
        Assert.Contains("<a class=\"service-discord\" href=\"https://discord.com/invite/B38eCSYyk5\" target=\"_blank\" rel=\"noopener noreferrer\" aria-label=\"Discord\"", html);
        // 並びは GitHub・Twitter・Twitch・Discord。アイコンは装飾なので aria-hidden
        var order = new[] { "service-github", "service-twitter", "service-twitch", "service-discord" }.Select(c => html.IndexOf(c, StringComparison.Ordinal)).ToArray();
        Assert.All(order, i => Assert.True(i > 0));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "<svg class=\"social-icon\"[^>]*aria-hidden=\"true\"").Count);
        // アイコンの中には文字がない（読み上げ・本文のテキストに出ない）
        Assert.Equal("FaraBotModerator | 問い合わせ | プライバシーポリシー | © 2026 Fara", Text(html));
    }

    [Fact]
    public async Task Footer_has_no_sns_icons_when_no_url_is_set()
    {
        var html = await RenderAsync<SiteFooter>();
        Assert.DoesNotContain("site-footer-socials", html);
        Assert.DoesNotContain("social-icon", html);
    }

    [Fact]
    public void Shipped_sns_urls_in_appsettings_match_the_portfolio_values()
    {
        var path = FindUp("appsettings.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var site = doc.RootElement.GetProperty("Site");
        // GitHub・Twitter・Twitch は、ポートフォリオの wwwroot/data/profile.json（socials）と同じ。Discord は作者の招待 URL
        Assert.Equal("https://github.com/fara1991", site.GetProperty("GitHubUrl").GetString());
        Assert.Equal("https://twitter.com/game_fara", site.GetProperty("TwitterUrl").GetString());
        Assert.Equal("https://twitch.tv/game_fara", site.GetProperty("TwitchUrl").GetString());
        Assert.Equal("https://discord.com/invite/B38eCSYyk5", site.GetProperty("DiscordUrl").GetString());
    }

    [Fact]
    public void Socials_skip_the_empty_urls_and_keep_the_order()
    {
        var all = SiteFooterLinks.Socials(new SiteOptions { GitHubUrl = "https://g.example/", TwitterUrl = "https://t.example/", TwitchUrl = "https://w.example/", DiscordUrl = "https://d.example/" });
        Assert.Equal(new[] { "github", "twitter", "twitch", "discord" }, all.Select(s => s.Service).ToArray());
        var noDiscord = SiteFooterLinks.Socials(new SiteOptions { GitHubUrl = "https://g.example/", TwitchUrl = "https://w.example/", DiscordUrl = "" });
        Assert.Equal(new[] { "github", "twitch" }, noDiscord.Select(s => s.Service).ToArray());
    }

    [Fact]
    public void Side_menu_has_the_inquiry_and_privacy_links_at_the_bottom()
    {
        var nav = File.ReadAllText(FindUp("Components/Layout/NavMenu.razor"));
        var section = nav.IndexOf("<div class=\"nav-section\">このサイト</div>", StringComparison.Ordinal);
        Assert.True(section > nav.IndexOf("Fara のポートフォリオ", StringComparison.Ordinal));
        Assert.Contains("<NavLink class=\"nav-link\" href=\"/report\"><NavIcon Name=\"mail\" /><span>問い合わせ</span></NavLink>", nav[section..]);
        Assert.Contains("<NavLink class=\"nav-link\" href=\"/privacy\"><NavIcon Name=\"shield\" /><span>プライバシーポリシー</span></NavLink>", nav[section..]);
    }

    [Fact]
    public void The_visible_label_is_問い合わせ_everywhere_in_the_components()
    {
        foreach (var file in new[] { "Components/Layout/SiteFooter.razor", "Components/Layout/NavMenu.razor", "Components/Pages/Report.razor", "Components/Pages/Privacy.razor" })
            Assert.DoesNotContain("作者へ報告", File.ReadAllText(FindUp(file)));
        Assert.Contains("<PageTitle>問い合わせ | @Site.Value.Title</PageTitle>", File.ReadAllText(FindUp("Components/Pages/Report.razor")));
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
