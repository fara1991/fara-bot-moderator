using System;

namespace FaraBotModerator.Controllers;

/// <summary>
/// メインウィンドウの大きさの決め方（画面が小さい PC での最大化・WebView の枠の広げ方）。
/// </summary>
internal static class WindowLayoutPolicy
{
    /// <summary>
    /// WebView の枠の、左端の位置（左の Twitch の設定の枠の右）。
    /// </summary>
    internal const double WebViewLeft = 565;

    /// <summary>
    /// WebView の枠と、ウィンドウの端の間の余白。
    /// </summary>
    internal const double Margin = 10;

    /// <summary>
    /// WebView の枠の、最小の大きさ。
    /// </summary>
    internal const double MinWebViewSize = 300;

    /// <summary>
    /// 画面の作業領域が、ウィンドウの既定の大きさより小さいか（小さければ、最大化して使う）。
    /// </summary>
    /// <param name="workAreaWidth">作業領域の幅</param>
    /// <param name="workAreaHeight">作業領域の高さ</param>
    /// <param name="windowWidth">ウィンドウの既定の幅</param>
    /// <param name="windowHeight">ウィンドウの既定の高さ</param>
    internal static bool ShouldMaximize(double workAreaWidth, double workAreaHeight, double windowWidth, double windowHeight)
        => workAreaWidth < windowWidth || workAreaHeight < windowHeight;

    /// <summary>
    /// WebView の枠の大きさ。タブの中の領域のうち、左の枠と余白を除いた分（小さくなりすぎないよう、下限あり）。
    /// </summary>
    /// <param name="canvasWidth">タブの中の領域の幅</param>
    /// <param name="canvasHeight">タブの中の領域の高さ</param>
    internal static (double Width, double Height) WebViewSize(double canvasWidth, double canvasHeight)
        => (Math.Max(MinWebViewSize, canvasWidth - WebViewLeft - Margin),
            Math.Max(MinWebViewSize, canvasHeight - Margin * 2));
}
