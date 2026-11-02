using System;
using System.IO;
using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// secrets.json の読み込み。壊れていれば中身を消さずに退避し、初期値で動くこと。
/// </summary>
public class SecretKeyControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"));

    public SecretKeyControllerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // テスト後の掃除だけなので、消せなくても失敗にはしない
        }
    }

    [Fact]
    public void A_broken_secrets_file_is_kept_aside_and_defaults_are_written()
    {
        var path = Path.Combine(_directory, "secrets.json");
        const string secret = "KEEP-ME-SECRET-VALUE";
        // 途中で切れた JSON（書き込みの途中で止まった想定）。中身は消さずに残すこと
        File.WriteAllText(path, "{ \"twitch\": { \"api\": { \"secret\": \"" + secret + "\" ");

        var model = SecretKeyController.LoadKeys(path, out var quarantined);

        Assert.NotNull(quarantined);
        Assert.True(File.Exists(quarantined));
        Assert.Contains(secret, File.ReadAllText(quarantined!));
        // 初期値で動く（既定の文言が入っている）
        Assert.False(string.IsNullOrEmpty(model.Event.Follow.Message));
    }

    [Fact]
    public void After_recovery_the_file_reads_without_quarantine_again()
    {
        var path = Path.Combine(_directory, "secrets.json");
        File.WriteAllText(path, "null");

        var recovered = SecretKeyController.LoadKeys(path, out var first);
        var again = SecretKeyController.LoadKeys(path, out var second);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(recovered.Event.Follow.Message, again.Event.Follow.Message);
        Assert.Single(Directory.GetFiles(_directory, "secrets.broken-*"));
    }

    [Fact]
    public void A_missing_file_is_created_with_defaults_and_is_not_quarantined()
    {
        var path = Path.Combine(_directory, "secrets.json");

        var model = SecretKeyController.LoadKeys(path, out var quarantined);

        Assert.Null(quarantined);
        Assert.True(File.Exists(path));
        Assert.False(string.IsNullOrEmpty(model.Event.Follow.Message));
        Assert.Empty(Directory.GetFiles(_directory, "secrets.broken-*"));
    }

    [Theory]
    [InlineData("{ \"twitch\": null }")]
    [InlineData("{ \"twitch\": { \"client\": null } }")]
    [InlineData("{ \"event\": { \"follow\": null } }")]
    [InlineData("{ \"cycleMessage\": null }")]
    public void A_null_section_is_filled_with_defaults_and_the_file_is_not_quarantined(string json)
    {
        var path = Path.Combine(_directory, "secrets.json");
        File.WriteAllText(path, json);

        var model = SecretKeyController.LoadKeys(path, out var quarantined);

        // 入れ子の null は、既定の値で補う（画面の読み込みで NRE にならない）。壊れた扱いにはしない
        Assert.Null(quarantined);
        Assert.NotNull(model.Twitch);
        Assert.NotNull(model.Twitch.Client);
        Assert.NotNull(model.Twitch.Api);
        Assert.NotNull(model.Event.Follow);
        Assert.NotNull(model.CycleMessage.Timer1);
        Assert.Empty(Directory.GetFiles(_directory, "secrets.broken-*"));
    }

    [Fact]
    public void Other_settings_in_a_file_with_a_null_section_are_kept()
    {
        var path = Path.Combine(_directory, "secrets.json");
        File.WriteAllText(path, "{ \"twitch\": null, \"deepL\": { \"apiKey\": \"KEEP-KEY\" } }");

        var model = SecretKeyController.LoadKeys(path, out _);

        Assert.Equal("KEEP-KEY", model.DeepL.ApiKey);
    }
}
