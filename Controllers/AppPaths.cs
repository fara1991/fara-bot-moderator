using System;
using System.IO;

namespace FaraBotModerator.Controllers;

/// <summary>
/// アプリが書き込むファイルの保存先。インストール先（Program Files など、書き込めない場所）には書かず、ユーザーごとの
/// %LOCALAPPDATA%\FaraBotModerator に置く（設定・認証情報・ログを、ほかのユーザーが読み書きできないようにするため）。
/// </summary>
internal static class AppPaths
{
    /// <summary>
    /// 保存先の先頭（%LOCALAPPDATA%\FaraBotModerator）。ポケモンのデータのキャッシュも、ここに置く。
    /// </summary>
    internal static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaraBotModerator");

    /// <summary>
    /// 設定・認証情報のファイル。
    /// </summary>
    internal static string SecretFile => Path.Combine(DataDirectory, "secrets.json");

    /// <summary>
    /// ログのフォルダー。
    /// </summary>
    internal static string LogDirectory => Path.Combine(DataDirectory, "Log");

    /// <summary>
    /// BeatSaber 用の設定（bsr.json）のフォルダー。
    /// </summary>
    internal static string BeatSaberDirectory => Path.Combine(DataDirectory, "ChatSetting", "BeatSaber");

    /// <summary>
    /// WebView2 のユーザーデータ（ログイン状態など）のフォルダー。指定しないと、実行ファイルの隣に作られ、書き込めない場所では動かない。
    /// </summary>
    internal static string WebViewDirectory => Path.Combine(DataDirectory, "WebView2");
}
