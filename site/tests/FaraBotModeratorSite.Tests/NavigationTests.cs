using Xunit;

namespace FaraBotModeratorSite.Tests;

/// <summary>サイト内の移動（wwwroot/js/router.js）。Blazor の拡張ナビゲーションは使わず、静的な HTML のまま、本文だけを差し替える。その配線を確かめる。</summary>
public class NavigationTests
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

    [Fact]
    public void App_loads_the_router_as_a_deferred_script_and_not_blazor_web_js()
    {
        var app = ReadProjectFile("Components/App.razor");
        Assert.Contains("<script src=\"/js/router.js\" defer></script>", app);
        // 静的な書き出しでは _framework が出ないので、Blazor の拡張ナビゲーションは使わない
        Assert.DoesNotContain("blazor.web.js", app);
        Assert.DoesNotContain("_framework", app);
    }

    [Fact]
    public void Router_swaps_only_the_main_content_and_keeps_the_header_and_sidebar()
    {
        var js = ReadProjectFile("wwwroot/js/router.js");
        Assert.Contains("var MAIN_SELECTOR = \"main#main-content\";", js);
        // 差し替えは本文だけ。ヘッダーとサイドバーは触らない（それぞれの初期化が二重にならない）
        Assert.DoesNotContain("app-header", js);
        Assert.DoesNotContain("document.body", js);
        Assert.Contains("main.replaceChildren(frag);", js);
    }

    [Fact]
    public void Router_keeps_the_report_page_out_of_the_swap_because_its_csp_differs()
    {
        var js = ReadProjectFile("wwwroot/js/router.js");
        Assert.Contains("return path === \"/report\" || path === \"/report/\";", js);
        Assert.Contains("!isNavigablePath(here.pathname)", js);
    }

    [Fact]
    public void Router_syncs_title_meta_and_the_sidebar_current_link_and_announces_the_navigation()
    {
        var js = ReadProjectFile("wwwroot/js/router.js");
        Assert.Contains("'meta[name=\"description\"]'", js);
        Assert.Contains("'meta[property^=\"og:\"]'", js);
        Assert.Contains("'link[rel=\"canonical\"]'", js);
        Assert.Contains("aria-current", js);
        Assert.Contains("document.dispatchEvent(new Event(\"fara:navigated\"));", js);
    }

    [Fact]
    public void Scripts_that_keep_running_listen_for_the_navigation_instead_of_being_registered_again()
    {
        var site = ReadProjectFile("wwwroot/js/site.js");
        Assert.Contains("document.addEventListener(\"fara:navigated\", schedule);", site);
        var search = ReadProjectFile("wwwroot/js/search.js");
        Assert.Contains("if (window.faraNavigate) window.faraNavigate(href);", search);
    }

    [Fact]
    public void The_router_is_never_inlined_and_only_uses_same_origin_files()
    {
        var css = ReadProjectFile("wwwroot/js/router.js");
        // CSP（script-src 'self'）に合わせ、インラインの script・eval・外部の取得を使わない
        Assert.DoesNotContain("eval(", css);
        Assert.DoesNotContain("https://", css);
        Assert.DoesNotContain("innerHTML", css);
    }

    [Fact]
    public void The_CI_runs_the_router_tests()
    {
        var workflow = ReadProjectFile("../.github/workflows/site.yml");
        Assert.Contains("node --test worker/report.test.mjs worker/index.test.mjs worker/maintenance.test.mjs tests/js/router.test.mjs", workflow);
    }
}
