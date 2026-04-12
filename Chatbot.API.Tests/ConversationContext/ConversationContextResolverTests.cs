using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Conversation;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.ConversationContext;

public class ConversationContextResolverTests
{
    private readonly ConversationContextResolver _resolver = new();

    [Fact]
    public void Resolve_ShouldReturnStartFreshRecommendation_WhenBudgetRestartWithSameGoalDetected()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 47000000,
            PriceMax = 53000000,
            TargetPrice = 50000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nữ",
            ForWork = true,
            WantsLargeStorage = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "giờ t muốn mua tầm 50 triệu",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.ContextDecision.Should().Be(RecommendationContextDecision.StartFreshRecommendation);
        result.ShouldResetContext.Should().BeTrue();
        result.ShouldPreserveBudgetOnlyContext.Should().BeFalse();

        result.EffectiveIntent.Target.Should().Be("nữ");
        result.EffectiveIntent.ForWork.Should().BeTrue();
        result.EffectiveIntent.WantsLargeStorage.Should().BeTrue();
        result.EffectiveIntent.TargetPrice.Should().Be(50000000);
    }

    [Fact]
    public void Resolve_ShouldKeepExplicitGenderTarget_WhenBudgetRestartContainsExplicitMaleSignal()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 42000000,
            PriceMax = 48000000,
            TargetPrice = 45000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nữ",
            PrefersFemaleStyle = true,
            PrefersMaleStyle = false,
            ForWork = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "không phải nữ nữa, giờ tư vấn xe cho nam 45 triệu",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.ContextDecision.Should().Be(RecommendationContextDecision.StartFreshRecommendation);
        result.ShouldResetContext.Should().BeTrue();

        result.EffectiveIntent.Target.Should().Be("nam");
        result.EffectiveIntent.PrefersMaleStyle.Should().BeTrue();
        result.EffectiveIntent.PrefersFemaleStyle.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ShouldReturnExpandFromCurrentGoal_WhenMessageIsBudgetPivot()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 32000000,
            PriceMax = 38000000,
            TargetPrice = 35000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nữ",
            PrefersFemaleStyle = true,
            PrefersMaleStyle = false,
            ForWork = true,
            WantsLargeStorage = true,
            PreferredBrand = "Honda",
            PreferredCategory = "xe ga",
            HeightCm = 155,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "còn tầm 35 triệu thì sao",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.ContextDecision.Should().Be(RecommendationContextDecision.ExpandFromCurrentGoal);
        result.ShouldResetContext.Should().BeFalse();
        result.ShouldPreserveBudgetOnlyContext.Should().BeFalse();

        result.EffectiveIntent.Target.Should().Be("nữ");
        result.EffectiveIntent.ForWork.Should().BeTrue();
        result.EffectiveIntent.WantsLargeStorage.Should().BeTrue();
        result.EffectiveIntent.PreferredGenderShouldBeFemale();
        result.EffectiveIntent.Brand.Should().Be("Honda");
        result.EffectiveIntent.Category.Should().Be("xe ga");
        result.EffectiveIntent.HeightCm.Should().Be(155);
        result.EffectiveIntent.TargetPrice.Should().Be(35000000);
    }

    [Fact]
    public void Resolve_ShouldPreserveBudgetOnlyContext_WhenCurrentTurnIsOnlyBudgetChange()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 28000000,
            PriceMax = 32000000,
            TargetPrice = 30000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nữ",
            PrefersFemaleStyle = true,
            PrefersMaleStyle = false,
            ForSchool = true,
            WantsEasyControl = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "30 triệu",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.ShouldPreserveBudgetOnlyContext.Should().BeTrue();
        result.EffectiveIntent.Target.Should().Be("nữ");
        result.EffectiveIntent.PrefersFemaleStyle.Should().BeTrue();
        result.EffectiveIntent.PrefersMaleStyle.Should().BeFalse();
        result.EffectiveIntent.TargetPrice.Should().Be(30000000);
        result.EffectiveIntent.PriceMin.Should().Be(28000000);
        result.EffectiveIntent.PriceMax.Should().Be(32000000);
    }

    [Fact]
    public void Resolve_ShouldNotPreserveBudgetOnlyContext_WhenMessageIntroducesExplicitNewUseCase()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 28000000,
            PriceMax = 32000000,
            TargetPrice = 30000000,
            FilterType = PriceFilterType.Around,
            ForWork = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nữ",
            ForSchool = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "xe 30 triệu đi làm",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.ShouldPreserveBudgetOnlyContext.Should().BeFalse();
        result.EffectiveIntent.ForWork.Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldRemoveSelectedBrandFromExcludedBrands_WhenBrandIsExplicitlyChosen()
    {
        var parsedIntent = new ParsedIntent
        {
            Brand = "Honda",
            ExcludedBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Honda",
                "Yamaha"
            }
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "còn honda thì sao",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.EffectiveIntent.ExcludedBrands.Should().NotContain("Honda");
        result.EffectiveIntent.ExcludedBrands.Should().Contain("Yamaha");
    }

    [Fact]
    public void Resolve_ShouldRemoveSelectedCategoryFromExcludedCategories_WhenCategoryIsExplicitlyChosen()
    {
        var parsedIntent = new ParsedIntent
        {
            Category = "xe ga",
            ExcludedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "xe ga",
                "xe số"
            }
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" },
            BaseRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = _resolver.Resolve(
            "xe ga nhé",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.EffectiveIntent.ExcludedCategories.Should().NotContain("xe ga");
        result.EffectiveIntent.ExcludedCategories.Should().Contain("xe số");
    }

    [Fact]
    public void Resolve_ShouldMergeExistingProfileCoreData_WhenDecisionIsExpandFromCurrentGoal()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 42000000,
            PriceMax = 48000000,
            TargetPrice = 45000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nam",
            PrefersMaleStyle = true,
            ForWork = true,
            WantsFuelSaving = true,
            NeedsLowSeat = true,
            PreferredBrand = "Honda",
            PreferredCategory = "xe ga",
            HeightCm = 168,
            ExcludedBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SYM" },
            ExcludedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xe số" },
            RequestedStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thể thao" },
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" },
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _resolver.Resolve(
            "còn tầm 45 triệu thì sao",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.ContextDecision.Should().Be(RecommendationContextDecision.ExpandFromCurrentGoal);

        result.EffectiveIntent.Target.Should().Be("nam");
        result.EffectiveIntent.PrefersMaleStyle.Should().BeTrue();
        result.EffectiveIntent.ForWork.Should().BeTrue();
        result.EffectiveIntent.WantsFuelSaving.Should().BeTrue();
        result.EffectiveIntent.NeedsLowSeat.Should().BeTrue();
        result.EffectiveIntent.Brand.Should().Be("Honda");
        result.EffectiveIntent.Category.Should().Be("xe ga");
        result.EffectiveIntent.HeightCm.Should().Be(168);
        result.EffectiveIntent.TargetPrice.Should().Be(45000000);
    }
    [Fact]
    public void Resolve_ShouldReturnNoneAndResetFalse_WhenNoSpecialContextRuleApplies()
    {
        var parsedIntent = new ParsedIntent
        {
            IntentType = "unknown"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = false,
            LastRecommendedProducts = new List<string>(),
            BaseRecommendedProducts = new List<string>()
        };

        var result = _resolver.Resolve(
            "xin chào",
            parsedIntent,
            profile,
            ChatFlowType.Greeting);

        result.ContextDecision.Should().Be(RecommendationContextDecision.None);
        result.ShouldResetContext.Should().BeFalse();
        result.ShouldPreserveBudgetOnlyContext.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ShouldUseExplicitFemaleTarget_WhenMessageClearlyMentionsFemale()
    {
        var parsedIntent = new ParsedIntent
        {
            PriceMin = 28000000,
            PriceMax = 32000000,
            TargetPrice = 30000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            Target = "nam",
            PrefersMaleStyle = true,
            PrefersFemaleStyle = false,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" },
            BaseRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = _resolver.Resolve(
            "tư vấn xe cho nữ 30 triệu",
            parsedIntent,
            profile,
            ChatFlowType.Recommendation);

        result.EffectiveIntent.Target.Should().Be("nữ");
        result.EffectiveIntent.PrefersFemaleStyle.Should().BeTrue();
        result.EffectiveIntent.PrefersMaleStyle.Should().BeFalse();
    }
}

internal static class ParsedIntentAssertionExtensions
{
    public static void PreferredGenderShouldBeFemale(this ParsedIntent intent)
    {
        intent.PrefersFemaleStyle.Should().BeTrue();
        intent.PrefersMaleStyle.Should().BeFalse();
    }
}