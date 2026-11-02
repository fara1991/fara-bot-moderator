using System;
using System.IO;
using System.Linq;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// 以前の保存先（インストール先のフォルダー内）から、新しい保存先（%LOCALAPPDATA%）への、ファイルの移行。
/// </summary>
public class AppDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"));
    private string Legacy => Path.Combine(_root, "install");
    private string Data => Path.Combine(_root, "data");

    public AppDataMigrationTests()
    {
        Directory.CreateDirectory(Legacy);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // テスト後の掃除だけなので、消せなくても失敗にはしない
        }
    }

    private string Write(string relativeToLegacy, string content)
    {
        var path = Path.Combine(Legacy, relativeToLegacy);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void The_secrets_file_is_copied_to_the_new_place_and_the_old_one_is_removed()
    {
        var old = Write(Path.Combine("Resources", "secrets.json"), "{ \"keep\": \"me\" }");

        var moved = AppDataMigration.Migrate(Legacy, Data);

        Assert.Equal([Path.Combine(Data, "secrets.json")], moved);
        Assert.Equal("{ \"keep\": \"me\" }", File.ReadAllText(Path.Combine(Data, "secrets.json")));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void An_existing_file_in_the_new_place_is_never_overwritten_and_the_old_one_stays()
    {
        var old = Write(Path.Combine("Resources", "secrets.json"), "OLD");
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "secrets.json"), "NEW");

        var moved = AppDataMigration.Migrate(Legacy, Data);

        Assert.Empty(moved);
        Assert.Equal("NEW", File.ReadAllText(Path.Combine(Data, "secrets.json")));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void The_beatsaber_settings_and_the_logs_are_moved_too()
    {
        Write(Path.Combine("ChatSetting", "BeatSaber", "bsr.json"), "{}");
        Write(Path.Combine("Log", "2026.11.01.log"), "log one");
        Write(Path.Combine("Log", "Join_2026.11.01.log"), "log two");
        Write(Path.Combine("Log", "notes.txt"), "not a log");

        var moved = AppDataMigration.Migrate(Legacy, Data);

        Assert.Equal(3, moved.Count);
        Assert.True(File.Exists(Path.Combine(Data, "ChatSetting", "BeatSaber", "bsr.json")));
        Assert.Equal(["2026.11.01.log", "Join_2026.11.01.log"],
            Directory.GetFiles(Path.Combine(Data, "Log")).Select(p => Path.GetFileName(p)!).Order().ToArray());
        // ログ以外は、触らない
        Assert.True(File.Exists(Path.Combine(Legacy, "Log", "notes.txt")));
    }

    [Fact]
    public void Nothing_to_move_is_not_an_error()
    {
        Assert.Empty(AppDataMigration.Migrate(Legacy, Data));
        Assert.Empty(AppDataMigration.Migrate(Path.Combine(_root, "does-not-exist"), Data));
    }

    [Fact]
    public void A_legacy_file_that_cannot_be_deleted_is_still_copied()
    {
        var old = Write(Path.Combine("Resources", "secrets.json"), "{}");
        // 読み取り専用で、開いたまま（削除できない）にする
        using var locked = new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read);

        var moved = AppDataMigration.Migrate(Legacy, Data);

        Assert.Single(moved);
        Assert.True(File.Exists(Path.Combine(Data, "secrets.json")));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void Data_directories_are_under_the_users_local_app_data()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(local, AppPaths.DataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "secrets.json"), AppPaths.SecretFile);
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "Log"), AppPaths.LogDirectory);
        Assert.Equal(AppPaths.DataDirectory, PokemonCommandController.AppDirectory);
    }
}
