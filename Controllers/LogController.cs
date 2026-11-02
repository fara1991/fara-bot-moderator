using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using FaraBotModerator.Enums;

namespace FaraBotModerator.Controllers;

/// <summary>
/// ログ出力機能を担当する静的クラス
/// </summary>
public static class LogController
{
    /// <summary>
    /// ログの排他制御（Mutex）を待つ上限。
    /// </summary>
    private static readonly TimeSpan MutexWaitTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 指定されたテキストをログファイルに出力します。
    /// </summary>
    /// <param name="text">出力するテキスト</param>
    /// <param name="eventEnum">Twitchイベントの種類（指定すると種類別のログファイルにも出力されます）</param>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "ログ出力そのものが失敗したときの最後の逃げ道。ここで例外を出すと、ログを呼ぶあらゆる処理が落ちる")]
    public static void OutputLog(string text, TwitchEventEnum eventEnum = TwitchEventEnum.None)
    {
        try
        {
            var d = DateTime.Now;
            var directoryName = AppPaths.LogDirectory;
            var fileName = $"{d.Year:0000}.{d.Month:00}.{d.Day:00}.log";
            var filePath = Path.Combine(directoryName, fileName);
            
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            var logText = $"[{d.Year:0000}/{d.Month:00}/{d.Day:00} {d.Hour:00}:{d.Minute:00}:{d.Second:00}] " + text;
            AppendWithLock(fileName, filePath, logText);

            if (eventEnum == TwitchEventEnum.None) return;

            // Follow等のTwitchEventログは配信終了画像に自動で追加
            var eventFileName =
                $"{Enum.GetName(typeof(TwitchEventEnum), eventEnum)}_{d.Year:0000}.{d.Month:00}.{d.Day:00}.log";
            var eventFilePath = Path.Combine(directoryName, eventFileName);
            AppendWithLock(eventFileName, eventFilePath, logText);
        }
        catch (Exception ex)
        {
            // ログ出力自体が失敗した場合は、コンソールやデバッグ出力に逃がすか、無視するしかない
            System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
        }
    }

    /// <summary>
    /// 排他制御（Mutex）をかけて、1 行を追記します。待つのは短い間だけで、間に合わなければ、この 1 行は記録しません（呼び出し元の処理は続けます）。
    /// </summary>
    /// <param name="mutexName">排他制御の名前</param>
    /// <param name="filePath">追記するファイル</param>
    /// <param name="line">追記する 1 行</param>
    private static void AppendWithLock(string mutexName, string filePath, string line)
    {
        using var mutex = new Mutex(false, mutexName);

        // 他のプロセスが握ったままのとき、UI スレッドが固まらないよう、待つのは短い間だけ
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(MutexWaitTimeout);
        }
        catch (AbandonedMutexException)
        {
            // 前の持ち主が放さずに終了した場合。この時点で、こちらが握っているので、そのまま書く
            acquired = true;
        }

        if (!acquired) return;
        try
        {
            using var writer = new StreamWriter(filePath, true, Encoding.UTF8);
            writer.WriteLine(line);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
