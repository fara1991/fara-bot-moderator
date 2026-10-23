using System.Text;

namespace FaraBotModeratorSite.Search;

/// <summary>
/// 検索の文字の扱い（そろえ方・読み・強調・抜き出し）。以前の wwwroot/js/search.js と同じ規則。
/// ICU を載せない WebAssembly でも同じ結果になるよう、string.Normalize（NFKC）には頼らず、全角英数記号・半角カナ・… を自分でそろえる。
/// </summary>
public static class SearchText
{
    /// <summary>長音「ー」を除いたあと、「う」「お」を前の音とまとめる対象（おう・おお の書き方をそろえる）。</summary>
    private const string LongO = "おこそとのほもよろごぞどぼぽょぉ";

    private const string HalfKana = "ｦｧｨｩｪｫｬｭｮｯｰｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ";
    private const string FullKana = "をぁぃぅぇぉゃゅょっーあいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわん";

    /// <summary>全角・半角、大文字・小文字をそろえる。全角の ～（Windows の日本語入力）は ~ になるので、データの 〜 にそろえる。カタカナはひらがなにする。</summary>
    public static string Norm(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var consumed = 0;
            var folded = FoldChar(s, i, ref consumed);
            sb.Append(folded);
            i += consumed;
        }
        return sb.ToString();
    }

    /// <summary>
    /// 1 文字（半角カナの濁点・半濁点は、前の文字と合わせて 1 文字）をそろえる。
    /// consumed には「この文字のあとに続けて読んだ文字数」を入れる。
    /// </summary>
    private static string FoldChar(string s, int i, ref int consumed)
    {
        var c = s[i];
        if (c == '　') return " ";
        if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);          // 全角の英数記号 → 半角（NFKC と同じ）
        else if (c == '…') return "...";                              // NFKC は … を ... にする
        else if (c >= 'ｦ' && c <= 'ﾝ')                               // 半角カナ → ひらがな（濁点・半濁点は、前の字と合わせる）
        {
            var kana = FullKana[HalfKana.IndexOf(c)];
            if (i + 1 < s.Length && (s[i + 1] == 'ﾞ' || s[i + 1] == 'ﾟ'))
            {
                var composed = Compose(kana, s[i + 1] == 'ﾞ');
                if (composed is { } k) { consumed = 1; return k.ToString(); }
            }
            return kana.ToString();
        }
        if (c == '~') return "〜";
        if (c >= 'ァ' && c <= 'ヶ') return ((char)(c - 0x60)).ToString();   // カタカナ → ひらがな
        if (c >= 'A' && c <= 'Z') return ((char)(c + 32)).ToString();
        return char.ToLowerInvariant(c).ToString();
    }

    private static char? Compose(char kana, bool voiced)
    {
        const string plain = "かきくけこさしすせそたちつてとはひふへほう";
        const string dakuten = "がぎぐげござじずぜぞだぢづでどばびぶべぼゔ";
        const string handakuten = "ぱぴぷぺぽ";
        if (voiced) { var i = plain.IndexOf(kana); return i >= 0 ? dakuten[i] : null; }
        var j = "はひふへほ".IndexOf(kana);
        return j >= 0 ? handakuten[j] : null;
    }

    /// <summary>1 文字ずつそろえた文字列と、そろえたあとの位置から元の位置への対応（… が ... のように文字数が変わっても、位置がずれないように）。</summary>
    public static (string Normed, int[] Map) FoldMap(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var consumed = 0;
            var folded = FoldChar(text, i, ref consumed);
            foreach (var _ in folded) map.Add(i);
            sb.Append(folded);
            i += consumed;
        }
        return (sb.ToString(), map.ToArray());
    }

    /// <summary>検索語ごとの、探す形の一覧（[[入力どおり, ひらがな], ...]）。語ごとに、どれか 1 つが含まれればよい。</summary>
    public static List<IReadOnlyList<string>> TermGroups(string? query) =>
        SplitWords(Norm(query)).Select(Romaji.Alternatives).ToList();

    /// <summary>強調・抜き出し用に、すべての形を 1 列にしたもの。</summary>
    public static string[] Terms(string? query)
    {
        var all = new List<string>();
        foreach (var group in TermGroups(query))
            foreach (var t in group)
                if (!all.Contains(t)) all.Add(t);
        return all.ToArray();
    }

    private static IEnumerable<string> SplitWords(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// 読みの比べ方（pokemon の LooseKana と同じ規則）: 長音の「ー」を除き、おう・おお の書き方を 1 つにそろえる（ほうき・ほき、おおきい・おきい）。
    /// え段＋い（ていき・けいさん）は、長音と区別できないので、そろえない。
    /// </summary>
    public static string LooseKana(string? s)
    {
        var t = (s ?? "").Replace("ー", "");
        var sb = new StringBuilder(t.Length);
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            var prev = i > 0 ? t[i - 1] : '\0';
            if (c == 'う' && prev != '\0' && LongO.Contains(prev)) continue;
            if (c == 'お' && prev == 'お') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 読みでの点数。完全一致 30・前方一致は 3 文字以上で 15（2 文字だけなら 5）・途中の一致は 3 文字以上だけ 10。
    /// 2 文字のかなは、読みの途中には当てない（「あい」が途中に偶然当たらないように）。
    /// </summary>
    public static int ReadingScore(string? reading, string term)
    {
        if (string.IsNullOrEmpty(reading)) return 0;
        var key = LooseKana(term);
        var r = LooseKana(reading);
        if (key.Length < 2) return 0;
        if (r == key) return 30;
        if (r.StartsWith(key, StringComparison.Ordinal)) return key.Length >= 3 ? 15 : 5;
        return key.Length >= 3 && r.IndexOf(key, StringComparison.Ordinal) > 0 ? 10 : 0;
    }

    /// <summary>一致した部分を強調するための分割（text, 一致か）。全角・半角が違っても一致するよう、1 文字ずつそろえた文字列で位置を探す。</summary>
    public static IEnumerable<(string Text, bool Match)> Highlight(string? text, IReadOnlyList<string> terms)
    {
        text ??= "";
        if (terms.Count == 0)
        {
            if (text.Length > 0) yield return (text, false);
            yield break;
        }
        var (normed, map) = FoldMap(text);
        var ordered = terms.Where(t => t.Length > 0).OrderByDescending(t => t.Length).ToArray();
        var last = 0;
        var pos = 0;
        while (pos < normed.Length && ordered.Length > 0)
        {
            // 正規表現の「|」と同じ: 同じ位置では、長い語を先に試す。いちばん左の一致を取る
            var best = -1;
            var bestLen = 0;
            for (var p = pos; p < normed.Length && best < 0; p++)
            {
                foreach (var t in ordered)
                {
                    if (string.CompareOrdinal(normed, p, t, 0, t.Length) == 0)
                    {
                        best = p;
                        bestLen = t.Length;
                        break;
                    }
                }
            }
            if (best < 0) break;
            var s = map[best];
            var e = map[best + bestLen - 1] + 1;
            pos = best + bestLen;
            if (s < last) continue;
            if (s > last) yield return (text[last..s], false);
            yield return (text[s..e], true);
            last = e;
        }
        if (last < text.Length) yield return (text[last..], false);
    }

    /// <summary>本文から、最初に一致した語のまわりを切り出す（一致が無ければ、先頭から）。</summary>
    public static string Snippet(string? text, IReadOnlyList<string> terms, int width)
    {
        text ??= "";
        var (normed, map) = FoldMap(text);
        var pos = -1;
        for (var i = 0; i < terms.Count && pos < 0; i++)
            pos = normed.IndexOf(terms[i], StringComparison.Ordinal);
        if (pos < 0) return text[..Math.Min(width, text.Length)] + (text.Length > width ? "…" : "");
        pos = map[pos];   // 元の文字列の位置に戻す
        var start = Math.Max(0, pos - width / 3);
        var end = Math.Min(text.Length, start + width);
        return (start > 0 ? "…" : "") + text[start..end] + (end < text.Length ? "…" : "");
    }
}
