using Chatbot.API.Models.Chat;

namespace Chatbot.API.Services.Interfaces
{
    public interface IConversationMemoryService
    {
        Task<List<ChatMessage>> GetMessagesAsync(string conversationId);

        Task AddMessageAsync(string conversationId, ChatMessage message, string? channel = null, string? userId = null);

        Task ClearAsync(string conversationId);

        Task<bool> ExistsAsync(string conversationId);
    }
}