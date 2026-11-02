using FaraBotModerator.Controllers;
using FaraBotModerator.Models;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// 設定の正規表現が時間内に終わらないとき、その行だけ無視してチャットの処理を止めないこと。
/// </summary>
public class TextRegexTimeoutTests
{
    [Fact]
    public void A_pattern_that_takes_too_long_is_skipped_and_the_next_line_is_used()
    {
        var model = new TextRegexModel
        {
            BeatSaberChat =
            [
                // 入れ子の繰り返しは、一致しない入力で非常に時間がかかる（時間切れで、この行は無視する）
                new BeatSaberChatModel("^(a+)+$", "時間切れの行 {0}"),
                new BeatSaberChatModel("!$", "次の行 {0}")
            ]
        };

        var result = TextRegexController.ConvertText(new string('a', 30) + "!", model);

        Assert.Equal("次の行 !", result);
    }
}
