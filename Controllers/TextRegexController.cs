using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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
public static class TextRegexController
{
    private static readonly string BeatSaberDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatSetting", "BeatSaber");
    private static readonly string BeatSaberFile = Path.Combine(BeatSaberDirectory, "bsr.json");

    /// <summary>
    /// 読み書きを 1 つずつにするロック（チャットの読み上げと、画面のリセットが同時に触らないため）
    /// </summary>
    private static readonly object FileLock = new();

    /// <summary>
    /// ログに出した不正な正規表現・時間切れの正規表現（同じものを、チャットのたびに出し続けないため）
    /// </summary>
    private static readonly HashSet<string> ReportedInvalidPatterns = [];

    /// <summary>
    /// 1 つのパターンを実行してよい時間。これを超えたら、その行は無視する（チャットの処理を止めないため）
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// コンパイル済みのパターン（同じパターンを、チャットのたびに作り直さないため）。不正なパターンは null
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex?> CompiledPatterns = new();

    /// <summary>
    /// 読み込み済みの bsr.json（チャットのたびにファイルを読まないため）。ファイルの更新日時が変わったら読み直す
    /// </summary>
    private static (string Path, DateTime WriteTimeUtc, TextRegexModel Model)? _cachedBsrChat;

    /// <summary>
    /// BeatSaber用設定ファイルのモデルを読み込んで返します。ファイルが存在しない場合はデフォルトで作成します。
    /// 壊れたファイル（JSON として読めない）は改名して退避し、既定の設定を返します（設定は消しません）。
    /// </summary>
    public static TextRegexModel LoadBsrChatModel() => LoadBsrChatModel(BeatSaberFile);

    /// <summary>
    /// BeatSaber用設定ファイルをデフォルト値にリセットします。書けなかったとき（ファイルが使用中など）は、ログに残して false を返します。
    /// </summary>
    /// <returns>リセットできたか</returns>
    public static bool ResetBsrChatModel()
    {
        lock (FileLock)
        {
            try
            {
                WriteModel(BeatSaberFile, DefaultModel());
                _cachedBsrChat = null;
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogController.OutputLog($"<Error> BeatSaber設定を初期化できませんでした: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// BeatSaberのリクエスト形式などのテキストを、設定された正規表現に基づいて読み上げ用テキストに変換します。
    /// 不正な正規表現・時間内に終わらない正規表現の行は無視し（ログに出します）、ほかの行は使います。
    /// 設定ファイルは、更新されたときだけ読み直します。
    /// </summary>
    /// <param name="bsrText">変換前のテキスト</param>
    /// <returns>変換後のテキスト</returns>
    public static string LoadBsrChat(string bsrText)
    {
        return LoadBsrChat(bsrText, BeatSaberFile);
    }

    /// <summary>
    /// 指定した設定ファイルを使って変換します（<see cref="LoadBsrChat(string)"/> と同じ。テストで別のファイルを使うため）。
    /// </summary>
    internal static string LoadBsrChat(string bsrText, string path)
    {
        return ConvertText(bsrText, CachedBsrChatModel(path));
    }

    /// <summary>
    /// 読み込み済みの設定を返します。ファイルの更新日時が前と同じなら、ファイルを読まずに前のものを返します。
    /// 読めなかったとき（ファイルのロックなど）は既定の設定を返すだけで、覚えません（次のチャットで読み直し、直ったら反映するため）。
    /// </summary>
    private static TextRegexModel CachedBsrChatModel(string path)
    {
        lock (FileLock)
        {
            var existedBefore = File.Exists(path);
            // 読む前に更新日時を取る（読んだあとに取ると、読みと取得の間の編集で、古い内容に新しい日時が付いてしまう）
            var writeTime = existedBefore ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_cachedBsrChat is { } cached && cached.Path == path && cached.WriteTimeUtc == writeTime)
                return cached.Model;

            var model = LoadBsrChatModel(path, out var readFailed);
            if (readFailed)
            {
                _cachedBsrChat = null;
                return model;
            }

            // ファイルが無くて作った場合は、作ったファイルの更新日時で覚える。退避して無くなった場合は、無いものとして覚える（次のチャットで作り直さないため）
            if (!existedBefore)
                writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            else if (!File.Exists(path))
                writeTime = DateTime.MinValue;
            _cachedBsrChat = (path, writeTime, model);
            CompiledPatterns.Clear();
            return model;
        }
    }

    /// <summary>
    /// 指定したファイルから設定を読みます（ファイルが無ければ既定の設定を書きます）。
    /// </summary>
    /// <param name="path">設定ファイルのパス</param>
    internal static TextRegexModel LoadBsrChatModel(string path) => LoadBsrChatModel(path, out _);

    /// <summary>
    /// 指定したファイルから設定を読みます。読めなかった（IOException など）ときは <paramref name="readFailed"/> を true にして既定の設定を返します。
    /// </summary>
    /// <param name="path">設定ファイルのパス</param>
    /// <param name="readFailed">読めなかったか（既定の設定を返したが、ファイルの内容としては確かめられていないとき true）</param>
    internal static TextRegexModel LoadBsrChatModel(string path, out bool readFailed)
    {
        lock (FileLock)
        {
            readFailed = false;
            try
            {
                if (!File.Exists(path)) WriteModel(path, DefaultModel());
                var model = JsonSerializer.Deserialize<TextRegexModel>(File.ReadAllText(path));
                if (model is not null) return model;
                Quarantine(path, "内容が空です");
            }
            catch (JsonException e)
            {
                Quarantine(path, e.Message);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 読めないだけ（ファイルのロックなど）のときは、ファイルには触らず既定の設定を使う
                readFailed = true;
                LogController.OutputLog($"<Error> BeatSaber設定を読めませんでした（既定の設定を使います）: {e.Message}");
            }

            return DefaultModel();
        }
    }

    /// <summary>
    /// 設定に従ってテキストを変換します。不正な正規表現の行は、その行だけ無視します。
    /// </summary>
    internal static string ConvertText(string bsrText, TextRegexModel model)
    {
        foreach (var textKeyValues in model.BeatSaberChat ?? [])
        {
            var regex = textKeyValues.Key is null ? null : GetRegex(textKeyValues.Key);
            if (regex is null) continue;

            try
            {
                // MatchCollection は遅延評価なので、件数や値を読むときに時間切れの例外が出る。読む処理は全部 try の中に入れる
                var matches = regex.Matches(bsrText);
                if (matches.Count == 0) continue;

                // !bsr xxxx
                // Request Dope /Krouton_06 68.1% (xxxx) added to queue.
                // ↑をチャット表示用にデータ取得でもいい
                // https://api.beatsaver.com/maps/id/xxxx
                // 1 回の走査で置き換える（順に Replace すると、一致した文の中の "{1}" が次の段階で置き換わってしまう）。
                // 一致の文は MatchEvaluator で入れる（$ を置換の書式として解釈させない）
                var value = textKeyValues.Value ?? string.Empty;
                return Regex.Replace(value, @"\{(0|[1-9]\d*)\}", placeholder =>
                    int.TryParse(placeholder.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < matches.Count
                        ? matches[index].Value
                        : placeholder.Value);
            }
            catch (RegexMatchTimeoutException)
            {
                ReportOnce(textKeyValues.Key!, $"BeatSaber設定の正規表現が {RegexTimeout.TotalMilliseconds} ms 以内に終わらないため、この行は無視します");
            }
        }

        return bsrText;
    }

    /// <summary>
    /// パターンをコンパイル済みで返します（同じパターンは 1 度だけ作る）。不正なパターンは、ログに出して null。
    /// </summary>
    private static Regex? GetRegex(string pattern)
    {
        return CompiledPatterns.GetOrAdd(pattern, p =>
        {
            try
            {
                return new Regex(p, RegexOptions.None, RegexTimeout);
            }
            catch (ArgumentException e)
            {
                ReportOnce(p, $"BeatSaber設定の正規表現が不正なため、この行は無視します ({e.Message})");
                return null;
            }
        });
    }

    /// <summary>
    /// 同じパターンの警告は 1 回だけログに出します。
    /// </summary>
    private static void ReportOnce(string pattern, string message)
    {
        lock (ReportedInvalidPatterns)
        {
            if (!ReportedInvalidPatterns.Add(pattern)) return;
        }

        LogController.OutputLog($"<Error> {message}: {pattern}");
    }

    /// <summary>
    /// 壊れた設定ファイルを、日時の付いた名前に改名して退避します（消しません）。
    /// </summary>
    private static void Quarantine(string path, string reason)
    {
        var broken = Path.Combine(Path.GetDirectoryName(path) ?? "",
            $"{Path.GetFileNameWithoutExtension(path)}.broken-{DateTime.Now:yyyyMMdd-HHmmssfff}{Path.GetExtension(path)}");
        try
        {
            File.Move(path, broken);
            LogController.OutputLog($"<Error> BeatSaber設定が壊れていたため退避しました（既定の設定を使います）: {broken} ({reason})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogController.OutputLog($"<Error> BeatSaber設定が壊れていて、退避もできませんでした（既定の設定を使います）: {reason} ({e.Message})");
        }
    }

    /// <summary>
    /// 設定を一時ファイルに書いてから置き換えます（書き込みの途中で止まっても、元のファイルは壊れません）。
    /// </summary>
    private static void WriteModel(string path, TextRegexModel textRegex)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

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

        var temp = path + ".tmp";
        File.WriteAllText(temp, jsonData + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    /// <summary>
    /// 既定の BeatSaber 用の設定。
    /// </summary>
    private static TextRegexModel DefaultModel()
    {
        return new TextRegexModel
        {
            BeatSaberChat =
            [
                new BeatSaberChatModel("(?<=!bsr ).*", "から、ソングリクエスト{0}を頂きました。"),
                new BeatSaberChatModel("(?<=Request ).*?(?= /)", "リクエスト曲 {0} が登録されました。"),
                new BeatSaberChatModel("(?=.*requested by )(?=.*added to queue).*",
                    ""),
                new BeatSaberChatModel("(?=.*already in queue).*",
                    "既にリクエストされた曲です。"),
                new BeatSaberChatModel("^[^/]+(?= */)|(?<=requested by )[^ ]+(?= +is next)",
                    "次の曲は、{1}さんがリクエストした{0}です。"),

                new BeatSaberChatModel("(?=Queue is closed).*", "ソングリクエストを終了します。皆さんありがとう！"),
                new BeatSaberChatModel("(?=Queue is open).*", "ソングリクエストを開始しました。リクエストお待ちしてます。"),
                new BeatSaberChatModel("(?<=map ).*?(?= not found)",
                    "リクエスト曲{0}は存在しません。"),
                new BeatSaberChatModel("(?<=No results found for request ).*", "{0}はリクエストにないよ。"),
                new BeatSaberChatModel("(?<=Request for).*(?=produces)|(?<=produces).*(?=results)",
                    "{0} で検索したら {1}曲あったよ。絞り込んでみてね。")
            ]
        };
    }
}
