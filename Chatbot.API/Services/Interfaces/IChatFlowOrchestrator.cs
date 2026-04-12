using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IChatFlowOrchestrator
    {
        Task<ChatResponse> HandleAsync(ChatRequest request);
    }
}