using System.Text.RegularExpressions;
using Xunit;

namespace FaraBotModeratorSite.Tests;

/// <summary>スクロール量の線（.scroll-progress）。レイアウト・CSS・JS に、実装がそろっていることを確かめる（ブラウザでの見え方は、別に確認する）。</summary>
public class ScrollProgressTests
{
    private static string ReadProjectFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FaraBotModeratorSite.csproj")))
                return File.ReadAllText(Path.Combine(dir.FullName, relative));
        }
        throw new FileNotFoundException(relative);
    }

    /// <summary>セレクタの規則（{ ... }）の中身を返す。同じセレクタの規則が複数あれば、すべてつなぐ。</summary>
    private static string RuleBody(string css, string selector)
    {
        var bodies = Regex.Matches(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(bodies);
        return string.Join("\n", bodies);
    }

    [Fact]
    public void Main_layout_puts_the_bar_first_inside_the_page_and_hides_it_from_assistive_tech()
    {
        var razor = ReadProjectFile("Components/Layout/MainLayout.razor");
        var bar = razor.IndexOf("<div class=\"scroll-progress\" data-scroll-progress aria-hidden=\"true\"></div>", StringComparison.Ordinal);
        Assert.True(bar > 0, "scroll-progress の要素が MainLayout にない");
        Assert.True(bar > razor.IndexOf("<div class=\"page\">", StringComparison.Ordinal));
        Assert.True(bar < razor.IndexOf("<header class=\"app-header\">", StringComparison.Ordinal));
        // 中身は空（線は CSS で描く）。JS は data-scroll-progress で見つける
        Assert.Single(Regex.Matches(razor, "data-scroll-progress"));
    }

    [Fact]
    public void The_bar_is_a_fixed_2px_line_scaled_by_the_progress_variable_and_never_takes_clicks()
    {
        var css = ReadProjectFile("wwwroot/css/site.css");
        var rule = RuleBody(css, ".scroll-progress");
        Assert.Contains("position: fixed;", rule);
        Assert.Contains("top: 0;", rule);
        Assert.Contains("height: 2px;", rule);
        Assert.Contains("transform: scaleX(var(--progress, 0));", rule);
        Assert.Contains("transform-origin: left", rule);
        Assert.Contains("pointer-events: none;", rule);
        Assert.Contains("background: var(--scroll-grad);", rule);
        // 位置は、PC の固定ヘッダー（.app-header の z-index 70）より前。動きは付けない（スクロールに合わせて即座に伸びる）
        Assert.Contains("z-index: 80;", rule);
        Assert.DoesNotContain("transition", rule);
    }

    [Fact]
    public void The_gradient_is_defined_once_on_root_and_uses_the_accent_colors()
    {
        var css = ReadProjectFile("wwwroot/css/site.css");
        var root = RuleBody(css, ":root");
        Assert.Contains("--scroll-grad: linear-gradient(90deg, #a855f7 0%, #c084fc 55%, #818cf8 100%);", root);
        Assert.Single(Regex.Matches(css, "--scroll-grad:"));
    }

    [Fact]
    public void Script_updates_progress_from_the_scroll_position_without_inline_code()
    {
        var js = ReadProjectFile("wwwroot/js/site.js");
        Assert.Contains("[data-scroll-progress]", js);
        Assert.Contains("bar.style.setProperty(\"--progress\"", js);
        Assert.Contains("window.addEventListener(\"scroll\", schedule, { passive: true });", js);
        Assert.Contains("window.addEventListener(\"resize\", schedule);", js);
        Assert.Contains("window.requestAnimationFrame(update);", js);
        // 0 から 1 に収める。スクロールできない（max が 0 以下）ときは 0
        Assert.Contains("max > 0 ? top / max : 0", js);
        Assert.Contains("Math.min(1, Math.max(0, value))", js);
        // CSP（script-src 'self'）と、画面に文字を差し込まないこと
        Assert.DoesNotContain("innerHTML", js);
        Assert.DoesNotContain("eval(", js);
    }
}
