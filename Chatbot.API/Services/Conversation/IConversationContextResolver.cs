using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Conversation
{
    public interface IConversationContextResolver
    {
        ContextResolutionResult Resolve(
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile existingProfile,
            string? previousActiveFlow = null);
    }
}