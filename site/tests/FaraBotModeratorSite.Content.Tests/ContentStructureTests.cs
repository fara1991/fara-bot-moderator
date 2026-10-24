using FaraBotModeratorSite.Content;
using Xunit;

namespace FaraBotModeratorSite.Content.Tests;

/// <summary>「コマンド実行」の親子構造（入口 → ゲームごとのページ）、旧 URL、検索の索引への出方、Manual.md から作ったページの形。</summary>
public class ContentStructureTests
{
    // ソースの site/ フォルダ（Content/pages.json のある場所）
    private static readonly string SiteDir = Path.GetDirectoryName(Path.GetDirectoryName(FindUp("Content/pages.json")))!;

    private static SiteContentData LoadData() => ContentBuilder.LoadFrom(SiteDir);

    private static SiteContent LoadContent() => new(LoadData());

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
        Assert.Equal(new[] { "pokemon", "beatsaber" }, content.Children(hub).Select(p => p.Slug));
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

        var (beforeBeatSaber, afterBeatSaber) = content.Neighbors(content.Find("pokemon-notes")!);
        Assert.Equal("pokemon-rival", beforeBeatSaber!.Slug);
        Assert.Equal("beatsaber", afterBeatSaber!.Slug);

        var (beforeFaq, afterFaq) = content.Neighbors(content.Find("beatsaber")!);
        Assert.Equal("pokemon-notes", beforeFaq!.Slug);
        Assert.Equal("faq", afterFaq!.Slug);
    }

    [Fact]
    public void Search_index_has_the_hub_and_the_game_pages()
    {
        var urls = LoadContent().Search.Select(e => e.Url).ToHashSet();
        Assert.Contains("/docs/commands", urls);
        Assert.Contains("/docs/pokemon-my", urls);
        Assert.Contains("/docs/beatsaber", urls);
    }

    [Fact]
    public void Tables_in_the_docs_are_wrapped_so_they_can_scroll_on_narrow_screens()
    {
        var html = LoadContent().Find("beatsaber")!.Html;
        Assert.Contains("<div class=\"table-wrap\"><table>", html);
        Assert.DoesNotContain("<table>", html.Replace("<div class=\"table-wrap\"><table>", ""));
    }

    [Fact]
    public void Every_page_whose_title_has_kanji_has_a_hiragana_reading_for_search()
    {
        var pages = LoadContent().Pages;
        var kanji = new System.Text.RegularExpressions.Regex(@"\p{IsCJKUnifiedIdeographs}");
        var missing = pages.Where(p => kanji.IsMatch(p.Title) && p.Reading.Length == 0).Select(p => p.Slug).ToList();
        Assert.Empty(missing);
        Assert.All(pages.Where(p => p.Reading.Length > 0), p => Assert.Matches(@"^[ぁ-ゖー]+$", p.Reading));
    }

    [Fact]
    public void Search_index_gives_the_reading_only_to_the_page_entry()
    {
        var entries = LoadContent().Search;
        var withReading = entries.Where(e => e.Reading is not null).ToList();
        Assert.All(withReading, e => Assert.Equal("page", e.Type));
        Assert.Contains(withReading, e => e.Url == "/docs/commands" && e.Reading == "こまんどじっこう");
        Assert.Contains(withReading, e => e.Url == "/docs/faq" && e.Reading == "よくあるしつもん");
    }

    [Fact]
    public void Tree_validation_rejects_unknown_parents_cycles_and_groupless_top_pages()
    {
        static PageDefinition Def(string slug, string parent = "", string group = "") =>
            new() { Slug = slug, Title = slug, Parent = parent, Group = group };

        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: "none", group: "g") }));
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: "b"), Def("b", parent: "a") }));
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: "a") }));
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a") }));
        ContentBuilder.ValidateTree(new[] { Def("a", group: "g"), Def("b", parent: "a") });
    }

    [Fact]
    public void Null_parent_or_group_in_pages_json_is_a_validation_error_not_a_crash()
    {
        static PageDefinition Def(string slug, string? parent, string? group) =>
            new() { Slug = slug, Title = slug, Parent = parent!, Group = group! };

        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: null, group: "g") }));
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: "", group: null) }));
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: "", group: "g"), Def("b", parent: null, group: null) }));
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.ValidateTree(new[] { Def("a", parent: "b", group: "g"), Def("b", parent: null, group: "g") }));
    }

    [Fact]
    public void Shipped_page_definitions_pass_the_tree_check()
    {
        var definitions = System.Text.Json.JsonSerializer.Deserialize<List<PageDefinition>>(
            File.ReadAllText(Path.Combine(SiteDir, "Content", "pages.json")),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        ContentBuilder.ValidateTree(definitions);
    }

    [Fact]
    public void A_heading_missing_from_the_manual_stops_the_build()
    {
        const string pages = """[ { "slug": "a", "title": "A", "group": "g", "sections": [ { "heading": "1. ない見出し" } ] } ]""";
        Assert.Throws<InvalidOperationException>(() => ContentBuilder.Build("# 本\n\n## 2. 別の見出し\n", pages, null));
    }

    [Fact]
    public void A_link_to_a_heading_on_the_same_page_stops_the_build_because_base_href_would_send_it_home()
    {
        const string pages = """[ { "slug": "a", "title": "A", "group": "g", "sections": [ { "heading": "1. 最初" } ] } ]""";
        var manual = "# 本\n\n## 1. 最初\n\n### 小見出し\n\n[上へ](#小見出し)\n";
        var error = Assert.Throws<InvalidOperationException>(() => ContentBuilder.Build(manual, pages, null));
        Assert.Contains("/docs/a", error.Message);
    }

    [Fact]
    public void A_link_to_a_heading_on_another_page_becomes_a_link_to_that_page()
    {
        const string pages = """
            [ { "slug": "a", "title": "A", "group": "g", "sections": [ { "heading": "1. 最初" } ] },
              { "slug": "b", "title": "B", "group": "g", "sections": [ { "heading": "2. 次" } ] } ]
            """;
        var manual = "# 本\n\n## 1. 最初\n\n[次の小見出し](#小見出し)\n\n## 2. 次\n\n### 小見出し\n\n中身\n";
        var data = ContentBuilder.Build(manual, pages, null);
        Assert.Contains("href=\"/docs/b#%E5%B0%8F%E8%A6%8B%E5%87%BA%E3%81%97\"", data.Pages[0].Html);
    }

    [Fact]
    public void Site_data_survives_a_round_trip_through_json()
    {
        var data = LoadData();
        var json = SiteJson.Serialize(data);
        Assert.DoesNotContain("\\u", json);   // 日本語を \uXXXX にしない（容量のため）
        var back = SiteJson.Deserialize(json);
        Assert.Equal(data.Pages.Count, back.Pages.Count);
        Assert.Equal(data.Pages.Select(p => p.Html), back.Pages.Select(p => p.Html));
        Assert.Equal(data.Pages.Select(p => (p.Slug, p.Parent, p.Reading)), back.Pages.Select(p => (p.Slug, p.Parent, p.Reading)));
        Assert.Equal(data.Changelog.Select(c => (c.Date, c.Title, string.Join("|", c.Items))), back.Changelog.Select(c => (c.Date, c.Title, string.Join("|", c.Items))));
        Assert.Equal(data.Search.Count, back.Search.Count);
        Assert.Equal(data.Search.Select(e => e.Reading), back.Search.Select(e => e.Reading));
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
