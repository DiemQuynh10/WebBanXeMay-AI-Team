using Chatbot.API.Services;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.State;

public class ClarificationStateServiceTests
{
    [Fact]
    public void SetPending_ShouldStoreValue_WhenConversationIdAndMessageAreValid()
    {
        var service = new ClarificationStateService();

        service.SetPending("conv-001", "xe vision giá bao nhiêu");

        var result = service.GetPending("conv-001");

        result.Should().Be("xe vision giá bao nhiêu");
    }

    [Fact]
    public void GetPending_ShouldReturnNull_WhenConversationIdDoesNotExist()
    {
        var service = new ClarificationStateService();

        var result = service.GetPending("conv-not-found");

        result.Should().BeNull();
    }

    [Fact]
    public void Clear_ShouldRemovePendingValue_WhenConversationIdExists()
    {
        var service = new ClarificationStateService();
        service.SetPending("conv-002", "xe air blade còn hàng không");

        service.Clear("conv-002");

        var result = service.GetPending("conv-002");
        result.Should().BeNull();
    }

    [Fact]
    public void SetPending_ShouldDoNothing_WhenConversationIdIsNullOrWhiteSpace()
    {
        var service = new ClarificationStateService();

        service.SetPending("", "abc");
        service.SetPending("   ", "abc");
        service.SetPending(null!, "abc");

        service.GetPending("").Should().BeNull();
        service.GetPending("   ").Should().BeNull();
    }

    [Fact]
    public void SetPending_ShouldDoNothing_WhenNormalizedMessageIsNullOrWhiteSpace()
    {
        var service = new ClarificationStateService();

        service.SetPending("conv-003", "");
        service.SetPending("conv-003", "   ");
        service.SetPending("conv-003", null!);

        var result = service.GetPending("conv-003");
        result.Should().BeNull();
    }

    [Fact]
    public void GetPending_ShouldReturnNull_WhenConversationIdIsNullOrWhiteSpace()
    {
        var service = new ClarificationStateService();

        service.GetPending("").Should().BeNull();
        service.GetPending("   ").Should().BeNull();
        service.GetPending(null!).Should().BeNull();
    }

    [Fact]
    public void Clear_ShouldDoNothing_WhenConversationIdIsNullOrWhiteSpace()
    {
        var service = new ClarificationStateService();

        var action1 = () => service.Clear("");
        var action2 = () => service.Clear("   ");
        var action3 = () => service.Clear(null!);

        action1.Should().NotThrow();
        action2.Should().NotThrow();
        action3.Should().NotThrow();
    }

    [Fact]
    public void SetPending_ShouldOverwriteOldValue_WhenSameConversationIdIsUsedAgain()
    {
        var service = new ClarificationStateService();

        service.SetPending("conv-004", "câu cũ");
        service.SetPending("conv-004", "câu mới");

        var result = service.GetPending("conv-004");

        result.Should().Be("câu mới");
    }
}