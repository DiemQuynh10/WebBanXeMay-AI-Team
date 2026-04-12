using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services.Conversation
{
    public interface IFlowDecisionService
    {
        FlowRoutingResult ResolveFinalRouting(
            string normalizedMessage,
            ParsedIntent effectiveIntent,
            CustomerPreferenceProfile conversationProfile,
            RecommendationContextDecision contextDecision,
            FlowRoutingResult baseRouting);
    }
}