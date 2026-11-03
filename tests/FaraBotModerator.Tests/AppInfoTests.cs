using FaraBotModerator.Models;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// アプリのバージョン（ビルドに埋め込まれた値）の読み取りと、表示。
/// </summary>
public class AppInfoTests
{
    [Theory]
    [InlineData("1.2.0+abcdef0123456789", "1.2.0")]
    [InlineData("1.2.0-beta.1+deadbeef", "1.2.0-beta.1")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1.2.0+", "1.2.0")]
    [InlineData("0.0.0-dev+1a2b3c4d", "0.0.0-dev")]
    [InlineData("", "0.0.0-dev")]
    [InlineData("   ", "0.0.0-dev")]
    [InlineData(null, "0.0.0-dev")]
    [InlineData("+abcdef0", "0.0.0-dev")]
    public void The_version_number_is_taken_from_the_informational_version(string? informationalVersion, string expected)
    {
        Assert.Equal(expected, AppInfo.ParseInformationalVersion(informationalVersion));
    }

    [Theory]
    [InlineData("1.2.0", "v1.2.0")]
    [InlineData("1.2.0-beta.1", "v1.2.0-beta.1")]
    [InlineData("0.0.0-dev", "開発版")]
    public void A_development_build_is_shown_as_a_development_version(string version, string expected)
    {
        Assert.Equal(expected, AppInfo.FormatDisplayVersion(version));
    }

    [Fact]
    public void The_version_embedded_in_the_build_can_be_read()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
        Assert.DoesNotContain("+", AppInfo.Version);
        Assert.Equal($"{AppInfo.Name} {AppInfo.DisplayVersion}", AppInfo.WindowTitle);
    }
}
