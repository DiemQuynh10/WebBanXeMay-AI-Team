using Chatbot.API.Models.Rag;

namespace Chatbot.API.Services.Interfaces
{
    public interface IRagService
    {
        Task<RagQueryResponse?> QueryAsync(string query, int topK = 8);
    }
}
