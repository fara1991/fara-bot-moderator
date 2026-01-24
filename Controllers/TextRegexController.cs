using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using FaraBotModerator.Models;

namespace FaraBotModerator.Controllers;

/// <summary>
/// 正規表現を用いたテキスト変換（主にBeatSaber関連）を管理するクラス
/// </summary>
internal static class TextRegexController
{
    private static readonly string BeatSaberDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatSetting", "BeatSaber");
    private static readonly string BeatSaberFile = Path.Combine(BeatSaberDirectory, "bsr.json");

    /// <summary>
    /// BeatSaberのリクエスト形式などのテキストを、設定された正規表現に基づいて読み上げ用テキストに変換します。
    /// </summary>
    /// <param name="bsrText">変換前のテキスト</param>
    /// <returns>変換後のテキスト</returns>
    public static string LoadBsrChat(string bsrText)
    {
        // BeatSaber
        if (!Directory.Exists(BeatSaberDirectory)) Directory.CreateDirectory(BeatSaberDirectory);
        if (!File.Exists(BeatSaberFile)) CreateBsrChatFile();
        try
        {
            TextRegexModel? textRegex;
            using (var file = File.OpenText(BeatSaberFile))
            {
                var jsonData = file.ReadToEnd();
                textRegex = JsonSerializer.Deserialize<TextRegexModel>(jsonData);
            }

            if (textRegex is null)
            {
                const string message = "<Error> Text conversion not possible.";
                LogController.OutputLog(message);
                throw new FileFormatException(message);
            }

            foreach (var textKeyValues in textRegex.BeatSaberChat)
                if (Regex.IsMatch(bsrText, textKeyValues.Key))
                {
                    // !bsr xxxx
                    // Request Dope /Krouton_06 68.1% (xxxx) added to queue.
                    // ↑をチャット表示用にデータ取得でもいい
                    // https://api.beatsaver.com/maps/id/xxxx
                    var matches = Regex.Matches(bsrText, textKeyValues.Key);
                    for (var i = 0; i < matches.Count; i++)
                        textKeyValues.Value = textKeyValues.Value.Replace("{" + i + "}", matches[i].Value);

                    return textKeyValues.Value;
                }
        }
        catch (Exception e)
        {
            LogController.OutputLog($"<Error> {e.Message}");
            var fileInfo = new FileInfo(BeatSaberFile);
            fileInfo.Delete();
            CreateBsrChatFile();
        }

        return bsrText;
    }

    /// <summary>
    /// BeatSaber用設定ファイルを保存します。
    /// </summary>
    /// <param name="textRegex">保存する正規表現モデル</param>
    private static void SaveBsrChatFile(TextRegexModel textRegex)
    {
        using var writer = new StreamWriter(BeatSaberFile, false, Encoding.UTF8);
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = true
        };
        var jsonData = JsonSerializer.Serialize(textRegex, options);

        // 変換したい特殊文字は先に変換
        jsonData = jsonData
            .Replace("\\u002B", "+")
            .Replace("\\u003C", "<");
        writer.WriteLine(jsonData);
    }

    /// <summary>
    /// デフォルトのBeatSaber用設定ファイルを作成します。
    /// </summary>
    private static void CreateBsrChatFile()
    {
        var textRegex = new TextRegexModel
        {
            BeatSaberChat =
            [
                new BeatSaberChatModel("(?<=!bsr ).*", "から、ソングリクエスト{0}を頂きました。"),
                new BeatSaberChatModel("(?<=Request ).*?(?= /)", "リクエスト曲 {0} が登録されました。"),
                new BeatSaberChatModel("^[^/]+(?= */)|(?<=requested by )[^ ]+(?= +is next)",
                    "次の曲は、{1}さんがリクエストした{0}です。"),

                new BeatSaberChatModel("(?=Queue is closed).*", "ソングリクエストを終了します。皆さんありがとう！"),
                new BeatSaberChatModel("(?=Queue is open).*", "ソングリクエストを開始しました。リクエストお待ちしてます。"),
                new BeatSaberChatModel("(?<=No results found for request ).*", "{0}はリクエストにないよ。"),
                new BeatSaberChatModel("(?<=Request for).*(?=produces)|(?<=produces).*(?=results)",
                    "{0} で検索したら {1}曲あったよ。絞り込んでみてね。")
            ]
        };
        SaveBsrChatFile(textRegex);
    }
}