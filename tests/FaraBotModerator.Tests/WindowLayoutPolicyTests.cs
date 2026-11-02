using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// メインウィンドウの大きさの決め方（画面が小さい PC では最大化・WebView の枠は空いた分だけ広げる）。
/// </summary>
public class WindowLayoutPolicyTests
{
    [Theory]
    [InlineData(1920, 1040, 1500, 600, false)]
    [InlineData(1500, 1040, 1500, 600, false)]
    [InlineData(1366, 728, 1500, 600, true)]
    [InlineData(1499, 1040, 1500, 600, true)]
    [InlineData(1920, 560, 1500, 600, true)]
    public void A_screen_smaller_than_the_default_window_is_maximized(double workWidth, double workHeight, double width, double height, bool expected)
    {
        Assert.Equal(expected, WindowLayoutPolicy.ShouldMaximize(workWidth, workHeight, width, height));
    }

    [Fact]
    public void The_web_view_takes_the_space_left_of_the_settings_box()
    {
        var (width, height) = WindowLayoutPolicy.WebViewSize(1484, 520);

        Assert.Equal(1484 - 565 - 10, width);
        Assert.Equal(500, height);
    }

    [Fact]
    public void The_web_view_never_gets_smaller_than_the_minimum()
    {
        var (width, height) = WindowLayoutPolicy.WebViewSize(700, 200);

        Assert.Equal(WindowLayoutPolicy.MinWebViewSize, width);
        Assert.Equal(WindowLayoutPolicy.MinWebViewSize, height);
    }
}
