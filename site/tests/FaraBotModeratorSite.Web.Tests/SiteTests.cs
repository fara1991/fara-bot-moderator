using System.Text.RegularExpressions;
using FaraBotModeratorSite.Build;
using FaraBotModeratorSite.Content;
using Xunit;

namespace FaraBotModeratorSite.Web.Tests;

/// <summary>静的な本文（検索エンジン・JavaScript なし用）、index.html、CSP の方針、保存のキー。</summary>
public class SiteTests
{
    private static readonly string SiteDir = Path.GetDirectoryName(Path.GetDirectoryName(FindUp("Content/pages.json")))!;
    private static string WebDir => Path.Combine(SiteDir, "src", "FaraBotModeratorSite.Web");

    private static string FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public async Task Every_page_gets_a_static_body_with_one_h1_and_the_entry_class()
    {
        var content = new SiteContent(ContentBuilder.LoadFrom(SiteDir));
        var json = await StaticPages.BuildPagesJsonAsync(content);
        var pages = json["pages"]!.AsArray();
        Assert.Equal(content.Pages.Count + 5, pages.Count);   // ホーム・一覧・更新履歴・プライバシー・問い合わせ
        foreach (var page in pages)
        {
            var path = page!["path"]!.GetValue<string>();
            var body = page["bodyHtml"]!.GetValue<string>();
            Assert.StartsWith("<main class=\"entry-static\">", body);
            Assert.Single(Regex.Matches(body, "<h1[ >]"));
            Assert.DoesNotContain("href=\"#", body);   // <base href="/"> のため、#だけのリンクはホームへ移る
            Assert.True(page["description"]!.GetValue<string>().Length <= 160, path);
        }
        Assert.True(pages.Single(p => p!["path"]!.GetValue<string>() == "/report")!["noindex"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_static_report_page_uses_the_same_personal_info_notice_as_the_form()
    {
        // 問い合わせのフォーム（kit の ReportFormView）と、JavaScript なしで読める静的な本文は、同じ注意書きにする
        var razor = File.ReadAllText(FindUp("external/fara-blazor-component/src/FaraBlazorComponent.Razor/Components/ReportFormView.razor"));
        var notice = Regex.Match(razor, @"個人情報（[^）]+）は書かないでください。").Value;
        Assert.NotEmpty(notice);
        var json = await StaticPages.BuildPagesJsonAsync(new SiteContent(ContentBuilder.LoadFrom(SiteDir)));
        var body = json["pages"]!.AsArray().Single(p => p!["path"]!.GetValue<string>() == "/report")!["bodyHtml"]!.GetValue<string>();
        Assert.Contains(notice, body);
    }

    [Fact]
    public async Task The_static_doc_body_carries_the_same_html_as_the_page_data()
    {
        var data = ContentBuilder.LoadFrom(SiteDir);
        var pages = (await StaticPages.BuildPagesJsonAsync(new SiteContent(data)))["pages"]!.AsArray();
        var overview = pages.Single(p => p!["path"]!.GetValue<string>() == "/docs/overview")!["bodyHtml"]!.GetValue<string>();
        Assert.Contains(data.Pages.Single(p => p.Slug == "overview").Html, overview);
    }

    [Fact]
    public void Storage_keys_in_index_html_match_SiteInfo_and_the_old_static_site()
    {
        var html = File.ReadAllText(Path.Combine(WebDir, "wwwroot", "index.html"));
        Assert.Contains($"data-storage-key=\"{SiteInfo.StorageKey}\"", html);
        Assert.Contains($"data-sidebar-key=\"{SiteInfo.SidebarKey}\"", html);
        Assert.Equal("fara-bot-moderator-site.theme", SiteInfo.StorageKey);          // 以前のキーのまま
        Assert.Equal("fara-bot-moderator-site.sidebar-hidden", SiteInfo.SidebarKey);
        Assert.Contains("viewport-fit=cover", html);
        // 旧い値を直す site-init.js は、kit の theme-init.js より前に読む
        Assert.True(html.IndexOf("<script src=\"js/site-init.js\"", StringComparison.Ordinal) < html.IndexOf("<script src=\"_content/FaraBlazorComponent.Razor/js/theme-init.js\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_page_has_an_og_image_that_exists_as_a_1200x630_png()
    {
        var json = await StaticPages.BuildPagesJsonAsync(new SiteContent(ContentBuilder.LoadFrom(SiteDir)));
        foreach (var page in json["pages"]!.AsArray())
            Assert.Equal(StaticPages.OgImagePath, (string?)page!["ogImage"]);
        Assert.Matches(@"^/og\.png\?v=\d+$", StaticPages.OgImagePath);   // 差し替えるときは版を上げる
        var png = File.ReadAllBytes(Path.Combine(WebDir, "wwwroot", StaticPages.OgImagePath.TrimStart('/').Split('?')[0]));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal(1200, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);   // IHDR の幅
        Assert.Equal(630, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);    // IHDR の高さ
    }

    [Fact]
    public void Favicons_are_linked_outside_the_seo_head_and_favicon_ico_sits_at_the_root()
    {
        var wwwroot = Path.Combine(WebDir, "wwwroot");
        var html = File.ReadAllText(Path.Combine(wwwroot, "index.html"));
        var outsideSeoHead = html[(html.IndexOf("SEO-HEAD:END", StringComparison.Ordinal))..];
        foreach (var href in new[] { "favicon.svg", "favicon.ico", "favicon-32x32.png", "favicon-16x16.png", "apple-touch-icon.png" })
        {
            Assert.Contains($"href=\"/{href}?v=1\"", outsideSeoHead);   // 差し替えるときは ?v= を上げる（HTML と合わせる）
            Assert.True(File.Exists(Path.Combine(wwwroot, href)), href);   // favicon.ico が無いと、/favicon.ico は 404 ページになる
        }
        Assert.DoesNotContain("prefers-color-scheme", File.ReadAllText(Path.Combine(wwwroot, "favicon.svg")));   // Safari 非対応。PNG・ICO と見た目をそろえる
    }

    [Fact]
    public void Index_html_has_the_markers_build_pages_needs_and_no_external_hosts()
    {
        var html = File.ReadAllText(Path.Combine(WebDir, "wwwroot", "index.html"));
        foreach (var marker in new[] { "SEO-HEAD:START", "SEO-HEAD:END", "SEO-BODY:START", "SEO-BODY:END" })
            Assert.Contains(marker, html);
        Assert.DoesNotContain("fonts.googleapis.com", html);   // 外部のフォントは使わない（site/CLAUDE.md）
        Assert.DoesNotContain("<script>", html);               // インラインの script は CSP で使えない
    }

    [Fact]
    public void Razor_files_have_no_inline_scripts_or_event_attributes()
    {
        var files = Directory.EnumerateFiles(Path.Combine(SiteDir, "src"), "*.razor", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("<script", text);
            Assert.False(Regex.IsMatch(text, @"\son(click|load|error|mouse\w+)\s*="), file);
        }
    }

    [Fact]
    public void Site_urls_match_the_portfolio_values_and_the_agreed_year()
    {
        Assert.Equal(2026, SiteInfo.PublishedYear);
        Assert.Equal("https://fara-labs.com/", SiteInfo.PortfolioUrl);
        Assert.Equal(new[] { "github", "twitter", "twitch", "discord" }, SiteInfo.Social.Select(s => s.Service));
        Assert.Equal("https://github.com/fara1991", SiteInfo.Social[0].Url);
        Assert.Equal("https://twitter.com/game_fara", SiteInfo.Social[1].Url);
        Assert.Equal("https://twitch.tv/game_fara", SiteInfo.Social[2].Url);
        Assert.Equal("https://discord.com/invite/B38eCSYyk5", SiteInfo.Social[3].Url);
        Assert.Equal("twitch-bot-dev.fara-labs.com", SiteInfo.DevelopHost);
    }

    [Fact]
    public void The_pipeline_config_declares_the_csp_hosts_for_turnstile_and_web_analytics_only()
    {
        var json = File.ReadAllText(Path.Combine(SiteDir, "build", "site.json"));
        Assert.Contains("\"bodyRootClass\": \"entry-static\"", json);
        var hosts = Regex.Matches(json, "https://[a-z.-]+").Select(m => m.Value).Distinct().Order().ToArray();
        Assert.Equal(new[] { "https://challenges.cloudflare.com", "https://cloudflareinsights.com", "https://static.cloudflareinsights.com", "https://twitch-bot-dev.fara-labs.com", "https://twitch-bot.fara-labs.com" }, hosts);
    }
}
