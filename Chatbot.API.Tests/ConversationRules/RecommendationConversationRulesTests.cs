using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Conversation;
using FluentAssertions;
using Xunit;

namespace Chatbot.API.Tests.ConversationRules;

public class RecommendationConversationRulesTests
{
    [Theory]
    [InlineData("còn tầm 35 triệu thì sao")]
    [InlineData("còn khoảng 30 triệu thì sao")]
    [InlineData("thế tầm 40 triệu thì sao")]
    public void LooksLikeBudgetPivotFollowUp_ShouldReturnTrue_ForBudgetPivotMessages(string message)
    {
        var result = RecommendationConversationRules.LooksLikeBudgetPivotFollowUp(message);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("tư vấn xe 45 triệu")]
    [InlineData("xe 30 triệu")]
    [InlineData("mua xe 40 triệu")]
    public void LooksLikeRecommendationFollowUp_ShouldReturnFalse_ForFreshBudgetConsultation(string message)
    {
        var intent = new ParsedIntent
        {
            PriceMin = 42000000,
            PriceMax = 48000000,
            TargetPrice = 45000000,
            FilterType = PriceFilterType.Around
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, intent, profile);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("cốp rộng hơn")]
    [InlineData("rẻ hơn chút")]
    [InlineData("còn honda thì sao")]
    public void LooksLikeRecommendationFollowUp_ShouldReturnTrue_ForShortFollowUpMessages(string message)
    {
        var intent = new ParsedIntent
        {
            IsFollowUp = true,
            FollowUpType = "refine",
            WantsLargeStorage = message.Contains("cốp rộng")
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, intent, profile);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("mẫu nào ổn hơn")]
    [InlineData("xe nào hợp hơn")]
    [InlineData("con nào đẹp hơn")]
    public void LooksLikeRecommendationFollowUp_ShouldReturnTrue_ForComparisonStyleFollowUps(string message)
    {
        var intent = new ParsedIntent
        {
            IsFollowUp = true,
            FollowUpType = "refine",
            ComparisonFeature = "general"
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, intent, profile);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("vision giá bao nhiêu")]
    [InlineData("air blade còn hàng không")]
    [InlineData("latte có mấy màu")]
    public void LooksLikeRecommendationFollowUp_ShouldReturnFalse_ForSpecificProductQuestions(string message)
    {
        var intent = new ParsedIntent
        {
            IsFollowUp = false,
            MentionedProducts = new List<string> { "Honda Vision" }
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, intent, profile);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("tư vấn xe cho nữ")]
    [InlineData("gợi ý xe tay ga")]
    [InlineData("mua xe đi làm tầm 40 triệu")]
    public void LooksLikeRecommendationFollowUp_ShouldReturnFalse_ForFreshRecommendationRequests(string message)
    {
        var intent = new ParsedIntent
        {
            IsFollowUp = false,
            Target = message.Contains("nữ") ? "nữ" : null,
            ForWork = message.Contains("đi làm"),
            PriceMin = message.Contains("40 triệu") ? 37000000 : null,
            PriceMax = message.Contains("40 triệu") ? 43000000 : null,
            TargetPrice = message.Contains("40 triệu") ? 40000000 : null,
            FilterType = message.Contains("40 triệu") ? PriceFilterType.Around : PriceFilterType.None
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Air Blade", "Yamaha Grande" }
        };

        var result = RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, intent, profile);

        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeRecommendationFollowUp_ShouldReturnFalse_WhenNoRecommendationContextExists()
    {
        var intent = new ParsedIntent
        {
            IsFollowUp = true,
            FollowUpType = "refine",
            WantsLargeStorage = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = false,
            LastRecommendedProducts = new List<string>()
        };

        var result = RecommendationConversationRules.LooksLikeRecommendationFollowUp(
            "cốp rộng hơn",
            intent,
            profile);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("giờ t muốn xe cho nam 50 triệu")]
    [InlineData("đổi ý, tư vấn xe cho nữ khoảng 30 triệu")]
    [InlineData("không phải xe nữ nữa, giờ xe nam 45 triệu")]
    public void LooksLikeFreshRecommendationRestart_ShouldReturnTrue_ForFreshRestartMessages(string message)
    {
        var intent = new ParsedIntent
        {
            Target = message.Contains("nam") ? "nam" : "nữ",
            PriceMin = 28000000,
            PriceMax = 52000000,
            TargetPrice = 45000000,
            FilterType = PriceFilterType.Around
        };

        var result = RecommendationConversationRules.LooksLikeFreshRecommendationRestart(message, intent);

        result.Should().BeTrue();
    }

    [Fact]
    public void LooksLikeFreshRecommendationRestart_ShouldReturnFalse_WhenMessageIsOnlyBudgetChange()
    {
        var intent = new ParsedIntent
        {
            PriceMin = 32000000,
            PriceMax = 38000000,
            TargetPrice = 35000000,
            FilterType = PriceFilterType.Around
        };

        var result = RecommendationConversationRules.LooksLikeFreshRecommendationRestart(
            "giờ t muốn 35 triệu",
            intent);

        result.Should().BeFalse();
    }

    [Fact]
    public void LooksLikeAlternativeRequestAfterRejection_ShouldReturnTrue_WhenUserRejectsCurrentSuggestionAndAsksAlternative()
    {
        var intent = new ParsedIntent
        {
            IsFollowUp = true,
            FollowUpType = "refine",
            HasExpandRecommendationSignal = true
        };

        var profile = new CustomerPreferenceProfile
        {
            HasActiveRecommendationContext = true,
            LastRecommendedProducts = new List<string> { "Honda Vision", "Yamaha Latte" }
        };

        var result = RecommendationConversationRules.LooksLikeAlternativeRequestAfterRejection(
            "không mua xe đó, gợi ý xe khác đi",
            intent,
            profile);

        result.Should().BeTrue();
    }
}