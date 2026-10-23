namespace FaraBotModeratorSite.Search;

/// <summary>検索の結果 1 件。</summary>
/// <param name="Entry">索引の 1 件</param>
/// <param name="Score">点数（高いほど上）</param>
/// <param name="ByReading">題名には一致せず、読み（ひらがな）だけで当たったページか（どの語で当たったかを示すため）</param>
public sealed record SearchHit(SearchEntry Entry, double Score, bool ByReading);

/// <summary>
/// サイト内検索の索引。日本語は単語区切りがないので、形態素解析はせず、スペースで区切った語が「すべて含まれる」ものを一致とする。
/// 全角・半角の違いはそろえ、カタカナはひらがなにそろえる（あどおん・アドオンのどちらでも探せる）。
/// ローマ字（adoon）も、ひらがな（あどおん）にした形で探す。
/// ページの題名に漢字があるものは、Content/pages.json の reading（ひらがなの読み）でも探す（例: komando・こまんどじっこう → コマンド実行）。
/// 点数は以前の search.js と同じ: 見出し > ページ名 > 本文の順。
/// </summary>
public sealed class SearchIndex
{
    private sealed record Prepared(SearchEntry Entry, string Title, string Page, string Text, string Reading);

    private readonly List<Prepared> _entries;

    public SearchIndex(IEnumerable<SearchEntry> entries)
    {
        _entries = entries.Select(e => new Prepared(e,
            SearchText.Norm(e.Title), SearchText.Norm(e.Page), SearchText.Norm(e.Text), SearchText.LooseKana(SearchText.Norm(e.Reading)))).ToList();
    }

    public int Count => _entries.Count;

    /// <summary>1 つの語の点数（含まれなければ 0）。</summary>
    private static int ScoreOf(Prepared e, string t)
    {
        if (e.Title == t) return 30;
        if (e.Title.StartsWith(t, StringComparison.Ordinal)) return 15;
        if (e.Title.Contains(t, StringComparison.Ordinal)) return 10;
        var reading = SearchText.ReadingScore(e.Reading, t);
        if (reading != 0) return reading;
        if (e.Page.Contains(t, StringComparison.Ordinal)) return 4;
        if (e.Text.Contains(t, StringComparison.Ordinal)) return 1;
        return 0;
    }

    /// <summary>すべての語を含むものを、見出し > ページ名 > 本文の順に点数を付けて並べる（同点は索引の順）。</summary>
    public IReadOnlyList<SearchHit> Search(string? query)
    {
        var groups = SearchText.TermGroups(query);
        var hits = new List<SearchHit>();
        if (groups.Count == 0) return hits;
        foreach (var e in _entries)
        {
            double score = 0;
            foreach (var group in groups)
            {
                var s = 0;
                foreach (var t in group) s = Math.Max(s, ScoreOf(e, t));
                if (s == 0) { score = 0; break; }
                score += s;
            }
            if (score <= 0) continue;
            var entry = e.Entry;
            // 題名に一致しないで、読みだけで当たったページ
            var byReading = entry.Type == "page" && e.Reading.Length > 0
                && !groups.SelectMany(g => g).Any(t => e.Title.Contains(t, StringComparison.Ordinal))
                && groups.All(g => g.Any(t => SearchText.ReadingScore(e.Reading, t) > 0));
            hits.Add(new SearchHit(entry, score + (entry.Type == "page" ? 0.5 : 0), byReading));
        }
        // 安定ソート（OrderByDescending は同点で元の順を保つ）
        return hits.OrderByDescending(h => h.Score).ToList();
    }
}
