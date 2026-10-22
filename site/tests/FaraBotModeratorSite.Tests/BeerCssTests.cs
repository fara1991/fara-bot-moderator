using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace FaraBotModeratorSite.Tests;

/// <summary>BeerCSS の取り込み（css/vendor）と、CSP（style-src 'self'・script-src 'self'）に合う書き方かどうかを確かめる。</summary>
public class BeerCssTests
{
    private const string VendorMd5 = "1e1565b430d311c00c828f8d0b494142";

    private static string ProjectDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FaraBotModeratorSite.csproj"))) return dir.FullName;
        }
        throw new FileNotFoundException("FaraBotModeratorSite.csproj");
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(Path.Combine(ProjectDir(), "Components"), "*.razor", SearchOption.AllDirectories);

    [Fact]
    public void Razor_files_have_no_style_attributes_style_blocks_or_inline_scripts()
    {
        foreach (var file in RazorFiles())
        {
            var text = File.ReadAllText(file);
            // CSP の style-src 'self' で止まる書き方（style 属性・<style>）と、script-src 'self' で止まる書き方（src のない <script>）を使わない
            Assert.False(Regex.IsMatch(text, @"\sstyle\s*="), $"{Path.GetFileName(file)} に style 属性がある");
            Assert.DoesNotContain("<style", text, StringComparison.OrdinalIgnoreCase);
            Assert.False(Regex.IsMatch(text, @"<script(?![^>]*\ssrc\s*=)[^>]*>", RegexOptions.IgnoreCase), $"{Path.GetFileName(file)} に src のない <script> がある");
        }
    }

    [Fact]
    public void Vendored_beercss_is_the_patched_5_0_3_css_without_external_urls_or_fonts()
    {
        var path = Path.Combine(ProjectDir(), "wwwroot", "css", "vendor", "beer.min.css");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(VendorMd5, Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant());
        var css = System.Text.Encoding.UTF8.GetString(bytes);
        // パッチの内容: @font-face を取り除いてある（jsdelivr の絶対 URL は font-src 'self' の CSP に違反する）
        Assert.DoesNotContain("@font-face", css);
        Assert.DoesNotContain("https://", css);
        Assert.DoesNotContain("http://", css);
    }

    [Fact]
    public void Vendor_readme_is_not_published_and_the_css_is_loaded_before_the_site_css()
    {
        var ignore = File.ReadAllText(Path.Combine(ProjectDir(), "wwwroot", ".assetsignore"));
        Assert.Contains("css/vendor/README.md", ignore);
        var app = File.ReadAllText(Path.Combine(ProjectDir(), "Components", "App.razor"));
        var vendor = app.IndexOf("/css/vendor/beer.min.css", StringComparison.Ordinal);
        var site = app.IndexOf("/css/site.css", StringComparison.Ordinal);
        Assert.True(vendor >= 0 && site > vendor, "BeerCSS は site.css より先に読む");
    }

    /// <summary>BeerCSS の既定を戻す上書きが、site.css に残っていること（崩れやすいので、正規表現は緩めに書く）。</summary>
    [Theory]
    [InlineData(@"\.doc-toc nav \{[^}]*white-space:\s*normal;")]
    [InlineData(@"\.app-header \{\s*min-block-size:\s*0;")]
    [InlineData(@"\.site-footer \{\s*min-block-size:\s*0;")]
    [InlineData(@"\.prose pre code, \.code-sample code \{\s*white-space:\s*pre;")]
    public void Site_css_overrides_beercss_defaults(string pattern)
    {
        var css = File.ReadAllText(Path.Combine(ProjectDir(), "wwwroot", "css", "site.css"));
        Assert.Matches(pattern, css);
    }
}
