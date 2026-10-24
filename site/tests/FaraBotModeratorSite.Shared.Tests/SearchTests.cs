using FaraBotModeratorSite.Search;
using Xunit;

namespace FaraBotModeratorSite.Shared.Tests;

/// <summary>
/// サイト内検索（Search/）のテスト: ローマ字・かなの読み・長音・nn・2 文字の一致・本文検索の順位。
/// 以前の wwwroot/js/search.js と tests/js/search.test.mjs（#114）の事例を、そのまま移したもの。
/// </summary>
public class SearchTests
{
    [Fact]
    public void LooseKana_drops_the_long_vowel_mark_and_unifies_ou_and_oo_but_not_ei()
    {
        Assert.Equal("げむ", SearchText.LooseKana("げーむ"));
        Assert.Equal("ほき", SearchText.LooseKana("ほうき"));
        Assert.Equal(SearchText.LooseKana("ひょき"), SearchText.LooseKana("ひょうき"));
        Assert.Equal("おきい", SearchText.LooseKana("おおきい"));
        Assert.Equal("ていき", SearchText.LooseKana("ていき"));
        Assert.Equal("せいじん", SearchText.LooseKana("せいじん"));
        Assert.Equal("うお", SearchText.LooseKana("うお"));
    }

    [Theory]
    [InlineData("kennri", "けんり")]   // nn は「ん」1 つ
    [InlineData("kann", "かん")]
    [InlineData("kenn", "けん")]
    [InlineData("kanni", "かんに")]   // 後ろが母音・y なら、2 つ目の n は次の音
    [InlineData("konnichiha", "こんにちは")]
    [InlineData("konnnichiha", "こんにちは")]
    [InlineData("konnyaku", "こんにゃく")]
    [InlineData("komando", "こまんど")]
    [InlineData("kenri", "けんり")]
    [InlineData("gemu-", "げむー")]
    [InlineData("kitte", "きって")]
    [InlineData("ka'n", "かん")]
    public void Romaji_converts_to_hiragana(string romaji, string expected) =>
        Assert.Equal(expected, Romaji.ToHiragana(romaji));

    [Theory]
    [InlineData("xyz")]
    [InlineData("")]
    [InlineData("kome1")]
    [InlineData("こまんど")]
    public void Romaji_returns_null_for_what_cannot_be_converted(string text) =>
        Assert.Null(Romaji.ToHiragana(text));

    [Fact]
    public void Alternatives_add_the_hiragana_form_for_romaji_of_three_or_more_letters()
    {
        Assert.Equal(new[] { "kennri", "けんり" }, Romaji.Alternatives("kennri"));
        Assert.Equal(new[] { "ab" }, Romaji.Alternatives("ab"));
    }

    [Fact]
    public void ReadingScore_is_30_for_exact_15_or_5_for_prefix_and_10_for_the_middle()
    {
        Assert.Equal(30, SearchText.ReadingScore("こまんどじっこう", "こまんどじっこう"));
        Assert.Equal(15, SearchText.ReadingScore("こまんどじっこう", "こまんど"));
        Assert.Equal(5, SearchText.ReadingScore("あいてちーむとりれき", "あい"));
        Assert.Equal(0, SearchText.ReadingScore("ちあいてむ", "あい"));          // 2 文字は途中の一致を見ない
        Assert.Equal(0, SearchText.ReadingScore("ちあいてむ", "てむ"));
        Assert.Equal(10, SearchText.ReadingScore("こまんどじっこう", "じっこ"));   // 3 文字以上は途中の一致も見る
        Assert.Equal(5, SearchText.ReadingScore("げーむせってい", "げむ"));        // 長音を除いて比べる（gemu）
        Assert.Equal(0, SearchText.ReadingScore("", "あい"));
        Assert.Equal(0, SearchText.ReadingScore("こまんど", "あ"));                // 1 文字は読みでは見ない
    }

    private static SearchIndex Index(params SearchEntry[] entries) => new(entries);

    private static SearchEntry Entry(string type = "section", string title = "", string page = "", string group = "", string url = "/x", string text = "", string? reading = null) =>
        new(type, title, page, group, url, text, reading);

    private static string[] Urls(IReadOnlyList<SearchHit> hits) => hits.Select(h => h.Entry.Url).ToArray();

    [Fact]
    public void A_page_found_only_by_its_reading_shows_up_in_romaji_and_in_kana()
    {
        var idx = Index(
            Entry("page", "コマンド実行", "コマンド実行", "コマンド実行", "/docs/commands", "", "こまんどじっこう"),
            Entry("page", "よくある質問（FAQ）", "よくある質問（FAQ）", "困ったときは", "/docs/faq", "", "よくあるしつもん"));
        Assert.Equal("/docs/commands", idx.Search("こまんどじっこう")[0].Entry.Url);
        Assert.Equal("/docs/commands", idx.Search("komandojikko")[0].Entry.Url);
        Assert.Equal("/docs/faq", idx.Search("yokuarushitsumon")[0].Entry.Url);
        Assert.Empty(idx.Search("kenri"));   // 当たらない語は出ない
        Assert.True(idx.Search("komandojikko")[0].ByReading);
    }

    [Fact]
    public void Two_kana_only_match_from_the_start_of_a_reading()
    {
        var idx = Index(
            Entry("page", "相手チームと履歴", "相手チームと履歴", url: "/docs/rival", reading: "あいてちーむとりれき"),
            Entry("page", "途中に含むページ", "途中に含むページ", url: "/docs/mid", reading: "ちあいてむ"));
        Assert.Equal(new[] { "/docs/rival" }, Urls(idx.Search("あい")));
    }

    [Fact]
    public void Title_match_ranks_above_page_name_above_body()
    {
        var idx = Index(
            Entry(title: "説明", page: "A", url: "/docs/a#x", text: "dmg の使い方"),
            Entry(title: "dmg の計算", page: "B", url: "/docs/b#y"),
            Entry("page", "ダメージ", "ダメージ", url: "/docs/c", text: "dmg", reading: "だめーじ"));
        Assert.Equal(new[] { "/docs/b#y", "/docs/c", "/docs/a#x" }, Urls(idx.Search("dmg")));
    }

    [Fact]
    public void Every_word_must_match_and_katakana_matches_hiragana_and_full_width_matches_half_width()
    {
        var idx = Index(
            Entry(title: "アドオンの追加", page: "P", url: "/a", text: "ＤＭＧ を使う"),
            Entry(title: "別のもの", page: "P", url: "/b", text: "dmg"));
        Assert.Equal(new[] { "/a" }, Urls(idx.Search("あどおん dmg")));
        Assert.Equal(new[] { "/a" }, Urls(idx.Search("アドオン")));
        Assert.Equal(2, idx.Search("ｄｍｇ").Count);
        Assert.Empty(idx.Search("あどおん zzz"));
        Assert.Empty(idx.Search("   "));
    }

    [Fact]
    public void Half_width_katakana_with_voiced_marks_are_folded()
    {
        Assert.Equal("こまんど", SearchText.Norm("ｺﾏﾝﾄﾞ"));
        Assert.Equal("ぱ", SearchText.Norm("ﾊﾟ"));
        Assert.Equal("ゔ", SearchText.Norm("ｳﾞ"));
        Assert.Equal("a〜b", SearchText.Norm("A~B"));
        Assert.Equal("a〜b", SearchText.Norm("Ａ～Ｂ"));
        Assert.Equal("a b", SearchText.Norm("a　b"));
        Assert.Equal("(1)", SearchText.Norm("（１）"));
        Assert.Equal("...", SearchText.Norm("…"));
    }

    [Fact]
    public void Highlight_marks_the_matches_even_when_the_width_differs_and_keeps_positions_after_an_ellipsis()
    {
        var parts = SearchText.Highlight("あ…ＤＭＧです", new[] { "dmg" }).ToList();
        Assert.Equal(new[] { ("あ…", false), ("ＤＭＧ", true), ("です", false) }, parts);
        Assert.Equal("ab", string.Concat(SearchText.Highlight("ab", Array.Empty<string>()).Select(p => p.Text)));
        // 長い語を先に試す
        var longest = SearchText.Highlight("abcd", new[] { "ab", "abc" }).ToList();
        Assert.Equal(new[] { ("abc", true), ("d", false) }, longest);
    }

    [Fact]
    public void Snippet_cuts_around_the_first_match_with_ellipses()
    {
        var text = string.Concat(Enumerable.Repeat("あ", 50)) + "目印" + string.Concat(Enumerable.Repeat("い", 50));
        var snippet = SearchText.Snippet(text, new[] { "目印" }, 30);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("目印", snippet);
        Assert.Equal("あいう", SearchText.Snippet("あいう", new[] { "ない" }, 30));
        Assert.Equal("あいう…", SearchText.Snippet("あいうえお", new[] { "ない" }, 3));
    }

    [Fact]
    public void Terms_flatten_the_alternatives_without_duplicates()
    {
        Assert.Equal(new[] { "komando", "こまんど" }, SearchText.Terms("komando"));
        Assert.Equal(new[] { "dmg", "ab" }, SearchText.Terms("DMG ab dmg"));
        Assert.Empty(SearchText.Terms(null));
    }
}
