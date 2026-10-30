using System.Text.Json;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FaraBotModeratorSite.Content;

/// <summary>
/// Content/pages.json の 1 ページ分の定義。Parent は親ページの slug。親を書いたページは、親のグループに入り、
/// サイドメニューでは親の下に入れ子で出る（Group は親のものを使うので、子には書かなくてよい）。
/// </summary>
public sealed class PageDefinition
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Group { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>題名の読み（ひらがな・長音の「ー」）。題名に漢字があるページだけに書く。サイト内検索で、読みからも探せるようにする。</summary>
    public string Reading { get; set; } = "";
    /// <summary>サイドメニューに出す名前。書かなければ題名。</summary>
    public string? MenuTitle { get; set; }
    /// <summary>true なら、本文の先頭に見出しへのリンクの目次を出す。</summary>
    public bool InlineToc { get; set; }
    public List<SectionDefinition> Sections { get; set; } = new();
}

/// <summary>ページに載せる Manual.md の見出し。Own が true なら、その見出しの直下の文章だけ（下の見出しは含めない）。</summary>
public sealed class SectionDefinition
{
    public string Heading { get; set; } = "";
    public bool Own { get; set; }
}

/// <summary>
/// 説明ページを作る。本文はリポジトリ直下の Manual.md（アプリに同梱する操作説明書）を見出しで切り分けたもので、
/// どの見出しをどのページにするかは Content/pages.json に書く。Manual.md を直せば、サイトにもそのまま反映される。
/// ビルド時に 1 回だけ実行し、結果（SiteContentData）を /data/site.json に書く。ブラウザは Markdown を読まない。
/// </summary>
public static partial class ContentBuilder
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAlertBlocks()
        .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
        .UseAutoLinks()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    /// <summary>changelog.json（[ { "date": "2026-10-22", "title": "...", "items": ["..."] } ]）を、新しい順の一覧にする。日付が無い・見出しが空・細かい内容に空の項目があれば FormatException。</summary>
    public static List<ChangelogEntry> ParseChangelog(string json)
    {
        List<ChangelogEntry> list;
        try
        {
            list = SiteJson.DeserializeChangelog(json);
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

    /// <summary>
    /// サイトの中身を、site/ フォルダ（Content/pages.json・Content/changelog.json がある場所）と Manual.md から作る。
    /// 見出しが見つからない・pages.json の誤り・changelog.json の誤り・行き先のないページ内リンクは、例外にする（ビルド・テストで気づけるように）。
    /// </summary>
    public static SiteContentData LoadFrom(string siteDir, string manualRelativePath = "../Manual.md")
    {
        var manualPath = Path.GetFullPath(Path.Combine(siteDir, manualRelativePath));
        var changelogPath = Path.Combine(siteDir, "Content", "changelog.json");
        return Build(
            File.ReadAllText(manualPath),
            File.ReadAllText(Path.Combine(siteDir, "Content", "pages.json")),
            File.Exists(changelogPath) ? File.ReadAllText(changelogPath) : null);
    }

    /// <summary>Manual.md の本文・pages.json・changelog.json から、サイトの中身を作る。</summary>
    public static SiteContentData Build(string manualText, string pagesJson, string? changelogJson)
    {
        var manual = ManualDocument.Parse(manualText);
        var definitions = JsonSerializer.Deserialize<List<PageDefinition>>(pagesJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();

        foreach (var d in definitions)
            if (!Regex.IsMatch(d.Slug, "^[a-z0-9-]+$")) throw new InvalidOperationException($"pages.json の slug は、小文字の英数字とハイフンだけにしてください: {d.Slug}");
        if (definitions.Select(d => d.Slug).Distinct().Count() != definitions.Count) throw new InvalidOperationException("pages.json に、同じ slug が重複しています");

        ValidateTree(definitions);
        var byDefinition = definitions.ToDictionary(d => d.Slug);
        var pages = new List<DocPage>();
        // ページの題名にしたため本文に出さない見出し（1 つ目の見出し）へのリンクは、そのページ自体へ向ける
        var headingPage = new Dictionary<string, string>();
        foreach (var d in definitions)
            if (d.Sections.Count > 0) headingPage[Slugify(manual.FullHeading(d.Sections[0].Heading))] = d.Slug;
        foreach (var d in definitions)
        {
            var markdown = string.Join("\n", d.Sections.Select((s, i) => manual.Extract(s.Heading, s.Own, includeHeading: i > 0)));
            var html = RenderHtml(markdown);
            var parent = d.Parent.Length > 0 ? d.Parent : null;
            pages.Add(new DocPage(d.Slug, d.Title, GroupOf(d, byDefinition), d.Description, html, ExtractToc(html), parent, d.Reading ?? "", string.IsNullOrWhiteSpace(d.MenuTitle) ? null : d.MenuTitle, d.InlineToc));
        }

        // 別のページにある見出しへの同じページ内リンク（#見出し）を、そのページへのリンクに直す
        var pageOfId = new Dictionary<string, string>();
        foreach (var p in pages)
            foreach (var t in p.Toc) pageOfId.TryAdd(t.Id, p.Slug);
        pages = pages.Select(p => p with { Html = Decorate(p.Html, p.Slug, pageOfId, headingPage) }).ToList();

        // 行き先のないページ内リンク（#見出し）が残っていたら、リンク切れなので止める。
        // ブラウザでは <base href="/"> のため、#見出し だけのリンクは、ホームへ移ってしまう
        foreach (var p in pages)
            if (DeadAnchorPattern().Match(p.Html) is { Success: true } dead)
                throw new InvalidOperationException($"{p.Url} に、行き先のないリンクがあります: {dead.Value}（Manual.md の見出しの名前と pages.json を確認してください）");

        return new SiteContentData
        {
            Pages = pages,
            // changelog.json の誤りは、ここで例外にして気づけるようにする（ビルド・テストで失敗する）
            Changelog = changelogJson is null ? new() : ParseChangelog(changelogJson),
            Search = BuildSearchIndex(pages),
        };
    }

    /// <summary>
    /// pages.json の親子関係を確かめる。親の slug が無い・自分が親になる・親をたどって循環する、
    /// 親ページにグループが無い、のときは例外（ビルド・テストで気づけるようにする）。
    /// </summary>
    public static void ValidateTree(IReadOnlyList<PageDefinition> definitions)
    {
        // JSON の null は、既定値（""）を上書きして null になる。ここで止めて、後の処理で NullReferenceException にしない
        foreach (var d in definitions)
        {
            if (d.Parent is null) throw new InvalidOperationException($"pages.json: {d.Slug} の parent に null は使えません（親が無いページは、parent の行を消してください）");
            if (d.Group is null) throw new InvalidOperationException($"pages.json: {d.Slug} の group に null は使えません（空にするか、行を消してください）");
            if (d.Reading is not null && !Regex.IsMatch(d.Reading, @"^[ぁ-ゖー]*$"))
                throw new InvalidOperationException($"pages.json: {d.Slug} の reading は、ひらがなと「ー」だけにしてください: {d.Reading}");
        }
        var bySlug = definitions.ToDictionary(d => d.Slug);
        foreach (var d in definitions)
        {
            var current = d;
            for (var steps = 0; current.Parent.Length > 0; steps++)
            {
                if (!bySlug.TryGetValue(current.Parent, out var parent))
                    throw new InvalidOperationException($"pages.json: {current.Slug} の parent が見つかりません: {current.Parent}");
                if (steps >= definitions.Count) throw new InvalidOperationException($"pages.json: {d.Slug} の親子関係が循環しています");
                current = parent;
            }
            if (string.IsNullOrWhiteSpace(current.Group))
                throw new InvalidOperationException($"pages.json: グループのないページがあります（親ページには group を書いてください）: {current.Slug}");
        }
    }

    /// <summary>子ページは親のグループに入るので、一番上の親の group を使う。</summary>
    private static string GroupOf(PageDefinition d, IReadOnlyDictionary<string, PageDefinition> byDefinition)
    {
        var current = d;
        while (current.Parent.Length > 0) current = byDefinition[current.Parent];
        return current.Group;
    }

    /// <summary>
    /// 検索インデックス。ページ自体（題名・説明）と、ページの中の見出し（h2・h3）ごとの節を 1 件ずつ。
    /// 一致した場所を「ページ › 見出し」で示せるように、節ごとに分けて持つ。
    /// </summary>
    public static List<SearchEntry> BuildSearchIndex(IReadOnlyList<DocPage> pages)
    {
        var entries = new List<SearchEntry>();
        foreach (var page in pages)
        {
            entries.Add(new SearchEntry("page", page.Title, page.Title, page.Group, page.Url, page.Description, page.Reading.Length > 0 ? page.Reading : null));
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
            // 同じページの見出しでも /docs/<slug>#見出し にする（<base href="/"> のため、#見出し だけだとホームへ移ってしまう）
            if (pageOfId.TryGetValue(id, out var target)) return $"href=\"/docs/{target}#{m.Groups[1].Value}\"";
            if (headingPage.TryGetValue(id, out var titled)) return $"href=\"/docs/{titled}\"";
            return m.Value;
        });
        // 外部サイトへのリンクは新しいタブで開く
        html = ExternalLinkPattern().Replace(html, m => $"<a href=\"{m.Groups[1].Value}\" target=\"_blank\" rel=\"noopener noreferrer\"");
        // 表は、狭い画面で横にスクロールできるようにする
        html = TableOpenPattern().Replace(html, m => "<div class=\"table-wrap\">" + m.Value);
        html = html.Replace("</table>", "</table></div>");
        // 補足・注意などの囲み（Markdig の alert）の見出しを日本語にする（色だけに頼らず、ラベルの文字で種類が分かるように）
        return AlertTitlePattern().Replace(html, m => $"<p class=\"markdown-alert-title\">{AlertLabels.GetValueOrDefault(m.Groups[1].Value.ToLowerInvariant(), m.Groups[1].Value)}</p>");
    }

    /// <summary>囲み（`> [!NOTE]` など）の見出しに出す日本語。</summary>
    private static readonly Dictionary<string, string> AlertLabels = new()
    {
        ["note"] = "補足",
        ["tip"] = "ヒント",
        ["important"] = "重要",
        ["warning"] = "注意",
        ["caution"] = "警告",
    };

    /// <summary>この長さ（文字数）以上の項目がある箇条書きは、項目どうしの区切りを強める（文章の壁にならないように）。</summary>
    private const int LongListItemLength = 100;

    /// <summary>
    /// Markdown を HTML にする。見た目のための印を、構造から付ける: 長い項目のある箇条書き（long-list）・
    /// 2 列の表（stack-sm。スマホでは「コマンド → 説明」を縦に積む）・FAQ の質問の見出し（faq-q）。文章は変えない。
    /// </summary>
    public static string RenderHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var block in document.Descendants().ToList())
        {
            switch (block)
            {
                // 囲み（補足・注意）の中の箇条書きには付けない（囲みの面の中に、さらに面を重ねないため）
                case ListBlock { IsOrdered: false } list when !InsideQuote(list) && list.OfType<ListItemBlock>().Any(i => OwnTextLength(i) >= LongListItemLength):
                    list.GetAttributes().AddClass("long-list");
                    break;
                // 見出し行にリンクがある表は積まない（積んだ見出しの帯の中のリンクは、タップの大きさの計測に当たらず、layout-check の違反になるため）
                case Table table when table.OfType<TableRow>().FirstOrDefault() is { } head && head.Count == 2 && !head.Descendants<LinkInline>().Any():
                    table.GetAttributes().AddClass("stack-sm");
                    break;
                // Manual.md の見出しは Extract で深さがそろえられ、FAQ の質問（### Q.）は実ページでは h2 になる。h2・h3 のどちらでも付ける
                case HeadingBlock { Level: 2 or 3 } heading when heading.Inline is not null && TextOf(heading).StartsWith("Q.", StringComparison.Ordinal):
                    heading.GetAttributes().AddClass("faq-q");
                    break;
            }
        }
        return document.ToHtml(Pipeline);
    }

    private static string TextOf(ContainerBlock container) =>
        string.Concat(container.Descendants<LeafBlock>().Where(l => l.Inline is not null).SelectMany(l => l.Inline!.Descendants<Inline>()).Select(InlineText));

    private static string TextOf(LeafBlock leaf) => leaf.Inline is null ? "" : string.Concat(leaf.Inline.Descendants<Inline>().Select(InlineText));

    /// <summary>項目の直下の段落の文字数（入れ子の子項目は数えない）。</summary>
    private static int OwnTextLength(ListItemBlock item) => item.OfType<LeafBlock>().Sum(l => TextOf(l).Length);

    private static bool InsideQuote(Block block)
    {
        for (var parent = block.Parent; parent is not null; parent = parent.Parent)
            if (parent is QuoteBlock) return true;
        return false;
    }

    private static string InlineText(Inline inline) => inline switch
    {
        LiteralInline l => l.Content.ToString(),
        CodeInline c => c.Content,
        _ => "",
    };

    [GeneratedRegex("<table(?: [^>]*)?>")]
    private static partial Regex TableOpenPattern();

    [GeneratedRegex("<p class=\"markdown-alert-title\">(?:<svg.*?</svg>)?\\s*(.*?)</p>(?:\\s*<p></p>)?", RegexOptions.Singleline)]
    private static partial Regex AlertTitlePattern();

    [GeneratedRegex("<h([1-6]) id=\"([^\"]+)\"[^>]*>(.*?)</h\\1>", RegexOptions.Singleline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("href=\"#([^\"]+)\"")]
    private static partial Regex InternalLinkPattern();

    [GeneratedRegex("<a href=\"(https?://[^\"]+)\"")]
    private static partial Regex ExternalLinkPattern();

    [GeneratedRegex("href=\"#[^\"]*\"")]
    private static partial Regex DeadAnchorPattern();
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

    /// <summary>見出しの先頭の文字（pages.json に書いたもの）から、見出しの全文を返す。</summary>
    public string FullHeading(string headingPrefix)
    {
        var heading = _headings.Find(h => h.Text.StartsWith(headingPrefix, StringComparison.Ordinal));
        return heading?.Text ?? throw new InvalidOperationException($"Manual.md に見出しが見つかりません: {headingPrefix}");
    }

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
