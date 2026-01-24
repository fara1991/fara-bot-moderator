using System;
using System.Windows;
using FaraBotModerator.Controllers;

namespace FaraBotModerator;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 実行ディレクトリへの書き込み権限チェック
        CheckWritePermission();

        // 未処理の例外をキャッチしてログに出力する
        DispatcherUnhandledException += (s, args) =>
        {
            LogController.OutputLog($"[Fatal] DispatcherUnhandledException: {args.Exception}");
            MessageBox.Show($"致命的なエラーが発生しました:\n{args.Exception.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (object s, UnhandledExceptionEventArgs args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LogController.OutputLog($"[Fatal] AppDomain.UnhandledException: {ex?.ToString() ?? "Unknown error"}");
            MessageBox.Show($"致命的なエラーが発生しました:\n{ex?.Message ?? "Unknown error"}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
    }

    private void CheckWritePermission()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            // 一時ファイルを作成して書き込みテストを行う
            var testFile = System.IO.Path.Combine(baseDir, $".write_test_{Guid.NewGuid()}");
            System.IO.File.WriteAllText(testFile, "test");
            System.IO.File.Delete(testFile);
            
            // LogとChatSettingフォルダの作成を試みる
            var logDir = System.IO.Path.Combine(baseDir, "Log");
            if (!System.IO.Directory.Exists(logDir)) System.IO.Directory.CreateDirectory(logDir);
            
            var chatSettingDir = System.IO.Path.Combine(baseDir, "ChatSetting");
            if (!System.IO.Directory.Exists(chatSettingDir)) System.IO.Directory.CreateDirectory(chatSettingDir);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "アプリケーションの実行フォルダに書き込み権限がありません。\n" +
                $"場所: {baseDir}\n\n" +
                "「管理者として実行」するか、書き込み権限のあるフォルダ（デスクトップ等）に移動して実行してください。",
                "権限エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // その他のエラー（ログに出力できればする）
            System.Diagnostics.Debug.WriteLine($"Permission check failed: {ex.Message}");
        }
    }
}