using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IRecommendationLLMService
    {
        Task<LLMRecommendationResult?> RerankAsync(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            IReadOnlyList<ProductSummaryDto> candidates,
            string? ragContext = null);
    }
}