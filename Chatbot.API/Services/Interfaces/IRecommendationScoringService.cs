using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Recommendation;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IRecommendationScoringService
    {
        List<ScoredRecommendationItem> ScoreProducts(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage,
            int take = 6);
    }
}