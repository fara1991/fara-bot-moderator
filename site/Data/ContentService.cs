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

    public ContentService(IWebHostEnvironment env, IOptions<SiteOptions> options)
    {
        _env = env;
        _options = options.Value;
        Reload();
    }

    public IReadOnlyList<DocPage> Pages => _pages;
    public IReadOnlyList<PageGroup> Groups => _groups;

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

        var pages = new List<DocPage>();
        // ページの題名にしたため本文に出さない見出し（1 つ目の見出し）へのリンクは、そのページ自体へ向ける
        var headingPage = new Dictionary<string, string>();
        foreach (var d in definitions)
            if (d.Sections.Count > 0) headingPage[Slugify(d.Sections[0].Heading)] = d.Slug;
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

        _pages = pages;
        _bySlug = pages.ToDictionary(p => p.Slug);
        _groups = pages.GroupBy(p => p.Group).Select(g => new PageGroup(g.Key, g.ToList())).ToList();
    }

    private static List<TocItem> ExtractToc(string html) =>
        HeadingPattern().Matches(html)
            .Select(m => new TocItem(m.Groups[2].Value, Regex.Replace(m.Groups[3].Value, "<[^>]+>", ""), int.Parse(m.Groups[1].Value)))
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
