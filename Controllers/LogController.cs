using System;
using System.IO;
using System.Text;
using System.Threading;
using FaraBotModerator.Enums;

namespace FaraBotModerator.Controllers;

/// <summary>
/// </summary>
public static class LogController
{
    /// <summary>
    /// </summary>
    /// <param name="text"></param>
    /// <param name="eventEnum"></param>
    public static void OutputLog(string text, TwitchEventEnum eventEnum = TwitchEventEnum.None)
    {
        var d = DateTime.Now;
        var directoryName = $@"{Directory.GetCurrentDirectory()}\Log";
        var fileName = $"{d.Year:0000}.{d.Month:00}.{d.Day:00}.log";
        var filePath = $@"{directoryName}\{fileName}";
        if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);

        // 排他制御
        using var mutex = new Mutex(false, fileName);
        mutex.WaitOne();
        using (var writer = new StreamWriter(filePath, true, Encoding.UTF8))
        {
            var logTime = $"[{d.Year:0000}/{d.Month:00}/{d.Day:00} {d.Hour:00}:{d.Minute:00}:{d.Second:00}] ";
            var logText = logTime + text;
            writer.WriteLine(logText);
        }

        mutex.ReleaseMutex();

        if (eventEnum == TwitchEventEnum.None) return;

        // Follow等のTwitchEventログは配信終了画像に自動で追加
        var eventFileName =
            $"{System.Enum.GetName(typeof(TwitchEventEnum), eventEnum)}_{d.Year:0000}.{d.Month:00}.{d.Day:00}.log";
        var eventFilePath = $@"{directoryName}\{eventFileName}";

        // 排他制御
        using var eventMutex = new Mutex(false, fileName);
        eventMutex.WaitOne();
        using (var writer = new StreamWriter(eventFilePath, true, Encoding.UTF8))
        {
            var logTime = $"[{d.Year:0000}/{d.Month:00}/{d.Day:00} {d.Hour:00}:{d.Minute:00}:{d.Second:00}] ";
            var logText = logTime + text;
            writer.WriteLine(logText);
        }

        eventMutex.ReleaseMutex();
    }
}