using Chatbot.API.Helpers;
using FluentAssertions;

namespace Chatbot.API.Tests.Compare;

public class ChatChannelMessageHelperTests
{
    [Fact]
    public void FormatTelegramReply_ShouldRenderCompareReplyAsHtmlTable_WhenReplyContainsTwoCompareRows()
    {
        const string reply =
            "Mình so sánh nhanh **Honda Vision** và **Yamaha Latte** cho bạn:\n\n" +
            "- **Honda Vision**: giá 31,000,000 VNĐ, còn 9 chiếc, thuộc nhóm Tay Ga.\n" +
            "- **Yamaha Latte**: giá 42,000,000 VNĐ, còn 8 chiếc, thuộc nhóm Tay Ga.\n\n" +
            "Nếu bạn ưu tiên **giá mềm hơn** thì **Honda Vision** lợi thế hơn.";

        var formatted = ChatChannelMessageHelper.FormatTelegramReply(reply, "fallback");

        formatted.Should().Contain("<pre>");
        formatted.Should().Contain("Mau xe");
        formatted.Should().Contain("Honda Vision");
        formatted.Should().Contain("Yamaha Latte");
        formatted.Should().Contain("<b>Honda Vision</b>");
        formatted.Should().Contain("<b>gi&#225; mềm hơn</b>");
    }

    [Fact]
    public void FormatTelegramReply_ShouldKeepNormalMarkdownBehavior_WhenReplyIsNotCompare()
    {
        const string reply = "Mình gợi ý **Honda Vision** cho bạn.";

        var formatted = ChatChannelMessageHelper.FormatTelegramReply(reply, "fallback");

        formatted.Should().NotContain("<pre>");
        formatted.Should().Contain("<b>Honda Vision</b>");
    }

    [Fact]
    public void FormatTelegramReply_ShouldRenderDetailedCompareColumns_WhenRowContainsBrandAndCc()
    {
        const string reply =
            "Mình so sánh nhanh **Honda Vision** và **Yamaha Janus** cho bạn:\n\n" +
            "- **Honda Vision**: giá 36,000,000 VNĐ, còn 12 chiếc, hãng Honda, loại Xe ga, 110cc.\n" +
            "- **Yamaha Janus**: giá 38,000,000 VNĐ, còn 8 chiếc, hãng Yamaha, loại Xe ga, 125cc.";

        var formatted = ChatChannelMessageHelper.FormatTelegramReply(reply, "fallback");

        formatted.Should().Contain("<pre>");
        formatted.Should().Contain("Hang");
        formatted.Should().Contain("CC");
        formatted.Should().Contain("Honda");
        formatted.Should().Contain("Yamaha");
    }
}
