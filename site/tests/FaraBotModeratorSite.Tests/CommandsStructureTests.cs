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
    public void Commands_hub_is_the_only_page_in_its_group_in_the_index()
    {
        var content = LoadContent();
        var group = content.Groups.Single(g => g.Name == "コマンド実行");
        Assert.Equal(new[] { "/docs/commands" }, group.Pages.Select(p => p.Url));
    }

    [Fact]
    public void Games_are_children_of_the_hub_and_pokemon_pages_are_children_of_pokemon()
    {
        var content = LoadContent();
        var hub = content.Find("commands")!;
        // Beat Saber は、チャットのコマンドの説明が無いので入れていない（PR 本文を参照）
        Assert.Equal(new[] { "pokemon" }, content.Children(hub).Select(p => p.Slug));
        Assert.Equal(new[] { "pokemon-my", "pokemon-team", "pokemon-sync", "pokemon-battle", "pokemon-rival", "pokemon-notes" },
            content.Children(content.Find("pokemon")!).Select(p => p.Slug));
        Assert.Equal(new[] { "commands", "pokemon" }, content.Ancestors(content.Find("pokemon-my")!).Select(p => p.Slug));
        Assert.Empty(content.Ancestors(hub));
    }

    [Fact]
    public void Child_pages_take_the_group_of_their_top_level_page()
    {
        var content = LoadContent();
        Assert.All(content.Pages.Where(p => p.Parent is not null), p => Assert.Equal("コマンド実行", p.Group));
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("pokemon-my")]
    [InlineData("pokemon-team")]
    [InlineData("pokemon-sync")]
    [InlineData("pokemon-battle")]
    [InlineData("pokemon-rival")]
    [InlineData("pokemon-notes")]
    public void Old_pokemon_urls_still_resolve(string slug) =>
        Assert.NotNull(LoadContent().Find(slug));

    [Fact]
    public void Previous_and_next_follow_the_reading_order_through_the_hub()
    {
        var content = LoadContent();
        var (prev, next) = content.Neighbors(content.Find("pokemon-my")!);
        Assert.Equal("pokemon", prev!.Slug);
        Assert.Equal("pokemon-team", next!.Slug);

        var (beforeFaq, afterFaq) = content.Neighbors(content.Find("pokemon-notes")!);
        Assert.Equal("pokemon-rival", beforeFaq!.Slug);
        Assert.Equal("faq", afterFaq!.Slug);
    }

    [Fact]
    public void Search_index_has_the_hub_and_the_game_pages()
    {
        var urls = LoadContent().BuildSearchIndex().Select(e => e.Url).ToHashSet();
        Assert.Contains("/docs/commands", urls);
        Assert.Contains("/docs/pokemon-my", urls);
    }

    [Fact]
    public async Task Hub_lists_each_game_with_a_link_to_its_page()
    {
        var content = LoadContent();
        var html = await RenderAsync<Doc>(content, new Dictionary<string, object?> { ["Slug"] = "commands" });
        Assert.Contains("href=\"/docs/pokemon\"", html);
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

    [Fact]
    public void Tree_validation_rejects_unknown_parents_cycles_and_groupless_top_pages()
    {
        static PageDefinition Def(string slug, string parent = "", string group = "") =>
            new() { Slug = slug, Title = slug, Parent = parent, Group = group };

        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: "none", group: "g") }));
        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: "b"), Def("b", parent: "a") }));
        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: "a") }));
        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a") }));
        ContentService.ValidateTree(new[] { Def("a", group: "g"), Def("b", parent: "a") });
    }

    [Fact]
    public void Null_parent_or_group_in_pages_json_is_a_validation_error_not_a_crash()
    {
        static PageDefinition Def(string slug, string? parent, string? group) =>
            new() { Slug = slug, Title = slug, Parent = parent!, Group = group! };

        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: null, group: "g") }));
        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: "", group: null) }));
        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: "", group: "g"), Def("b", parent: null, group: null) }));
        Assert.Throws<InvalidOperationException>(() => ContentService.ValidateTree(new[] { Def("a", parent: "b", group: "g"), Def("b", parent: null, group: "g") }));
    }

    [Fact]
    public void Shipped_page_definitions_pass_the_tree_check()
    {
        var definitions = System.Text.Json.JsonSerializer.Deserialize<List<PageDefinition>>(
            File.ReadAllText(Path.Combine(SiteDir, "Content", "pages.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        ContentService.ValidateTree(definitions);
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
