using System;
using System.IO;
using System.Linq;
using System.Text;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// 「Random Tailor」の設定ファイル。ファイル名に使えない名前は書かず、使える名前だけ書くこと。
/// </summary>
public class UniqueChannelPointControllerTests : IDisposable
{
    private const string RandomTailor = "Random Tailor";

    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"));

    public UniqueChannelPointControllerTests()
    {
        Directory.CreateDirectory(_directory);
        // 設定ファイルは Shift-JIS で書く（アプリは起動のときに登録する。テストでも同じにする）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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

    private UniqueChannelPointController CreateController() =>
        new(new SecretKeyModel { BeatSaber = new BeatSaberModel { UserDataPath = _directory } });

    [Theory]
    [InlineData("bad name")]
    [InlineData("../escape")]
    [InlineData("日本語")]
    [InlineData("a.b")]
    public void A_user_name_with_characters_that_are_not_allowed_writes_no_file(string userName)
    {
        var message = CreateController().Exec(userName, RandomTailor);

        Assert.Contains("could not create", message);
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_directory)!, "escape*"));
    }

    [Fact]
    public void A_login_name_writes_one_setting_file_named_after_it()
    {
        var message = CreateController().Exec("viewer_1", RandomTailor);

        Assert.StartsWith("New Tailor SaberTailor.viewer_1-", message);
        var files = Directory.GetFiles(_directory, "SaberTailor.viewer_1-*.json");
        Assert.Single(files);
    }

    [Fact]
    public void Other_channel_point_titles_do_nothing()
    {
        Assert.Equal("", CreateController().Exec("viewer_1", "Something else"));
        Assert.Empty(Directory.GetFiles(_directory).Where(f => f.Contains("SaberTailor")));
    }
}
