using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Conversation
{
    public interface IConversationPolicyService
    {
        (ParsedIntent EffectiveIntent, RecommendationContextDecision ContextDecision) ResolveEffectiveIntent(
            string conversationId,
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? existingProfile);

        bool ShouldForceCompareFollowUp(
            string normalizedMessage,
            ParsedIntent effectiveIntent,
            CustomerPreferenceProfile? profile);
    }
}