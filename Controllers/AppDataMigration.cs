using System;
using System.Collections.Generic;
using System.IO;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 以前の保存先（インストール先のフォルダー内の Resources・ChatSetting・Log）にあるファイルを、新しい保存先（%LOCALAPPDATA%\FaraBotModerator）へ移す。
/// 先にコピーして、できてから元を消す（コピーに失敗したら、元は残す）。新しい保存先に同じファイルがあるときは、何もしない（上書きしない）。
/// 元を消せなかった（書き込めない場所）ときも、コピーは済んでいるので、続ける。
/// </summary>
internal static class AppDataMigration
{
    /// <summary>
    /// 以前の保存先のファイルを、新しい保存先へ移します。
    /// </summary>
    /// <param name="legacyBase">以前の保存先（実行ファイルのフォルダー）</param>
    /// <param name="dataDirectory">新しい保存先</param>
    /// <returns>移したファイル（新しい保存先でのパス）</returns>
    internal static IReadOnlyList<string> Migrate(string legacyBase, string dataDirectory)
    {
        var moved = new List<string>();
        MoveFile(Path.Combine(legacyBase, "Resources", "secrets.json"), Path.Combine(dataDirectory, "secrets.json"), moved);
        MoveFile(Path.Combine(legacyBase, "ChatSetting", "BeatSaber", "bsr.json"),
            Path.Combine(dataDirectory, "ChatSetting", "BeatSaber", "bsr.json"), moved);

        var legacyLog = Path.Combine(legacyBase, "Log");
        if (Directory.Exists(legacyLog))
        {
            foreach (var file in Directory.GetFiles(legacyLog, "*.log"))
                MoveFile(file, Path.Combine(dataDirectory, "Log", Path.GetFileName(file)), moved);
        }

        return moved;
    }

    private static void MoveFile(string source, string destination, List<string> moved)
    {
        try
        {
            if (!File.Exists(source) || File.Exists(destination)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
            moved.Add(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // コピーできなかった: 元を残して、次の起動でやり直す
            return;
        }

        try
        {
            File.Delete(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 元を消せない（書き込めない場所）。コピーは済んでいるので、続ける
        }
    }
}
