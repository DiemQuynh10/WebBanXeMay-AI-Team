using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IConversationPreferenceService
    {
        Task<CustomerPreferenceProfile> GetAsync(string conversationId);
        Task<CustomerPreferenceProfile> MergeAsync(
        string conversationId,
        ParsedIntent intent,
        bool isFreshRecommendation = false
    );

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
        Task SaveProductLookupContextAsync(
    string conversationId,
    int? productId,
    string? productName,
    IEnumerable<string>? candidateNames = null);

        Task SaveResolvedOrderContextAsync(
            string conversationId,
            int? orderId,
            string? phone);

        Task ClearOrderLookupPendingAsync(string conversationId);

        Task UpdateCurrentRecommendedProductsAsync(
            string conversationId,
            IEnumerable<ProductSummaryDto> products,
            string answerMode = "fresh_consultation");

        string BuildProfileSummary(CustomerPreferenceProfile profile);
    }
}
