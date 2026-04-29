using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface ITurnContextBuilder
    {
        Task<TurnContextBuildResult> BuildAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile existingProfile,
            ConversationState state);
    }
}