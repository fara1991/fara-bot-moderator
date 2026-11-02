using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using FaraBotModerator.Controllers;

namespace FaraBotModerator;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// アプリの起動中、持ち続ける Mutex の名前。インストーラー（Installer\installer.iss の AppMutex）が、起動中かを調べるのに使う。
    /// </summary>
    private const string AppMutexName = "FaraBotModerator_Running";

    /// <summary>
    /// 起動中ずっと持つ（プロセスが終わると、解放される）。
    /// </summary>
    private static Mutex? _appMutex;

    /// <summary>
    ///     Handles the startup logic for the application.
    /// </summary>
    /// <param name="e">Provides data for the startup event, including command-line arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 起動中であることを、インストーラー・アンインストーラーに知らせる（起動中は、インストール・アンインストールを止めるため）
        _appMutex = new Mutex(false, AppMutexName);

        // 以前の保存先（インストール先のフォルダー内）にあるファイルを、新しい保存先へ移してから、書き込みを確認する
        MigrateLegacyData();
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
    /// <summary>
    /// 以前の保存先（インストール先のフォルダー内の Resources・ChatSetting・Log）にあるファイルを、新しい保存先へ移します。
    /// </summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "起動時の移行。失敗しても起動は止めず、次の起動でやり直す。理由はログに残す")]
    private static void MigrateLegacyData()
    {
        try
        {
            var moved = AppDataMigration.Migrate(AppDomain.CurrentDomain.BaseDirectory, AppPaths.DataDirectory);
            if (moved.Count > 0) LogController.OutputLog($"以前の保存先のファイルを、新しい保存先へ移しました（{moved.Count} 件）: {AppPaths.DataDirectory}");
        }
        catch (Exception ex)
        {
            LogController.OutputLog($"<Error> 以前の保存先のファイルを移せませんでした: {ex.GetType().Name}");
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "起動時の書き込み確認。確認できなかっただけでは起動を止めず、理由はログに残す")]
    private static void CheckWritePermission()
    {
        var dataDir = AppPaths.DataDirectory;
        try
        {
            // 設定・ログの保存先に、一時ファイルを作って、書き込みを確かめる
            System.IO.Directory.CreateDirectory(dataDir);
            var testFile = System.IO.Path.Combine(dataDir, $".write_test_{Guid.NewGuid()}");
            System.IO.File.WriteAllText(testFile, "test");
            System.IO.File.Delete(testFile);

            // Log と ChatSetting のフォルダーを作る
            System.IO.Directory.CreateDirectory(AppPaths.LogDirectory);
            System.IO.Directory.CreateDirectory(AppPaths.BeatSaberDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "設定・ログの保存先に書き込み権限がありません。\n" +
                $"場所: {dataDir}\n\n" +
                "このフォルダーの権限（ユーザー自身が書き込めること）を確認してください。",
                "権限エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            // その他のエラー（書き込み確認ができなかっただけなので、起動は続ける。理由はログに残す）
            LogController.OutputLog($"<Error> 設定・ログの保存先の書き込み確認に失敗しました: {ex.GetType().Name}");
        }
    }
}