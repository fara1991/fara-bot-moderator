using FaraBotModeratorSite.Content;
using Xunit;

namespace FaraBotModeratorSite.Content.Tests;

/// <summary>読みやすさのための印（長い箇条書き・2 列の表・FAQ の質問・囲み）が、HTML に付くこと。</summary>
public class ReadabilityTests
{
    [Fact]
    public void Long_list_items_get_long_list_and_short_ones_do_not()
    {
        var longItem = new string('あ', 120);
        Assert.Contains("<ul class=\"long-list\">", ContentBuilder.RenderHtml($"- {longItem}\n- 短い\n"));
        Assert.DoesNotContain("long-list", ContentBuilder.RenderHtml("- 短い\n- これも短い\n"));
    }

    [Fact]
    public void Two_column_tables_are_marked_for_stacking_and_wider_ones_are_not()
    {
        Assert.Contains("class=\"stack-sm\"", ContentBuilder.RenderHtml("| a | b |\n|---|---|\n| 1 | 2 |\n"));
        Assert.DoesNotContain("stack-sm", ContentBuilder.RenderHtml("| a | b | c |\n|---|---|---|\n| 1 | 2 | 3 |\n"));
    }

    [Fact]
    public void Tables_whose_header_has_a_link_are_not_stacked()
    {
        // 積んだ見出しの帯の中のリンクが、タップの大きさの計測（layout-check）に当たるため
        Assert.DoesNotContain("stack-sm", ContentBuilder.RenderHtml("| a | [b](https://example.com) |\n|---|---|\n| 1 | 2 |\n"));
    }

    [Fact]
    public void Faq_questions_get_a_class_and_other_h3_do_not()
    {
        var html = ContentBuilder.RenderHtml("### Q. どうしますか\n\n### ふつうの見出し\n");
        Assert.Contains("faq-q", html);
        Assert.Equal(1, html.Split("faq-q").Length - 1);
        Assert.Contains("faq-q", ContentBuilder.RenderHtml("## Q. 二段目でも付く\n"));
    }

    [Fact]
    public void Real_faq_page_has_seven_faq_questions()
    {
        // Extract が見出しの深さをそろえるので、実ページでは Q. の見出しは h2。RenderHtml への直接入力ではなく、実際に作ったページで確かめる
        var page = LoadContent().Pages.Single(p => p.Slug == "faq");
        // class が付いても、目次・検索の見出しの抽出が効くこと
        Assert.Equal(7, page.Toc.Count(t => t.Text.StartsWith("Q.", StringComparison.Ordinal)));
        var faq = page.Html;
        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(faq, "<h[23] id=\"[^\"]*\" class=\"faq-q\">").Count);
    }

    [Fact]
    public void Nested_children_do_not_make_a_list_long_and_lists_in_alerts_are_not_long_lists()
    {
        var longChild = new string('あ', 150);
        // 外側は long-list にならない（入れ子の子項目は数えない）
        Assert.StartsWith("<ul>", ContentBuilder.RenderHtml($"- 短い\n  - {longChild}\n"));
        Assert.DoesNotContain("long-list", ContentBuilder.RenderHtml($"> [!NOTE]\n> - {longChild}\n> - もう一つ\n"));
    }

    [Fact]
    public void Real_alerts_have_no_empty_paragraph_and_no_long_list_inside()
    {
        foreach (var page in LoadContent().Pages)
        {
            Assert.DoesNotContain("<p></p>", page.Html);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(page.Html, "<div class=\"markdown-alert.*?\n</div>", System.Text.RegularExpressions.RegexOptions.Singleline))
                Assert.DoesNotContain("long-list", m.Value);
        }
    }

    private static SiteContentData LoadContent() => ContentBuilder.LoadFrom(Path.GetDirectoryName(Path.GetDirectoryName(FindUp("Content/pages.json")))!);

    [Fact]
    public void Alerts_get_japanese_labels_after_the_build()
    {
        var content = ContentBuilder.LoadFrom(Path.GetDirectoryName(Path.GetDirectoryName(FindUp("Content/pages.json")))!);
        var pokemon = content.Pages.Single(p => p.Slug == "pokemon").Html;
        Assert.Contains("markdown-alert-title\">注意</p>", pokemon);
        Assert.Contains("markdown-alert-title\">補足</p>", pokemon);
        Assert.DoesNotContain("markdown-alert-title\">Warning", pokemon);
    }

    private static string FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative))) return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }
}
