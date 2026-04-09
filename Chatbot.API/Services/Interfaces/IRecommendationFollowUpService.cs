using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IRecommendationFollowUpService
    {
        Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile);
    }
}