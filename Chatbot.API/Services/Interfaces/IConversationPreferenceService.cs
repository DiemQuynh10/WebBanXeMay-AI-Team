using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IConversationPreferenceService
    {
        Task<CustomerPreferenceProfile> GetAsync(string conversationId);
        Task<CustomerPreferenceProfile> MergeAsync(string conversationId, ParsedIntent intent);

        Task SetMentionedProductsAsync(string conversationId, IEnumerable<string> productNames);
        Task SetComparedProductsAsync(string conversationId, IEnumerable<string> productNames);
        Task SetLastIntentTypeAsync(string conversationId, string intentType);
        Task SetSemanticContextAsync(string conversationId, SemanticResult semanticResult);

        Task ClearRecommendationContextAsync(string conversationId);
        Task ResetForFreshConsultationAsync(string conversationId);
        Task ClearAsync(string conversationId);
        Task SetBaseRecommendedProductsAsync(
    string conversationId,
    IEnumerable<ProductSummaryDto> products);

        Task UpdateCurrentRecommendedProductsAsync(
            string conversationId,
            IEnumerable<ProductSummaryDto> products,
            string answerMode = "fresh_consultation");

        string BuildProfileSummary(CustomerPreferenceProfile profile);
    }
}
