using System;
using System.Windows;
using FaraBotModerator.Controllers;

namespace FaraBotModerator;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    ///     Handles the startup logic for the application.
    /// </summary>
    /// <param name="e">Provides data for the startup event, including command-line arguments.</param>
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

    /// <summary>
    ///     Verifies write permissions in the application's execution directory by attempting to create files and directories.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    ///     Thrown when write permission is not granted in the application's directory.
    ///     A message box is displayed regarding insufficient permissions.
    /// </exception>
    /// <remarks>
    ///     This method performs the following actions:
    ///     - Attempts to create and delete a test file in the application directory.
    ///     - Ensures the creation of required directories for logging and chat settings.
    ///     If any errors occur, they are either communicated to the user via a message box or logged for debugging purposes.
    /// </remarks>
    private static void CheckWritePermission()
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