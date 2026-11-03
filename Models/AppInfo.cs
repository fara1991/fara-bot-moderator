using System.Reflection;

namespace FaraBotModerator.Models;

/// <summary>
/// アプリの名前とバージョン。バージョンは、ビルド時に埋め込まれた値（リリースでは Installer/installer.iss の MyAppVersion）を読む。
/// 手元のビルドは、既定の「0.0.0-dev」になり、「開発版」と表示する。
/// </summary>
public static class AppInfo
{
    /// <summary>アプリの名前。</summary>
    public const string Name = "FaraBotModerator";

    /// <summary>バージョンを指定せずにビルドしたとき（手元のビルド）の値。FaraBotModerator.csproj の Version と同じにする。</summary>
    public const string DevelopmentVersion = "0.0.0-dev";

    /// <summary>「1.2.0」のような版の番号。</summary>
    public static string Version { get; } = ParseInformationalVersion(ReadInformationalVersion());

    /// <summary>画面に表示するバージョン。例:「v1.2.0」「開発版」</summary>
    public static string DisplayVersion { get; } = FormatDisplayVersion(Version);

    /// <summary>ウィンドウのタイトル。例:「FaraBotModerator v1.2.0」</summary>
    public static string WindowTitle { get; } = $"{Name} {DisplayVersion}";

    /// <summary>
    /// 「1.2.0+abcdef0123…」の形のバージョン情報から、版の番号だけを取り出す。読めないときは、開発版として扱う。
    /// </summary>
    public static string ParseInformationalVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return DevelopmentVersion;

        var version = informationalVersion.Split('+', 2)[0].Trim();
        return version.Length == 0 ? DevelopmentVersion : version;
    }

    /// <summary>
    /// 表示用のバージョン。リリースは先頭に「v」を付ける（v1.2.0）。開発版（0.0.0-dev）は「開発版」と表示する。
    /// </summary>
    public static string FormatDisplayVersion(string version)
        => version == DevelopmentVersion ? "開発版" : $"v{version}";

    private static string? ReadInformationalVersion()
        => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
