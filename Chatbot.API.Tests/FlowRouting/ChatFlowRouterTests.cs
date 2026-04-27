using Chatbot.API.Models.Intent;
using Chatbot.API.Services;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.FlowRouting;

public class ChatFlowRouterTests
{
    private readonly ChatFlowRouter _router = new();

    [Fact]
    public void Route_ShouldReturnGreeting_WhenIntentIsGreeting()
    {
        var intent = new ParsedIntent
        {
            IsGreeting = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("xin chào", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Greeting);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Greeting detected");
    }

    [Fact]
    public void Route_ShouldReturnOutOfScope_WhenIntentIsOutOfScope()
    {
        var intent = new ParsedIntent
        {
            IsOutOfScope = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("thời tiết hôm nay thế nào", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.OutOfScope);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Out-of-scope detected");
    }

    [Fact]
    public void Route_ShouldReturnOrderLookup_WhenIntentIsOrderLookup()
    {
        var intent = new ParsedIntent
        {
            IsOrderLookup = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("kiểm tra đơn hàng", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.OrderLookup);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Order lookup detected");
    }

    [Fact]
    public void Route_ShouldReturnCompare_WhenIntentIsDirectCompare()
    {
        var intent = new ParsedIntent
        {
            IsDirectCompare = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("vision với latte cái nào tốt hơn", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Compare);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Direct compare detected");
    }

    [Fact]
    public void Route_ShouldReturnProductLookup_WhenIntentIsDirectProductLookup()
    {
        var intent = new ParsedIntent
        {
            IsDirectProductLookup = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("vision giá bao nhiêu", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.ProductLookup);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Direct product lookup detected");
    }

    [Fact]
    public void Route_ShouldReturnProductLookup_WhenLookupFieldExistsAndPreviousLookupProductExists()
    {
        var intent = new ParsedIntent
        {
            LookupField = "price"
        };

        var profile = new CustomerPreferenceProfile
        {
            LastLookupProductName = "Honda Vision"
        };

        var result = _router.Route("giá bao nhiêu", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.ProductLookup);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Lookup follow-up detected from previous product context");
    }

    [Fact]
    public void Route_ShouldReturnCompare_WhenIntentIsFollowUpAndHasActiveCompareContext()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveCompareContext = true,
            LastComparedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _router.Route("cốp rộng hơn thì sao", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Compare);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Compare follow-up detected");
    }

    [Fact]
    public void Route_ShouldReturnRefinement_WhenIntentIsFollowUpAndHasHardRefinementSignals()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            WantsLargeStorage = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _router.Route("cốp rộng hơn", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Recommendation follow-up with hard refinement signals");
    }

    [Fact]
    public void Route_ShouldReturnRefinement_WhenIntentIsFollowUpAndAsksAlternativeChoice()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            RawMessage = "loại khác"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _router.Route("loại khác", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Recommendation follow-up with hard refinement signals");
    }

    [Fact]
    public void Route_ShouldReturnRecommendationFollowUp_WhenIntentIsFollowUpAndOnlyHasRecommendationContext()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _router.Route("mẫu nào ổn hơn", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.RecommendationFollowUp);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Recommendation follow-up detected");
    }

    [Fact]
    public void Route_ShouldReturnCompare_WhenIntentIsRefineAndCompareContextOverrides()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine",
            ComparisonFeature = "storage"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveCompareContext = true,
            LastComparedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _router.Route("cốp rộng hơn", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Compare);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Compare context overrides refine");
    }

    [Fact]
    public void Route_ShouldReturnRefinement_WhenIntentTypeIsRefine()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine",
            WantsFuelSaving = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("tiết kiệm xăng hơn", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Refinement detected");
    }

    [Fact]
    public void Route_ShouldReturnBrandSwitch_WhenIntentIsBrandSwitch()
    {
        var intent = new ParsedIntent
        {
            IsBrandSwitch = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("còn honda thì sao", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.BrandSwitch);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Brand switch detected");
    }

    [Fact]
    public void Route_ShouldReturnRefinement_WhenRecommendationContextExistsAndIntentTypeIsRefine()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true
        };

        var result = _router.Route("lọc lại giúp mình", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Refinement detected");
    }

    [Fact]
    public void Route_ShouldReturnRefinement_WhenRecommendationContextExistsAndMessageLooksLikeFilterFragment()
    {
        var intent = new ParsedIntent
        {
            PriceMax = 35000000
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _router.Route("dưới 35 triệu", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Recommendation context + filter fragment detected");
    }

    [Fact]
    public void Route_ShouldReturnRefinement_WhenRecommendationContextExistsAndHasRefinementSignals()
    {
        var intent = new ParsedIntent
        {
            WantsFuelSaving = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _router.Route("tiết kiệm xăng hơn", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Refinement);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Contain("Recommendation context");
    }

    [Fact]
    public void Route_ShouldReturnProductSearch_WhenIntentIsProductSearch()
    {
        var intent = new ParsedIntent
        {
            IsProductSearch = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("xe dưới 35 triệu", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.ProductSearch);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Product search detected");
    }

    [Fact]
    public void Route_ShouldReturnProductSearch_WhenOnlyPriceSignalExistsAndNoRecommendationIntent()
    {
        var intent = new ParsedIntent
        {
            PriceMin = 30000000,
            PriceMax = 35000000
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("30 đến 35 triệu", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.ProductSearch);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Price-only product search detected");
    }

    [Fact]
    public void Route_ShouldReturnRecommendation_WhenIntentIsOpenRecommendation()
    {
        var intent = new ParsedIntent
        {
            IsOpenRecommendation = true
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("tư vấn xe cho nữ tầm 30 triệu", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Recommendation);
        result.ShouldUseDeterministicFlow.Should().BeTrue();
        result.ShouldUseRag.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeFalse();
        result.Reason.Should().Be("Open recommendation detected");
    }

    [Fact]
    public void Route_ShouldReturnUnknownFallback_WhenMessageIsFinancingKnowledgeQuestion()
    {
        var intent = new ParsedIntent
        {
            IsOpenRecommendation = true,
            PriceMax = 35000000
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route(
            "tôi đang có 10 triệu muốn mua xe khoảng 35 triệu có được trả góp 0% không",
            intent,
            profile);

        result.FlowType.Should().Be(ChatFlowType.Unknown);
        result.ShouldUseDeterministicFlow.Should().BeFalse();
        result.ShouldUseRag.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeTrue();
        result.Reason.Should().Be("Static knowledge question detected");
    }

    [Fact]
    public void Route_ShouldReturnUnknownFallback_WhenRecommendationContextExistsButMessageIsStaticKnowledge()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            IsFollowUp = true,
            PriceMax = 35000000
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _router.Route(
            "mình có 10 triệu muốn mua xe 35 triệu, thủ tục trả góp 0% như nào",
            intent,
            profile);

        result.FlowType.Should().Be(ChatFlowType.Unknown);
        result.ShouldUseDeterministicFlow.Should().BeFalse();
        result.ShouldUseRag.Should().BeTrue();
        result.ShouldUseAiFallback.Should().BeTrue();
        result.Reason.Should().Be("Static knowledge question detected");
    }

    [Fact]
    public void Route_ShouldReturnUnknown_WhenNoRuleMatches()
    {
        var intent = new ParsedIntent
        {
            IntentType = "unknown"
        };

        var profile = new CustomerPreferenceProfile();

        var result = _router.Route("abc xyz", intent, profile);

        result.FlowType.Should().Be(ChatFlowType.Unknown);
        result.ShouldUseDeterministicFlow.Should().BeFalse();
        result.ShouldUseRag.Should().BeFalse();
        result.ShouldUseAiFallback.Should().BeTrue();
        result.Reason.Should().Be("Fallback");
    }
}