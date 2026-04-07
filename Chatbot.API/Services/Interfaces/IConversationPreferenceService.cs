using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IConversationPreferenceService
    {
        Task<CustomerPreferenceProfile> GetAsync(string conversationId);
        Task<CustomerPreferenceProfile> MergeAsync(string conversationId, ParsedIntent intent);
        Task ClearAsync(string conversationId);
        string BuildProfileSummary(CustomerPreferenceProfile profile);
    }
}