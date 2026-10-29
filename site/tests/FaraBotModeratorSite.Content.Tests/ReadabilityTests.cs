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
        // スマホで見出し行を隠す積み方にすると、隠した見出しの中のリンクにフォーカスが入ってしまうため
        Assert.DoesNotContain("stack-sm", ContentBuilder.RenderHtml("| a | [b](https://example.com) |\n|---|---|\n| 1 | 2 |\n"));
    }

    [Fact]
    public void Faq_questions_get_a_class_and_other_h3_do_not()
    {
        var html = ContentBuilder.RenderHtml("### Q. どうしますか\n\n### ふつうの見出し\n");
        Assert.Contains("faq-q", html);
        Assert.Equal(1, html.Split("faq-q").Length - 1);
    }

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
