using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IOrderLookupFlowService
    {
        Task<ChatResponse> HandleAsync(ChatRequest request, string normalizedMessage);
    }
}