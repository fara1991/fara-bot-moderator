using FaraBotModerator.Controllers;
using Xunit;

namespace FaraBotModerator.Tests;

/// <summary>
/// Bits のメッセージのテンプレートの置き換え。
/// </summary>
public class BitsMessageTests
{
    private const string Template = "{bitsSendUserName}, thanks {bitsAmount} bits (total {totalBitsAmount}).";

    [Fact]
    public void The_total_is_filled_in_when_it_is_known()
    {
        Assert.Equal("Fara, thanks 100 bits (total 1500).", TwitchClientController.BuildBitsMessage(Template, "Fara", 100, 1500));
    }

    [Fact]
    public void An_unknown_total_is_shown_as_a_dash_instead_of_the_raw_placeholder()
    {
        var message = TwitchClientController.BuildBitsMessage(Template, "Fara", 100, null);

        Assert.Equal("Fara, thanks 100 bits (total -).", message);
        Assert.DoesNotContain("{", message);
    }

    [Fact]
    public void A_template_without_the_total_is_unchanged_apart_from_the_other_values()
    {
        Assert.Equal("Fara cheered 50", TwitchClientController.BuildBitsMessage("{bitsSendUserName} cheered {bitsAmount}", "Fara", 50, 999));
    }
}
