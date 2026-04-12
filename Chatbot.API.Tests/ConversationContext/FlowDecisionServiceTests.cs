using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Conversation;
using Xunit;

public class FlowDecisionServiceTests
{
    [Fact]
    public void ResolveFinalRouting_ReturnsProductLookup_ForLookupFollowUp()
    {
        var service = new FlowDecisionService();

        var intent = new ParsedIntent();
        var profile = new CustomerPreferenceProfile
        {
            LastLookupProductId = 1
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.ProductSearch
        };

        var result = service.ResolveFinalRouting(
            "còn hàng không",
            intent,
            profile,
            RecommendationContextDecision.None,
            baseRouting);

        Assert.Equal(ChatFlowType.ProductLookup, result.FlowType);
    }

    [Fact]
    public void ResolveFinalRouting_ReturnsCompare_ForCompareFollowUpPriceQuestion()
    {
        var service = new FlowDecisionService();

        var intent = new ParsedIntent();

        var profile = new CustomerPreferenceProfile
        {
            HasActiveCompareContext = true,
            LastComparedProducts = { "Honda Vision", "Yamaha Latte" }
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.ProductLookup
        };

        var result = service.ResolveFinalRouting(
            "giá bao nhiêu",
            intent,
            profile,
            RecommendationContextDecision.None,
            baseRouting);

        Assert.Equal(ChatFlowType.Compare, result.FlowType);
    }
}