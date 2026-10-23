using FaraBotModeratorSite.Content;
using Microsoft.Extensions.Options;

namespace FaraBotModeratorSite.Data;

/// <summary>
/// 静的 SSR の現行サイト用の窓口（Blazor WebAssembly への移行が終わったら、このプロジェクトごとなくなる）。
/// 説明ページの組み立ては FaraBotModeratorSite.Content の ContentBuilder に移した（ブラウザ版と同じ処理を使う）。
/// </summary>
public sealed class ContentService
{
    private readonly IWebHostEnvironment _env;
    private readonly SiteOptions _options;
    private SiteContent _content = new(new SiteContentData());

    public ContentService(IWebHostEnvironment env, IOptions<SiteOptions> options)
    {
        _env = env;
        _options = options.Value;
        Reload();
    }

    /// <summary>すべてのページ（子ページも含む。定義の順）。</summary>
    public IReadOnlyList<DocPage> Pages => _content.Pages;
    /// <summary>サイドメニューとドキュメント一覧に出すグループ（親ページだけ。子ページは親の下に入る）。</summary>
    public IReadOnlyList<PageGroup> Groups => _content.Groups;
    /// <summary>更新履歴（新しい順）。Content/changelog.json。</summary>
    public IReadOnlyList<ChangelogEntry> Changelog => _content.Changelog;

    public static IReadOnlyList<ChangelogEntry> ParseChangelog(string json) => ContentBuilder.ParseChangelog(json);

    public DocPage? Find(string slug) => _content.Find(slug);
    public IReadOnlyList<DocPage> Children(DocPage page) => _content.Children(page);
    public IReadOnlyList<DocPage> Ancestors(DocPage page) => _content.Ancestors(page);
    public (DocPage? Previous, DocPage? Next) Neighbors(DocPage page) => _content.Neighbors(page);
    public IReadOnlyList<SearchEntry> BuildSearchIndex() => _content.Search;

    /// <summary>Manual.md と pages.json を読み直す（開発中は、ファイルを保存してリロードするだけで反映）。</summary>
    public void Reload() =>
        _content = new SiteContent(ContentBuilder.LoadFrom(_env.ContentRootPath, _options.ManualPath));
}
