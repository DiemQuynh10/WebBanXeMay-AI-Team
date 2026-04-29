using FluentAssertions;
using Chatbot.API.Services;

namespace Chatbot.API.Tests.Services;

public class RagContextPostProcessorTests
{
    [Fact]
    public void Filter_WhenQuestionAsksForDocuments_ShouldPreferPaperworkFacts()
    {
        var ragContext = """
            - domain: installment; slot: documents; value: Hồ sơ trả góp cần CCCD, hộ khẩu và sao kê.
            - domain: installment; slot: deposit; value: Nếu hồ sơ không được ngân hàng duyệt, cửa hàng sẽ hoàn lại 100% tiền cọc.
            - domain: installment; slot: refund; value: Hoàn cọc chỉ áp dụng khi đơn hàng không được duyệt.
            """;

        var filtered = RagContextPostProcessor.Filter(
            ragContext,
            "hồ sơ trả góp cần những gì",
            "hồ sơ trả góp cần những gì",
            null,
            null);

        filtered.Should().Contain("CCCD");
        filtered.Should().Contain("hộ khẩu");
        filtered.Should().NotContain("tiền cọc");
        filtered.Should().NotContain("Hoàn cọc");
    }
}
