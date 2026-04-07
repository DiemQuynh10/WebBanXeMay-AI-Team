using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Interfaces
{
    public interface IConversationHistoryService
    {
        Task SaveExchangeAsync(
            string conversationId,
            string? channel,
            string? userId,
            string userMessage,
            string botReply);

        Task<List<ConversationSummaryResponse>> GetConversationsAsync(string userId, string channel);

        Task<List<ConversationMessageResponse>> GetMessagesAsync(string conversationId);

        Task<bool> ExistsAsync(string conversationId);

        Task DeleteConversationAsync(string conversationId);
    }
}