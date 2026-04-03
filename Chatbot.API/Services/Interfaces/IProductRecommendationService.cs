using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IProductRecommendationService
    {
        List<ProductSummaryDto> RankProducts(
            IEnumerable<ProductSummaryDto> products,
            ParsedIntent intent,
            string normalizedMessage,
            int take = 5);
    }
}