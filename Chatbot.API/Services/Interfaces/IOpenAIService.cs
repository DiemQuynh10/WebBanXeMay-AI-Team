using System.Threading;
using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IOpenAIService
    {
        Task<ChatResponse> AskAsync(AIRequestContext context);

        Task<ChatResponse> AskAsync(
            AIRequestContext context,
            CancellationToken cancellationToken);
    }
}