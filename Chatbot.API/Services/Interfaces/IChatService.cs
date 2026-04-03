using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IChatService
    {
        Task<ChatResponse> ProcessMessageAsync(ChatRequest request);

    }
}
