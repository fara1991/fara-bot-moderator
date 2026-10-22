using System.Security.Cryptography;

using Xunit;

namespace FaraBotModeratorSite.Tests;

/// <summary>ページ移動（wwwroot/js/nav.js。avatar と共有）の配線。静的に書き出した HTML を、JS でページ単位に差し替える仕組みの、つながりを確かめる。
/// 画面の動きは、Edge（headless）で別に確かめる。判定の関数は node のテスト（tests/js/nav.test.mjs・site-nav.test.mjs）で確かめる。</summary>
public class NavigationTests
{
    /// <summary>avatar の develop の wwwroot/js/nav.js の sha256（共有ファイル。直すときは avatar 側と同じ内容にすること）</summary>
    private const string SharedNavSha256 = "d7eb60bc39ef9929c7133e1d4cb1993e8fc23ba909c903c0a6a06ad974faf93e";

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
    public void App_loads_the_config_then_nav_then_the_site_hooks_as_deferred_scripts()
    {
        var app = ReadProjectFile("Components/App.razor");
        var config = app.IndexOf("<script src=\"/js/nav-config.js\" defer></script>", StringComparison.Ordinal);
        var nav = app.IndexOf("<script src=\"/js/nav.js?v=@NavVersion\" defer></script>", StringComparison.Ordinal);
        var hooks = app.IndexOf("<script src=\"/js/site-nav.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(config >= 0 && nav >= 0 && hooks >= 0, "App.razor に nav-config.js・nav.js（キャッシュ更新のバージョン付き）・site-nav.js の読み込みがない");
        // 設定 → nav.js → 登録（site-nav.js）の順。defer のスクリプトは書いた順に動く
        Assert.True(config < nav && nav < hooks);
        Assert.Contains("private static readonly string NavVersion = DateTime.UtcNow.ToString(\"yyyyMMddHHmmss\");", app);
    }

    [Fact]
    public void Shared_nav_js_is_identical_to_the_avatar_copy()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(ProjectPath())!, "wwwroot", "js", "nav.js"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(SharedNavSha256, hash);
    }

    [Fact]
    public void Navigation_does_not_use_blazor_web_js_or_the_old_router()
    {
        var app = ReadProjectFile("Components/App.razor");
        Assert.DoesNotContain("blazor.web.js", app);
        Assert.DoesNotContain("_framework", app);
        Assert.DoesNotContain("router.js", app);
        var search = ReadProjectFile("wwwroot/js/search.js");
        Assert.DoesNotContain("faraNavigate", search);
        Assert.Contains("window.faraNav.navigate(href)", search);
    }

    [Fact]
    public void Layout_keeps_the_header_and_sidebar_controls_outside_the_swapped_parts()
    {
        // 移動では #main-content（本文）と、左のメニューの中のリンク（.nav-scrollable > nav）だけを差し替える。ヘッダー・開閉のボタン・検索欄は残る
        var layout = ReadProjectFile("Components/Layout/MainLayout.razor");
        Assert.Contains("<main id=\"main-content\" tabindex=\"-1\">", layout);
        Assert.Contains("<header class=\"app-header\">", layout);
        Assert.Contains("class=\"skip-link\"", layout);
        var menu = ReadProjectFile("Components/Layout/NavMenu.razor");
        Assert.Contains("<div class=\"nav-scrollable\">", menu);
        Assert.Contains("<nav class=\"flex-column\"", menu);
        var config = ReadProjectFile("wwwroot/js/nav-config.js");
        Assert.Contains("swap: [\".nav-scrollable > nav\"],", config);
        Assert.Contains("main: \"#main-content\",", config);
        Assert.Contains("excludePrefixes: [\"/api/\", \"/report\"],", config);
    }

    [Fact]
    public void Site_hooks_close_the_mobile_menu_and_tell_the_scroll_line_about_the_move()
    {
        var hooks = ReadProjectFile("wwwroot/js/site-nav.js");
        Assert.Contains("window.faraNav.register(", hooks);
        Assert.Contains("getElementById(\"nav-toggle\")", hooks);
        Assert.Contains("new Event(\"fara:navigated\")", hooks);
        var site = ReadProjectFile("wwwroot/js/site.js");
        Assert.Contains("document.addEventListener(\"fara:navigated\", schedule);", site);
    }

    [Fact]
    public void Page_scripts_register_with_nav_so_they_run_again_after_a_move()
    {
        // report.js は、nav.js がなくても動く形（faraNav があれば register、なければその場で呼ぶ）
        var report = ReadProjectFile("wwwroot/js/report.js");
        Assert.Contains("window.faraNav ? window.faraNav.register(init) : init();", report);
    }

    [Fact]
    public void The_CI_runs_the_navigation_node_tests()
    {
        var workflow = ReadProjectFile("../.github/workflows/site.yml");
        Assert.Contains("tests/js/nav.test.mjs tests/js/site-nav.test.mjs", workflow);
    }

    private static string ProjectPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "FaraBotModeratorSite.csproj");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("FaraBotModeratorSite.csproj");
    }
}
