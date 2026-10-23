using System.Text.Json.Serialization;

namespace FaraBotModeratorSite;

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

/// <summary>1 つの説明ページ。Parent は親ページの slug（親が無いページは null）。子ページは親のグループに入る。</summary>
public sealed record DocPage(string Slug, string Title, string Group, string Description, string Html, IReadOnlyList<TocItem> Toc, string? Parent = null, string Reading = "")
{
    [JsonIgnore]
    public string Url => $"/docs/{Slug}";
}

/// <summary>サイドバーの 1 グループ。</summary>
public sealed record PageGroup(string Name, IReadOnlyList<DocPage> Pages);

/// <summary>/data/site.json の検索の 1 件。ページ自体と、ページの中の見出しごとの節を 1 件ずつ持つ。</summary>
/// <param name="Type">page = ページ自体 / section = ページの中の見出し</param>
/// <param name="Title">節の見出し（ページ自体の場合はページの題名）</param>
/// <param name="Page">ページの題名</param>
/// <param name="Group">ページのグループ</param>
/// <param name="Url">行き先（見出しなら #見出し 付き）</param>
/// <param name="Text">本文のプレーンテキスト（一致する場所の抜き出しと、部分一致に使う）</param>
/// <param name="Reading">ページの題名の読み（ページ自体の行だけ。無ければ出さない）</param>
public sealed record SearchEntry(string Type, string Title, string Page, string Group, string Url, string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reading = null);

/// <summary>
/// ブラウザが起動時に読む、サイトの中身（/data/site.json）。ビルドツールが Manual.md・Content/pages.json・Content/changelog.json から作る。
/// ページは定義の順（子ページを含む）。検索の索引も、同じファイルに入れる。
/// </summary>
public sealed class SiteContentData
{
    public List<DocPage> Pages { get; set; } = new();
    public List<ChangelogEntry> Changelog { get; set; } = new();
    public List<SearchEntry> Search { get; set; } = new();
}
