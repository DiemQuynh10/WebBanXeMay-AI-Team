using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Conversation;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.ConversationContext;

public class RecommendationContextRulesTests
{
    [Fact]
    public void DecideRecommendationContextAction_ShouldReturnExpand_WhenMessageIsBudgetPivot()
    {
        var intent = new ParsedIntent
        {
            IntentType = "refine",
            PriceMin = 32000000,
            PriceMax = 38000000,
            TargetPrice = 35000000,
            FilterType = PriceFilterType.Around,
            WantsLargeStorage = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" },
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationContextRules.DecideRecommendationContextAction(
            "còn tầm 35 triệu thì sao",
            intent,
            profile,
            ChatFlowType.Recommendation);

        result.Should().Be(RecommendationContextDecision.ExpandFromCurrentGoal);
    }

    [Fact]
    public void DecideRecommendationContextAction_ShouldReturnNarrow_WhenMessageIsAttributeRefinement()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            IsFollowUp = true,
            WantsLargeStorage = true,
            HasNarrowRefinementSignal = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" },
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationContextRules.DecideRecommendationContextAction(
            "cốp rộng hơn",
            intent,
            profile,
            ChatFlowType.Recommendation);

        result.Should().Be(RecommendationContextDecision.NarrowWithinCurrentSet);
    }

    [Fact]
    public void DecideRecommendationContextAction_ShouldReturnExpand_WhenMessageAsksAlternativeChoice()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            IsFollowUp = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" },
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationContextRules.DecideRecommendationContextAction(
            "loại khác",
            intent,
            profile,
            ChatFlowType.Recommendation);

        result.Should().Be(RecommendationContextDecision.ExpandFromCurrentGoal);
    }

    [Fact]
    public void DecideRecommendationContextAction_ShouldReturnNone_WhenNoRecommendationContextExists()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            IsFollowUp = true,
            WantsLargeStorage = true,
            HasNarrowRefinementSignal = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = false,
            LastRecommendedProducts = new List<string>(),
            BaseRecommendedProducts = new List<string>()
        };

        var result = RecommendationContextRules.DecideRecommendationContextAction(
            "cốp rộng hơn",
            intent,
            profile,
            ChatFlowType.Recommendation);

        result.Should().Be(RecommendationContextDecision.None);
    }

    [Fact]
    public void DecideRecommendationContextAction_ShouldReturnStartFreshRecommendation_WhenMessageStartsNewRecommendationGoal()
    {
        var intent = new ParsedIntent
        {
            IntentType = "recommend",
            PriceMin = 47000000,
            PriceMax = 53000000,
            TargetPrice = 50000000,
            FilterType = PriceFilterType.Around,
            Target = "nam"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = RecommendationContextRules.DecideRecommendationContextAction(
            "tư vấn xe cho nam tầm 50 triệu",
            intent,
            profile,
            ChatFlowType.Recommendation);

        result.Should().Be(RecommendationContextDecision.StartFreshRecommendation);
    }

    [Fact]
    public void DecideRecommendationContextAction_ShouldReturnStartFreshRecommendation_WhenPreviousFlowWasProductLookupAndMessageLooksLikeFreshConsultation()
    {
        var intent = new ParsedIntent
        {
            IntentType = "recommend",
            PriceMin = 28000000,
            PriceMax = 32000000,
            TargetPrice = 30000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = RecommendationContextRules.DecideRecommendationContextAction(
            "xe 30 triệu",
            intent,
            profile,
            ChatFlowType.ProductLookup);

        result.Should().Be(RecommendationContextDecision.StartFreshRecommendation);
    }

    [Fact]
    public void ShouldResetContextForFreshConsultation_ShouldReturnTrue_WhenFreshRestartDetected()
    {
        var intent = new ParsedIntent
        {
            IntentType = "recommend",
            Target = "nam",
            PriceMin = 45000000,
            PriceMax = 55000000,
            TargetPrice = 50000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            ConversationId = "conv-001",
            TurnCount = 5,
            HasActiveRecommendationContext = true,
            PreferredBrand = "Honda",
            TargetPrice = 35000000,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = RecommendationContextRules.ShouldResetContextForFreshConsultation(
            "giờ t muốn xe cho nam 50 triệu",
            intent,
            profile);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldResetContextForFreshConsultation_ShouldReturnFalse_WhenMessageIsRecommendationFollowUp()
    {
        var intent = new ParsedIntent
        {
            IntentType = "followup",
            IsFollowUp = true,
            WantsLargeStorage = true
        };

        var profile = new CustomerPreferenceProfile
        {
            ConversationId = "conv-002",
            TurnCount = 3,
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };
         
        var result = RecommendationContextRules.ShouldResetContextForFreshConsultation(
            "cốp rộng hơn",
            intent,
            profile);

        result.Should().BeFalse();
    }
}