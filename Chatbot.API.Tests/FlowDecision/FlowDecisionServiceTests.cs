using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Conversation;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.FlowDecision;

public class FlowDecisionServiceTests
{
    private readonly FlowDecisionService _service = new();

    [Fact]
    public void ResolveFinalRouting_ShouldKeepCompare_WhenCurrentMessageNamesTwoProducts()
    {
        var intent = new ParsedIntent
        {
            IntentType = "compare",
            RouteFlow = ChatFlowType.Compare,
            IsDirectCompare = true,
            MentionedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var baseRouting = new FlowRoutingResult { FlowType = ChatFlowType.Compare };

        var result = _service.ResolveFinalRouting(
            "vision với latte cái nào hợp hơn",
            intent,
            profile,
            RecommendationContextDecision.NarrowWithinCurrentSet,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.Compare);
    }

    [Fact]
    public void ResolveFinalRouting_ShouldPreferRefinement_WhenRecommendationContextAndNoExplicitTwoProducts()
    {
        var intent = new ParsedIntent
        {
            IntentType = "compare",
            RouteFlow = ChatFlowType.Compare,
            IsDirectCompare = true,
            MentionedProducts = new List<string>()
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var baseRouting = new FlowRoutingResult { FlowType = ChatFlowType.Compare };

        var result = _service.ResolveFinalRouting(
            "cốp rộng hơn",
            intent,
            profile,
            RecommendationContextDecision.NarrowWithinCurrentSet,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
    }

    [Fact]
    public void ResolveFinalRouting_ShouldReturnRecommendation_WhenContextDecisionIsExpand()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var baseRouting = new FlowRoutingResult { FlowType = ChatFlowType.Refinement };

        var result = _service.ResolveFinalRouting(
            "còn tầm 35 triệu thì sao",
            intent,
            profile,
            RecommendationContextDecision.ExpandFromCurrentGoal,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.Recommendation);
    }

    [Fact]
    public void ResolveFinalRouting_ShouldReturnRecommendation_WhenContextDecisionIsStartFreshRecommendation()
    {
        var intent = new ParsedIntent
        {
            IntentType = "recommend",
            RouteFlow = ChatFlowType.Recommendation
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.ProductSearch
        };

        var result = _service.ResolveFinalRouting(
            "giờ t muốn xe cho nam 50 triệu",
            intent,
            profile,
            RecommendationContextDecision.StartFreshRecommendation,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.Recommendation);
        result.ShouldUseRag.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
    }

    [Fact]
    public void ResolveFinalRouting_ShouldReturnRefinement_WhenContextDecisionIsNarrowWithinCurrentSet()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine",
            HasNarrowRefinementSignal = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.RecommendationFollowUp
        };

        var result = _service.ResolveFinalRouting(
            "cốp rộng hơn",
            intent,
            profile,
            RecommendationContextDecision.NarrowWithinCurrentSet,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseAiFallback.Should().BeFalse();
    }

    [Fact]
    public void ResolveFinalRouting_ShouldKeepProductLookup_WhenIntentIsDirectProductLookup()
    {
        var intent = new ParsedIntent
        {
            IntentType = "product_lookup",
            IsDirectProductLookup = true,
            RouteFlow = ChatFlowType.ProductLookup,
            MentionedProducts = new List<string> { "Honda Vision" }
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.Unknown
        };

        var result = _service.ResolveFinalRouting(
            "vision giá bao nhiêu",
            intent,
            profile,
            RecommendationContextDecision.None,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.ProductLookup);
        result.ShouldUseAiFallback.Should().BeFalse();
    }

    [Fact]
    public void ResolveFinalRouting_ShouldKeepProductLookup_WhenLookupFollowUpExists()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            LookupField = "stock"
        };

        var profile = new CustomerPreferenceProfile
        {
            LastLookupProductId = 101,
            LastLookupProductName = "Honda Vision"
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.Unknown
        };

        var result = _service.ResolveFinalRouting(
            "còn hàng không",
            intent,
            profile,
            RecommendationContextDecision.None,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.ProductLookup);
        result.Reason.Should().Be("Forced by lookup follow-up");
    }

    [Fact]
    public void ResolveFinalRouting_ShouldForceRecommendationFollowUp_WhenExistingRecommendationContextAndRefineIntent()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine",
            RouteFlow = ChatFlowType.Unknown
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.Unknown
        };

        var result = _service.ResolveFinalRouting(
            "rẻ hơn chút",
            intent,
            profile,
            RecommendationContextDecision.None,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.RecommendationFollowUp);
        result.ShouldUseAiFallback.Should().BeFalse();
    }

    [Fact]
    public void ResolveFinalRouting_ShouldKeepBaseRouting_WhenNoForcedDecisionApplies()
    {
        var intent = new ParsedIntent
        {
            IntentType = "unknown",
            RouteFlow = ChatFlowType.Unknown
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = false
        };

        var baseRouting = new FlowRoutingResult
        {
            FlowType = ChatFlowType.ProductSearch,
            ShouldUseDeterministicFlow = true,
            ShouldUseAiFallback = false,
            Reason = "Base routing result"
        };

        var result = _service.ResolveFinalRouting(
            "xe dưới 35 triệu",
            intent,
            profile,
            RecommendationContextDecision.None,
            baseRouting);

        result.FlowType.Should().Be(ChatFlowType.ProductSearch);
        result.Reason.Should().Be("Base routing result");
    }
}