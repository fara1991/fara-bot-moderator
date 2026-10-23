namespace FaraBotModeratorSite;

/// <summary>
/// サイトの中身（ページ・グループ・更新履歴）と、ページの並びの問い合わせ。ブラウザでは起動時に /data/site.json から 1 回だけ作る。
/// </summary>
public sealed class SiteContent
{
    private readonly Dictionary<string, DocPage> _bySlug;

    public SiteContent(SiteContentData data)
    {
        Pages = data.Pages;
        Changelog = data.Changelog;
        Search = data.Search;
        Groups = Pages.Where(p => p.Parent is null).GroupBy(p => p.Group).Select(g => new PageGroup(g.Key, g.ToList())).ToList();
        _bySlug = Pages.ToDictionary(p => p.Slug);
    }

    /// <summary>すべてのページ（子ページも含む。定義の順）。</summary>
    public IReadOnlyList<DocPage> Pages { get; }

    /// <summary>サイドメニューとドキュメント一覧に出すグループ（親ページだけ。子ページは親の下に入る）。</summary>
    public IReadOnlyList<PageGroup> Groups { get; }

    /// <summary>更新履歴（新しい順）。</summary>
    public IReadOnlyList<ChangelogEntry> Changelog { get; }

    /// <summary>検索の索引（ページ自体と、ページの中の見出しごとの節）。</summary>
    public IReadOnlyList<SearchEntry> Search { get; }

    public DocPage? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    /// <summary>そのページの直下の子ページ（定義の順）。</summary>
    public IReadOnlyList<DocPage> Children(DocPage page) => Pages.Where(p => p.Parent == page.Slug).ToList();

    /// <summary>親ページをたどった先頭（一番上の親）から、直上の親までの並び。親が無ければ空。パンくずに使う。</summary>
    public IReadOnlyList<DocPage> Ancestors(DocPage page)
    {
        var list = new List<DocPage>();
        for (var current = page; current.Parent is { } parent && _bySlug.TryGetValue(parent, out var up); current = up)
            list.Insert(0, up);
        return list;
    }

    /// <summary>前後のページ（グループの順に、親から子へ、上から読む順）。</summary>
    public (DocPage? Previous, DocPage? Next) Neighbors(DocPage page)
    {
        var ordered = Groups.SelectMany(g => g.Pages).SelectMany(Subtree).ToList();
        var i = ordered.FindIndex(p => p.Slug == page.Slug);
        return (i > 0 ? ordered[i - 1] : null, i >= 0 && i < ordered.Count - 1 ? ordered[i + 1] : null);
    }

    /// <summary>ページと、その子・孫（定義の順、親の次に子）。</summary>
    private IEnumerable<DocPage> Subtree(DocPage page)
    {
        yield return page;
        foreach (var child in Children(page))
            foreach (var descendant in Subtree(child))
                yield return descendant;
    }
}
