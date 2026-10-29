using MudBlazor;

namespace FaraBotModeratorSite.Web.Layout;

/// <summary>
/// このサイトの MudBlazor のテーマ。色は site.css の「テーマ」の節（:root）と同じインディゴ〜ブルー系（変えるときは、両方をそろえる）、字体は同じシステムフォント
/// （外部のフォントは使わない。site/CLAUDE.md の CSP の方針）。サイドバーの幅も以前と同じ 270px。
/// </summary>
public static class BotTheme
{
    private static readonly string[] FontFamily = { "Segoe UI", "Hiragino Sans", "Yu Gothic UI", "Meiryo", "system-ui", "sans-serif" };

    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#3447c9",
            PrimaryContrastText = "#ffffff",
            Background = "#f4f6fd",
            Surface = "#ffffff",
            TextPrimary = "#121933",
            TextSecondary = "#454f73",
            DrawerBackground = "#0d1329",
            AppbarBackground = "#16214a",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#9fb0ff",
            PrimaryContrastText = "#0f1a52",
            Background = "#090d1c",
            Surface = "#131a33",
            TextPrimary = "#eaeefc",
            TextSecondary = "#aab4d6",
            DrawerBackground = "#0d1329",
            AppbarBackground = "#16214a",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontFamily, LetterSpacing = "normal" },
            Button = new ButtonTypography { FontFamily = FontFamily, TextTransform = "none", LetterSpacing = "normal", FontWeight = "500" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "4px",
            AppbarHeight = "56px",
            DrawerWidthLeft = "270px",
            DrawerMiniWidthLeft = "56px",
        },
    };
}
