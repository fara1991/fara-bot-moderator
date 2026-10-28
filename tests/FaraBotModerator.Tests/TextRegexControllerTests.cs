using System;
using System.IO;
using System.Linq;
using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// BeatSaber の読み上げ用の設定（bsr.json）の読み込みと変換のテスト。
/// 壊れたファイルや不正な正規表現で、利用者の設定を消さないことを確かめる。
/// </summary>
public class TextRegexControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        AppContext.BaseDirectory, "TestTemp", Guid.NewGuid().ToString("N"));

    public TextRegexControllerTests()
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
    public void ConvertText_skips_an_invalid_pattern_and_uses_the_next_line()
    {
        var model = new TextRegexModel
        {
            BeatSaberChat =
            [
                new BeatSaberChatModel("(?<=!bsr )(.*", "壊れた行 {0}"),          // 閉じ括弧が無い（不正）
                new BeatSaberChatModel("(?<=!bsr ).*", "リクエスト {0} です")
            ]
        };

        var result = TextRegexController.ConvertText("!bsr abc", model);

        Assert.Equal("リクエスト abc です", result);
    }

    [Fact]
    public void ConvertText_does_not_replace_a_placeholder_that_came_from_the_chat_text()
    {
        // チャット文に "{1}" が入っていても、次の一致の文で書き換わらない（1 回の走査で置き換える）
        var model = new TextRegexModel
        {
            BeatSaberChat = [new BeatSaberChatModel(@"\S+", "{0}-{1}")]
        };

        Assert.Equal("{1}-b", TextRegexController.ConvertText("{1} b", model));
    }

    [Fact]
    public void ConvertText_keeps_dollar_signs_in_the_chat_text_as_they_are()
    {
        var model = new TextRegexModel
        {
            BeatSaberChat = [new BeatSaberChatModel(".*", "[{0}]")]
        };

        Assert.Equal("[$1 $$]", TextRegexController.ConvertText("$1 $$", model));
    }

    [Fact]
    public void ConvertText_returns_the_input_when_no_line_matches()
    {
        var model = new TextRegexModel
        {
            BeatSaberChat = [new BeatSaberChatModel("^zzz$", "x")]
        };

        Assert.Equal("hello", TextRegexController.ConvertText("hello", model));
    }

    [Fact]
    public void LoadBsrChatModel_creates_the_default_file_when_missing()
    {
        var path = Path.Combine(_directory, "bsr.json");

        var model = TextRegexController.LoadBsrChatModel(path);

        Assert.True(File.Exists(path));
        Assert.NotEmpty(model.BeatSaberChat);
    }

    [Fact]
    public void LoadBsrChatModel_moves_a_broken_file_aside_instead_of_deleting_it()
    {
        var path = Path.Combine(_directory, "bsr.json");
        const string broken = "{ this is not json";
        File.WriteAllText(path, broken);

        var model = TextRegexController.LoadBsrChatModel(path);

        // 既定の設定が返る
        Assert.NotEmpty(model.BeatSaberChat);
        // 元のファイルは消さず、改名して残す
        var kept = Directory.GetFiles(_directory, "bsr.broken-*.json");
        Assert.Single(kept);
        Assert.Equal(broken, File.ReadAllText(kept[0]));
    }

    [Fact]
    public void LoadBsrChatModel_keeps_a_valid_user_file_as_is()
    {
        var path = Path.Combine(_directory, "bsr.json");
        File.WriteAllText(path, "{\"bsrChat\":[{\"key\":\"^a$\",\"value\":\"b\"}]}");

        var model = TextRegexController.LoadBsrChatModel(path);

        Assert.Single(model.BeatSaberChat);
        Assert.Equal("^a$", model.BeatSaberChat.Single().Key);
        Assert.Empty(Directory.GetFiles(_directory, "*.broken-*"));
    }
}
