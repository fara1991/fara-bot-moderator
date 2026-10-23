using MudBlazor;

namespace FaraBotModeratorSite.Web.Layout;

/// <summary>
/// このサイトの MudBlazor のテーマ。色は以前の静的サイト（site.css の :root）と同じ紫系、字体は同じシステムフォント
/// （外部のフォントは使わない。site/CLAUDE.md の CSP の方針）。サイドバーの幅も以前と同じ 270px。
/// </summary>
public static class BotTheme
{
    private static readonly string[] FontFamily = { "Segoe UI", "Hiragino Sans", "Yu Gothic UI", "Meiryo", "system-ui", "sans-serif" };

    public static MudTheme Create() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#6d28d9",
            PrimaryContrastText = "#ffffff",
            Background = "#f7f4fd",
            Surface = "#ffffff",
            TextPrimary = "#1e1433",
            TextSecondary = "#574c73",
            DrawerBackground = "#1e0e2c",
            AppbarBackground = "#2a1240",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#c4a5ff",
            PrimaryContrastText = "#1f0d45",
            Background = "#0c0616",
            Surface = "#1a1030",
            TextPrimary = "#f3edff",
            TextSecondary = "#b4a7d1",
            DrawerBackground = "#1e0e2c",
            AppbarBackground = "#2a1240",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = FontFamily, LetterSpacing = "normal" },
            Button = new ButtonTypography { FontFamily = FontFamily, TextTransform = "none", LetterSpacing = "normal", FontWeight = "500" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            AppbarHeight = "56px",
            DrawerWidthLeft = "270px",
            DrawerMiniWidthLeft = "56px",
        },
    };
}
