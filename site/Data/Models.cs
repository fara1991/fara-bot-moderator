namespace FaraBotModeratorSite.Data;

/// <summary>appsettings.json の Site セクション。サイト名や外部リンクはここにだけ書く。</summary>
public sealed class SiteOptions
{
    public string Title { get; set; } = "FaraBotModerator";
    public string Description { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";
    public string ReleasesUrl { get; set; } = "";
    public string PortfolioUrl { get; set; } = "";
    public string PokemonSiteUrl { get; set; } = "";
    /// <summary>説明の元になる Manual.md の場所（プロジェクトのフォルダからの相対パス）。</summary>
    public string ManualPath { get; set; } = "../Manual.md";
    /// <summary>フッターの © に出す、サイトを公開した年。</summary>
    public int PublishedYear { get; set; } = 2026;
}

/// <summary>更新履歴の 1 件（Content/changelog.json）。ホームは見出しだけ、/changelog は細かい内容（Items）まで出す。</summary>
public sealed class ChangelogEntry
{
    public DateOnly Date { get; set; }
    /// <summary>短い見出し（ホームにも出す）。</summary>
    public string Title { get; set; } = "";
    /// <summary>細かい内容（/changelog だけに出す）。</summary>
    public List<string> Items { get; set; } = new();
}

/// <summary>ページ内の見出し（右側の「このページの目次」に出す）。</summary>
public sealed record TocItem(string Id, string Text, int Level);

/// <summary>1 つの説明ページ。</summary>
public sealed record DocPage(string Slug, string Title, string Group, string Description, string Html, IReadOnlyList<TocItem> Toc)
{
    public string Url => $"/docs/{Slug}";
}

/// <summary>サイドバーの 1 グループ。</summary>
public sealed record PageGroup(string Name, IReadOnlyList<DocPage> Pages);

/// <summary>Content/pages.json の 1 ページ分の定義。</summary>
public sealed class PageDefinition
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Group { get; set; } = "";
    public string Description { get; set; } = "";
    public List<SectionDefinition> Sections { get; set; } = new();
}

/// <summary>ページに載せる Manual.md の見出し。Own が true なら、その見出しの直下の文章だけ（下の見出しは含めない）。</summary>
public sealed class SectionDefinition
{
    public string Heading { get; set; } = "";
    public bool Own { get; set; }
}

/// <summary>/search-index.json の 1 件（検索はクライアント側の wwwroot/js/search.js で行う）。ページ自体と、ページの中の見出しごとの節を 1 件ずつ持つ。</summary>
/// <param name="Type">page = ページ自体 / section = ページの中の見出し</param>
/// <param name="Title">節の見出し（ページ自体の場合はページの題名）</param>
/// <param name="Page">ページの題名</param>
/// <param name="Group">ページのグループ</param>
/// <param name="Url">行き先（見出しなら #見出し 付き）</param>
/// <param name="Text">本文のプレーンテキスト（一致する場所の抜き出しと、部分一致に使う）</param>
public sealed record SearchEntry(string Type, string Title, string Page, string Group, string Url, string Text);
