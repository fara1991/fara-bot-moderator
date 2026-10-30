using FaraBotModeratorSite.Content;
using FaraBotModeratorSite.Pages;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FaraBotModeratorSite.Web.Tests;

/// <summary>ページの部品（Pages）の中身。「コマンド実行」の入口とゲームのページのつながり、プライバシーポリシーの話題、ホームの更新履歴。</summary>
public class PagesTests
{
    // プライバシーポリシーの日付（改定したら、ここと Privacy.razor を同時に直す）
    private const string PrivacyDates = "制定: 2026 年 10 月 22 日 / 最終改定: 2026 年 10 月 29 日";

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
        Assert.DoesNotContain("FaraBombRush", html);
        Assert.Contains("小文字の !bomb", Text(html));
        Assert.DoesNotContain("href=\"https://github.com/fara1991/FaraBombRush\"", html);
    }

    [Fact]
    public async Task Hub_lists_each_game_with_a_link_to_its_page()
    {
        var html = await RenderAsync<Doc>(Slug("commands"));
        Assert.Contains("href=\"/docs/pokemon\"", html);
        Assert.Contains("href=\"/docs/beatsaber\"", html);
        Assert.DoesNotContain("href=\"/docs/pokemon-my\"", html);   // ポケモンの説明は 1 ページにまとめたので、旧子ページへのリンクは無い
    }

    [Fact]
    public async Task Pokemon_page_shows_breadcrumb_back_to_the_hub_and_one_table_of_contents_at_the_top()
    {
        var html = await RenderAsync<Doc>(Slug("pokemon"));
        Assert.Contains("href=\"/docs/commands\"", html);
        Assert.Contains("aria-label=\"このページの見出し一覧\"", html);
        // 先頭の目次は、h2 の見出しごとに 1 つのリンクを持ち、行き先の見出し（id）が本文にある
        var toc = System.Text.RegularExpressions.Regex.Match(html, "<nav class=\"inline-toc\".*?</nav>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        var links = System.Text.RegularExpressions.Regex.Matches(toc, "href=\"/docs/pokemon#([^\"]+)\"").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.Equal(7, links.Count);
        Assert.All(links, id => Assert.Contains($"<h2 id=\"{id}\"", html));
        // 以前の子ページへのリンクは無い
        Assert.DoesNotContain("href=\"/docs/pokemon-", html);
        Assert.DoesNotContain("含まれるページ", html);
    }

    [Fact]
    public async Task Pages_without_inline_toc_do_not_show_it()
    {
        var html = await RenderAsync<Doc>(Slug("timer"));
        Assert.DoesNotContain("inline-toc", html);
    }

    [Theory]
    [InlineData("main-settings")]
    [InlineData("reaction-events")]
    [InlineData("beatsaber")]
    [InlineData("faq")]
    [InlineData("pokemon")]
    public async Task Long_pages_show_an_inline_toc_whose_links_point_at_headings_that_exist(string slug)
    {
        var html = await RenderAsync<Doc>(Slug(slug));
        Assert.Contains("inline-toc", html);
        var toc = html[html.IndexOf("inline-toc", StringComparison.Ordinal)..];
        toc = toc[..toc.IndexOf("</nav>", StringComparison.Ordinal)];
        var ids = System.Text.RegularExpressions.Regex.Matches(toc, "href=\"/docs/" + slug + "#([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(ids);
        foreach (var id in ids) Assert.Contains($"id=\"{id}\"", html);
    }

    [Fact]
    public async Task Home_links_to_pokemon_sections_point_at_headings_that_exist()
    {
        var home = await RenderAsync<Home>();
        var page = await RenderAsync<Doc>(Slug("pokemon"));
        var ids = System.Text.RegularExpressions.Regex.Matches(home, "href=\"/docs/pokemon#([^\"]+)\"").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Contains($"<h2 id=\"{id}\"", page));
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
        foreach (var topic in new[] { "運営者", "ブラウザに保存する情報", "アクセス解析・広告", "Web Analytics", "問い合わせで送る情報", "Turnstile", "Discord", "外部のサービス", "第三者への提供", "開示・訂正・削除", "このサイトで扱う情報", "アプリ（FaraBotModerator）で扱う情報", "Windows アプリ", "利用目的", PrivacyDates })
            Assert.Contains(topic, text);
        // 問い合わせの回数制限（1 日 3 回・送信元の識別子を当日だけ保存）の記載。「匿名」とは書かない（識別子は送信元に結びつくため）
        Assert.Contains("1 日 3 回", text);
        Assert.Contains("IP アドレスそのものは保存しません", text);
        Assert.Contains("日付が変わったあとの最初の自動削除（日本時間の 0 時ごろ）", text);
        Assert.DoesNotContain("掃除", text);
        Assert.Contains("EU 圏内からのアクセスは計測の対象外です", text);
        Assert.Contains("Cloudflare（Turnstile など）は、その仕組みで、ブラウザに情報を保存することがあります", text);
        Assert.Contains("ブラウザの保存領域", text);
        Assert.Contains("送った内容（本文・連絡先など）そのものは、サイトの側では保存しません", text);
        Assert.DoesNotContain("匿名", text);
        Assert.Contains("広告は、いま使っていません。", text);
        Assert.Contains("このサイトからは Cookie を設定しません", text);
        Assert.DoesNotContain("個人を特定する情報", text);
        // このサイトは外部のフォント・CDN を使わない（CSP が同じオリジンだけ）ので、載せない
        Assert.DoesNotContain("Google Fonts", text);
        Assert.DoesNotContain("jsDelivr", text);
        // 運営者の運用の約束と、法的な適用の注記は書かない
        foreach (var banned in new[] { "保存期間は", "おすすめします", "専門家", "個人関連情報", "外部送信" })
            Assert.DoesNotContain(banned, text);
    }

    [Fact]
    public async Task Privacy_page_does_not_show_implementation_details_to_readers()
    {
        // 閲覧者に要らない技術の詳細（内部の仕組みの名前・保存のキー名・開発用の URL）は出さない（サービスの名前は出す）
        var html = await RenderAsync<Privacy>();
        foreach (var jargon in new[] { "ハッシュ", "トークン", "token", "Token", "localStorage", "Bearer", "pokemon-dev", "localhost", "Durable", "Cloudflare Workers", "秘密の鍵", "OAuth", "fara-bot-moderator-site." })
            Assert.DoesNotContain(jargon, html);
        // 表のセルには、列の見出しを小さい画面で出すための data-label がある（4 列の表は 3 つのセル、権限の表は 1 つ）
        Assert.Equal(5 * 3 + 4 * 3 + 7, Regex.Matches(html, "<td data-label=\"").Count);
        Assert.Equal(5 + 4 + 7, Regex.Matches(html, "<th scope=\"row\">").Count);
        Assert.Contains("class=\"policy-part-title\"", html);
    }

    [Fact]
    public async Task Privacy_page_describes_what_the_app_stores_and_sends()
    {
        var html = await RenderAsync<Privacy>();
        var text = Text(html);
        foreach (var fact in new[] { "DeepL", "Resources\\secrets.json", "暗号化せずに", "user.config", "Client Secret", "アクセスキーを添えて", "Log", "視聴者のユーザー名とチャットの本文", "自動では削除されない", "Twitch との通信", "by 表示名" })
            Assert.Contains(fact, text);
        Assert.DoesNotContain("平文", text);
        Assert.Contains("href=\"https://pokemon.fara-labs.com/\"", html);
        Assert.Contains("href=\"https://pokemon.fara-labs.com/privacy\"", html);
        // アプリの外部との通信の表（Twitch・DeepL・ポケモンのサイト・棒読みちゃん）
        var appRows = Regex.Matches(html, "<tr data-app-service=\"(?<name>[^\"]+)\">").Select(m => m.Groups["name"].Value).ToList();
        Assert.Equal(4, appRows.Count);
        foreach (var service in new[] { "Twitch", "DeepL", "ポケモンのサイト", "棒読みちゃん" })
            Assert.Contains(appRows, r => r.StartsWith(service));
        Assert.Contains("href=\"/docs/overview\"", html);
        Assert.Contains("href=\"/docs/main-settings\"", html);
        // アプリが Twitch に求める権限（TwitchAuthService.Scopes）は、すべてポリシーに載せる（増減したらここで気づく）
        var scopes = AppScopes();
        Assert.Equal(7, scopes.Count);
        foreach (var scope in scopes)
            Assert.Contains($">{scope}<", html);
    }

    private static List<string> AppScopes()
    {
        var source = File.ReadAllText(FindUp("Controllers/TwitchAuthService.cs"));
        var block = Regex.Match(source, @"string\[\] Scopes\s*=\s*\[(?<items>[^\]]*)\]").Groups["items"].Value;
        return Regex.Matches(block, "\"(?<scope>[a-z:_]+)\"").Select(m => m.Groups["scope"].Value).ToList();
    }

    [Fact]
    public async Task Privacy_external_services_table_matches_the_csp_hosts()
    {
        var html = await RenderAsync<Privacy>();
        var rows = Regex.Matches(html, "<tr data-service=\"(?<name>[^\"]+)\">")
            .Select(m => m.Groups["name"].Value).ToList();
        Assert.Equal(new[] { "Cloudflare（サイトの配信）", "Cloudflare Web Analytics", "Cloudflare Turnstile", "Discord", "GitHub" }, rows);

        // ブラウザが読み込む外部ホスト（build/site.json の csp）は、表のどのサービスのものかを決めておく。
        // 新しいホストを足したら、ここと表（Privacy.razor）に載せないとテストが落ちる
        var serviceByHost = new Dictionary<string, string>
        {
            ["https://static.cloudflareinsights.com"] = "Cloudflare Web Analytics",
            ["https://cloudflareinsights.com"] = "Cloudflare Web Analytics",
            ["https://challenges.cloudflare.com"] = "Cloudflare Turnstile",
        };
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(SiteDir, "build", "site.json")))!["csp"]!.AsObject();
        var cspHosts = json.SelectMany(kv => kv.Value!.AsArray().Select(v => v!.GetValue<string>())).Distinct().Order().ToList();
        Assert.NotEmpty(cspHosts);
        foreach (var host in cspHosts)
        {
            Assert.True(serviceByHost.TryGetValue(host, out var service), $"CSP のホスト {host} がプライバシーポリシーの表に対応していません");
            Assert.Contains(service!, rows);
        }
    }

    [Fact]
    public async Task Home_shows_the_three_newest_changelog_titles_and_the_download_link()
    {
        var html = await RenderAsync<Home>();
        foreach (var entry in Content.Changelog.Take(3)) Assert.Contains((entry.Title), html);
        Assert.DoesNotContain((Content.Changelog[3].Title), html);
        Assert.Contains("href=\"/download\"", html);   // ダウンロードは専用のページ（/download）へ案内する
    }

    [Fact]
    public async Task Download_page_links_to_the_latest_release_and_states_only_known_requirements()
    {
        var html = await RenderAsync<Download>();
        var text = Text(html);
        Assert.Contains($"href=\"{SiteInfo.ReleasesUrl}\"", html);
        foreach (var fact in new[] { "FaraBotModerator_Setup.exe", "Windows 10 / 11（x64）", ".NET 8 ランタイム", "WebView2 ランタイム", "設定・認証情報・ログ" })
            Assert.Contains(fact, text);
        foreach (var href in new[] { "/docs/overview", "/docs/main-settings", "/privacy" })
            Assert.Contains($"href=\"{href}\"", html);
        // 動作環境は、アプリの対象とリリースの設定と同じ（変わったらこのページも直す）
        Assert.Contains("<TargetFramework>net8.0-windows", File.ReadAllText(FindUp("FaraBotModerator.csproj")));
        Assert.Contains("FaraBotModerator_Setup.exe", File.ReadAllText(FindUp(".github/workflows/release-app.yml")));
    }

    [Fact]
    public void Side_menu_has_no_links_to_other_sites()
    {
        var nav = File.ReadAllText(Path.Combine(SiteDir, "src", "FaraBotModeratorSite.Web", "Layout", "NavMenu.razor"));
        Assert.DoesNotContain("http", nav);
        Assert.DoesNotContain("SiteInfo.", nav.Replace("SiteInfo.Name", ""));
        Assert.DoesNotContain("target=\"_blank\"", nav);
        Assert.Contains("href=\"/download\"", nav.Replace("Href=", "href="));
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
