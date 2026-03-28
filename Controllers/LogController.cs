using System;
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
    /// 指定されたテキストをログファイルに出力します。
    /// </summary>
    /// <param name="text">出力するテキスト</param>
    /// <param name="eventEnum">Twitchイベントの種類（指定すると種類別のログファイルにも出力されます）</param>
    public static void OutputLog(string text, TwitchEventEnum eventEnum = TwitchEventEnum.None)
    {
        try
        {
            var d = DateTime.Now;
            var directoryName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
            var fileName = $"{d.Year:0000}.{d.Month:00}.{d.Day:00}.log";
            var filePath = Path.Combine(directoryName, fileName);
            
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            // 排他制御
            using var mutex = new Mutex(false, fileName);
            mutex.WaitOne();
            try
            {
                using (var writer = new StreamWriter(filePath, true, Encoding.UTF8))
                {
                    var logTime = $"[{d.Year:0000}/{d.Month:00}/{d.Day:00} {d.Hour:00}:{d.Minute:00}:{d.Second:00}] ";
                    var logText = logTime + text;
                    writer.WriteLine(logText);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }

            if (eventEnum == TwitchEventEnum.None) return;

            // Follow等のTwitchEventログは配信終了画像に自動で追加
            var eventFileName =
                $"{Enum.GetName(typeof(TwitchEventEnum), eventEnum)}_{d.Year:0000}.{d.Month:00}.{d.Day:00}.log";
            var eventFilePath = Path.Combine(directoryName, eventFileName);

            // 排他制御
            using var eventMutex = new Mutex(false, eventFileName);
            eventMutex.WaitOne();
            try
            {
                using (var writer = new StreamWriter(eventFilePath, true, Encoding.UTF8))
                {
                    var logTime = $"[{d.Year:0000}/{d.Month:00}/{d.Day:00} {d.Hour:00}:{d.Minute:00}:{d.Second:00}] ";
                    var logText = logTime + text;
                    writer.WriteLine(logText);
                }
            }
            finally
            {
                eventMutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            // ログ出力自体が失敗した場合は、コンソールやデバッグ出力に逃がすか、無視するしかない
            System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
        }
    }
}