using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Options;

namespace FaraBotModeratorSite.Data;

/// <summary>
/// 説明ページを作る。本文はリポジトリ直下の Manual.md（アプリに同梱する操作説明書）を見出しで切り分けたもので、
/// どの見出しをどのページにするかは Content/pages.json に書く。Manual.md を直せば、サイトにもそのまま反映される。
/// </summary>
public sealed partial class ContentService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .UseAutoLinks()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    private readonly IWebHostEnvironment _env;
    private readonly SiteOptions _options;
    private IReadOnlyList<DocPage> _pages = Array.Empty<DocPage>();
    private IReadOnlyList<PageGroup> _groups = Array.Empty<PageGroup>();
    private Dictionary<string, DocPage> _bySlug = new();
    private IReadOnlyList<ChangelogEntry> _changelog = Array.Empty<ChangelogEntry>();

    public ContentService(IWebHostEnvironment env, IOptions<SiteOptions> options)
    {
        _env = env;
        _options = options.Value;
        Reload();
    }

    public IReadOnlyList<DocPage> Pages => _pages;
    public IReadOnlyList<PageGroup> Groups => _groups;
    /// <summary>更新履歴（新しい順）。Content/changelog.json。</summary>
    public IReadOnlyList<ChangelogEntry> Changelog => _changelog;

    /// <summary>changelog.json（[ { "date": "2026-10-22", "title": "...", "items": ["..."] } ]）を、新しい順の一覧にする。日付が無い・見出しが空・細かい内容に空の項目があれば FormatException。</summary>
    public static IReadOnlyList<ChangelogEntry> ParseChangelog(string json)
    {
        List<ChangelogEntry> list;
        try
        {
            list = JsonSerializer.Deserialize<List<ChangelogEntry>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        }
        catch (JsonException ex)
        {
            throw new FormatException("changelog.json を読めません: " + ex.Message, ex);
        }
        foreach (var e in list)
        {
            if (e is null) throw new FormatException("changelog.json に null の項目があります");
            if (e.Date == default) throw new FormatException($"changelog.json: date がありません（{e.Title}）");
            if (string.IsNullOrWhiteSpace(e.Title)) throw new FormatException($"changelog.json: {e.Date:yyyy-MM-dd} の title が空です");
            if (e.Items is null || e.Items.Any(string.IsNullOrWhiteSpace)) throw new FormatException($"changelog.json: {e.Date:yyyy-MM-dd} の items に空の項目があります");
        }
        // 同じ日付は書いた順のまま（OrderByDescending は安定ソート）
        return list.OrderByDescending(e => e.Date).ToList();
    }

    public DocPage? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    /// <summary>前後のページ（グループの順に、上から読む順）。</summary>
    public (DocPage? Previous, DocPage? Next) Neighbors(DocPage page)
    {
        var ordered = _groups.SelectMany(g => g.Pages).ToList();
        var i = ordered.FindIndex(p => p.Slug == page.Slug);
        return (i > 0 ? ordered[i - 1] : null, i >= 0 && i < ordered.Count - 1 ? ordered[i + 1] : null);
    }

    /// <summary>Manual.md と pages.json を読み直す（開発中は、ファイルを保存してリロードするだけで反映）。</summary>
    public void Reload()
    {
        var manualPath = Path.GetFullPath(Path.Combine(_env.ContentRootPath, _options.ManualPath));
        var definitionPath = Path.Combine(_env.ContentRootPath, "Content", "pages.json");
        var manual = ManualDocument.Parse(File.ReadAllText(manualPath));
        var definitions = JsonSerializer.Deserialize<List<PageDefinition>>(File.ReadAllText(definitionPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();

        foreach (var d in definitions)
            if (!Regex.IsMatch(d.Slug, "^[a-z0-9-]+$")) throw new InvalidOperationException($"pages.json の slug は、小文字の英数字とハイフンだけにしてください: {d.Slug}");
        if (definitions.Select(d => d.Slug).Distinct().Count() != definitions.Count) throw new InvalidOperationException("pages.json に、同じ slug が重複しています");

        var pages = new List<DocPage>();
        // ページの題名にしたため本文に出さない見出し（1 つ目の見出し）へのリンクは、そのページ自体へ向ける
        var headingPage = new Dictionary<string, string>();
        foreach (var d in definitions)
            if (d.Sections.Count > 0) headingPage[Slugify(manual.FullHeading(d.Sections[0].Heading))] = d.Slug;
        foreach (var d in definitions)
        {
            var markdown = string.Join("\n", d.Sections.Select((s, i) => manual.Extract(s.Heading, s.Own, includeHeading: i > 0)));
            var html = Markdown.ToHtml(markdown, Pipeline);
            pages.Add(new DocPage(d.Slug, d.Title, d.Group, d.Description, html, ExtractToc(html)));
        }

        // 別のページにある見出しへの同じページ内リンク（#見出し）を、そのページへのリンクに直す
        var pageOfId = new Dictionary<string, string>();
        foreach (var p in pages)
            foreach (var t in p.Toc) pageOfId.TryAdd(t.Id, p.Slug);
        pages = pages.Select(p => p with { Html = Decorate(p.Html, p.Slug, pageOfId, headingPage) }).ToList();

        var changelogPath = Path.Combine(_env.ContentRootPath, "Content", "changelog.json");
        _changelog = File.Exists(changelogPath) ? ParseChangelog(File.ReadAllText(changelogPath)) : Array.Empty<ChangelogEntry>();

        _pages = pages;
        _bySlug = pages.ToDictionary(p => p.Slug);
        _groups = pages.GroupBy(p => p.Group).Select(g => new PageGroup(g.Key, g.ToList())).ToList();
    }

    /// <summary>
    /// 検索インデックス。ページ自体（題名・説明）と、ページの中の見出し（h2・h3）ごとの節を 1 件ずつ。
    /// 一致した場所を「ページ › 見出し」で示せるように、節ごとに分けて持つ。
    /// </summary>
    public IReadOnlyList<SearchEntry> BuildSearchIndex()
    {
        var entries = new List<SearchEntry>();
        foreach (var page in _pages)
        {
            entries.Add(new SearchEntry("page", page.Title, page.Title, page.Group, page.Url, page.Description));
            var headings = HeadingPattern().Matches(page.Html).Where(m => m.Groups[1].Value is "2" or "3").ToList();
            var introEnd = headings.Count > 0 ? headings[0].Index : page.Html.Length;
            var intro = PlainText(page.Html[..introEnd]);
            if (intro.Length > 0) entries.Add(new SearchEntry("section", page.Title, page.Title, page.Group, page.Url, intro));
            for (var i = 0; i < headings.Count; i++)
            {
                var start = headings[i].Index + headings[i].Length;
                var end = i + 1 < headings.Count ? headings[i + 1].Index : page.Html.Length;
                var title = PlainText(headings[i].Groups[3].Value);
                var id = System.Net.WebUtility.HtmlDecode(headings[i].Groups[2].Value);
                entries.Add(new SearchEntry("section", title, page.Title, page.Group, $"{page.Url}#{Uri.EscapeDataString(id)}", PlainText(page.Html[start..end])));
            }
        }
        return entries;
    }

    /// <summary>HTML のタグを除き、空白をそろえたプレーンテキストにする。</summary>
    private static string PlainText(string html) =>
        Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ").Trim();

    private static List<TocItem> ExtractToc(string html) =>
        HeadingPattern().Matches(html)
            .Select(m => new TocItem(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value), System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, "<[^>]+>", "")), int.Parse(m.Groups[1].Value)))
            .Where(t => t.Level >= 2)
            .ToList();

    /// <summary>見出しの文字から、GitHub と同じ規則でリンク用の ID を作る（Manual.md の目次リンクの書き方）。</summary>
    private static string Slugify(string heading)
    {
        var text = Regex.Replace(heading.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}\s_-]", "");
        return text.Replace(' ', '-');
    }

    private static string Decorate(string html, string slug, Dictionary<string, string> pageOfId, Dictionary<string, string> headingPage)
    {
        // #見出し へのリンク: ほかのページにある見出しなら /docs/<slug>#見出し にする（href は % エンコードされているので戻して照合する）
        html = InternalLinkPattern().Replace(html, m =>
        {
            var id = Uri.UnescapeDataString(m.Groups[1].Value);
            if (pageOfId.TryGetValue(id, out var target)) return target == slug ? m.Value : $"href=\"/docs/{target}#{m.Groups[1].Value}\"";
            if (headingPage.TryGetValue(id, out var titled)) return $"href=\"/docs/{titled}\"";
            return m.Value;
        });
        // 外部サイトへのリンクは新しいタブで開く
        html = ExternalLinkPattern().Replace(html, m => $"<a href=\"{m.Groups[1].Value}\" target=\"_blank\" rel=\"noopener noreferrer\"");
        // 表は、狭い画面で横にスクロールできるようにする
        return html.Replace("<table>", "<div class=\"table-wrap\"><table>").Replace("</table>", "</table></div>");
    }

    [GeneratedRegex("<h([1-6]) id=\"([^\"]+)\">(.*?)</h\\1>", RegexOptions.Singleline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("href=\"#([^\"]+)\"")]
    private static partial Regex InternalLinkPattern();

    [GeneratedRegex("<a href=\"(https?://[^\"]+)\"")]
    private static partial Regex ExternalLinkPattern();
}

/// <summary>Manual.md を見出しの木として扱う。コードブロックの中の # は見出しとして数えない。</summary>
internal sealed class ManualDocument
{
    private sealed record Heading(int Line, int Level, string Text);

    private readonly string[] _lines;
    private readonly List<Heading> _headings = new();
    private readonly bool[] _inFence;

    private ManualDocument(string[] lines)
    {
        _lines = lines;
        _inFence = new bool[lines.Length];
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                _inFence[i] = true;
                continue;
            }
            _inFence[i] = fenced;
            if (fenced) continue;
            var m = Regex.Match(lines[i], @"^(#{1,6})\s+(.+?)\s*$");
            if (m.Success) _headings.Add(new Heading(i, m.Groups[1].Value.Length, m.Groups[2].Value));
        }
    }

    public static ManualDocument Parse(string text) =>
        new(text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n'));

    /// <summary>
    /// 見出し（先頭が一致するもの）の部分を取り出す。下の見出しは、本文が h2 から始まるようにレベルをそろえる。
    /// includeHeading が false なら、見出し自身は出さない（ページの題名として別に出すため）。
    /// </summary>
    /// <summary>見出しの先頭の文字（pages.json に書いたもの）から、見出しの全文を返す。</summary>
    public string FullHeading(string headingPrefix)
    {
        var heading = _headings.Find(h => h.Text.StartsWith(headingPrefix, StringComparison.Ordinal));
        return heading?.Text ?? throw new InvalidOperationException($"Manual.md に見出しが見つかりません: {headingPrefix}");
    }

    public string Extract(string headingPrefix, bool own, bool includeHeading)
    {
        var index = _headings.FindIndex(h => h.Text.StartsWith(headingPrefix, StringComparison.Ordinal));
        if (index < 0) throw new InvalidOperationException($"Manual.md に見出しが見つかりません: {headingPrefix}");
        var head = _headings[index];
        var end = _lines.Length;
        for (var i = index + 1; i < _headings.Count; i++)
        {
            if (own || _headings[i].Level <= head.Level) { end = _headings[i].Line; break; }
        }

        // 見出し自身を出すなら h2、出さないなら、その下の見出し（head.Level + 1）が h2 になるようにそろえる
        var baseLevel = includeHeading ? head.Level : head.Level + 1;
        var shift = 2 - baseLevel;
        var output = new List<string>();
        for (var i = includeHeading ? head.Line : head.Line + 1; i < end; i++)
        {
            var line = _lines[i];
            if (!_inFence[i])
            {
                var m = Regex.Match(line, @"^(#{1,6})(\s+.+)$");
                if (m.Success) line = new string('#', Math.Clamp(m.Groups[1].Value.Length + shift, 2, 6)) + m.Groups[2].Value;
            }
            // 区切り線（---）はページの区切りなので出さない（コードブロックの中は残す）
            if (!_inFence[i] && line.Trim() == "---") continue;
            output.Add(line);
        }
        return string.Join("\n", output) + "\n";
    }
}
