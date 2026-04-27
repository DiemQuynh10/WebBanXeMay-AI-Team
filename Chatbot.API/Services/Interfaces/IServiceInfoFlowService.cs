using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IServiceInfoFlowService
    {
        Task<ChatResponse> HandleAsync(
            ChatRequest request,
            string conversationId,
            string normalizedMessage,
            string semanticQuery,
            string originalMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile);
    }
}
