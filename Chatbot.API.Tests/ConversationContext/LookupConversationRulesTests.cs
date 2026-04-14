using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Conversation;
using Xunit;

public class LookupConversationRulesTests
{
    [Fact]
    public void LooksLikeDirectProductLookup_ReturnsTrue_ForPriceQuestionWithKnownModel()
    {
        var intent = new ParsedIntent();
        intent.MentionedProducts.Add("Honda Vision");

        var result = LookupConversationRules.LooksLikeDirectProductLookup(
            "vision giá bao nhiêu",
            intent);

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeLookupFollowUp_ReturnsTrue_ForConHangKhong()
    {
        var result = LookupConversationRules.LooksLikeLookupFollowUp("còn hàng không");

        Assert.True(result);
    }

    [Fact]
    public void LooksLikeDirectProductLookup_ReturnsFalse_ForGeneralRecommendationQuestion()
    {
        var intent = new ParsedIntent();

        var result = LookupConversationRules.LooksLikeDirectProductLookup(
            "xe cho nữ khoảng 30 triệu",
            intent);

        Assert.False(result);
    }
}