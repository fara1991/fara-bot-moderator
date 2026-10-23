using FaraBotModeratorSite.Components.Pages;
using FaraBotModeratorSite.Components.Shared;
using FaraBotModeratorSite.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FaraBotModeratorSite.Tests;

/// <summary>「コマンド実行」の親子構造（入口 → ゲームごとのページ）、旧 URL、検索・一覧・サイドメニューへの出方。</summary>
public class CommandsStructureTests
{
    private sealed class TestNav : NavigationManager
    {
        public TestNav(string uri) => Initialize("https://bot.example/", uri);
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    /// <summary>ContentService が使う ContentRootPath だけを持つ、テスト用の環境。</summary>
    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "FaraBotModeratorSite";
    }

    // ソースの site/ フォルダ（bin に複製された Content/ ではなく、プロジェクトファイルのある場所）
    private static readonly string SiteDir = Path.GetDirectoryName(FindUp("FaraBotModeratorSite.csproj"))!;

    private static ContentService LoadContent()
    {
        var env = new FakeEnvironment { ContentRootPath = SiteDir };
        return new ContentService(env, Options.Create(new SiteOptions { ManualPath = "../Manual.md" }));
    }

    private static async Task<string> RenderAsync<T>(ContentService content, IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(content);
        services.AddSingleton<NavigationManager>(new TestNav("https://bot.example/docs/commands"));
        services.AddSingleton<IOptions<SiteOptions>>(Options.Create(new SiteOptions { Title = "FaraBotModerator" }));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>());
            var output = await renderer.RenderComponentAsync<T>(view);
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task Beat_Saber_page_has_breadcrumb_to_the_hub_and_its_sections()
    {
        var content = LoadContent();
        var html = await RenderAsync<Doc>(content, new Dictionary<string, object?> { ["Slug"] = "beatsaber" });
        Assert.Contains("href=\"/docs/commands\"", html);
        Assert.Contains("!bsr", html);
        Assert.Contains("!bombreset", html);
    }

    [Fact]
    public async Task Hub_lists_each_game_with_a_link_to_its_page()
    {
        var content = LoadContent();
        var html = await RenderAsync<Doc>(content, new Dictionary<string, object?> { ["Slug"] = "commands" });
        Assert.Contains("href=\"/docs/pokemon\"", html);
        Assert.Contains("href=\"/docs/beatsaber\"", html);
        Assert.DoesNotContain("href=\"/docs/pokemon-my\"", html);   // 子の子は、入口ではなく、ポケモンのページに出す
    }

    [Fact]
    public async Task Game_page_shows_breadcrumb_back_to_the_hub_and_its_sub_pages()
    {
        var content = LoadContent();
        var html = await RenderAsync<Doc>(content, new Dictionary<string, object?> { ["Slug"] = "pokemon-my" });
        Assert.Contains("href=\"/docs/commands\"", html);
        Assert.Contains("href=\"/docs/pokemon\"", html);
        Assert.Contains("href=\"/docs/pokemon-team\"", html);
    }

    [Fact]
    public async Task Sidebar_nests_games_under_the_hub_with_depth_classes_and_no_inline_style()
    {
        var content = LoadContent();
        var html = await RenderAsync<NavPageItem>(content, new Dictionary<string, object?> { ["Page"] = content.Find("commands") });
        Assert.Contains("class=\"nav-item\"", html);                                  // 入口は字下げなし
        Assert.Contains("nav-item nav-child nav-depth-1", html);                         // ポケモン（段 1）
        Assert.Contains("nav-item nav-child nav-depth-2", html);                         // 育成ポケモンなど（段 2）
        Assert.Contains("role=\"group\"", html);                                       // 子の一覧は、階層として読める
        Assert.Contains("href=\"/docs/pokemon-battle\"", html);
        // CSP（worker/index.js の style-src 'self'）で style 属性は止まるので、使わない
        Assert.DoesNotContain("style=", html);
    }

    [Fact]
    public void Stylesheet_indents_each_depth_class()
    {
        var css = File.ReadAllText(Path.Combine(SiteDir, "wwwroot", "css", "site.css"));
        Assert.Contains(".sidebar .nav-item.nav-depth-1 {", css);
        Assert.Contains(".sidebar .nav-item.nav-depth-2 {", css);
        Assert.Contains(".sidebar .nav-item.nav-depth-3 {", css);
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
