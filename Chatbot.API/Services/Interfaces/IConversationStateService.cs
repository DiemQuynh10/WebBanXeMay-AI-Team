using Chatbot.API.Models.Chat;

namespace Chatbot.API.Services.Interfaces
{
    public interface IConversationStateService
    {
        Task<ConversationState> GetAsync(string conversationId, string? channel = null, string? userId = null);
        Task ApplyPatchAsync(string conversationId, ConversationStatePatch patch, string? channel = null, string? userId = null);
        Task ClearAsync(string conversationId);
    }
}