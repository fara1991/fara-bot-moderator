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
