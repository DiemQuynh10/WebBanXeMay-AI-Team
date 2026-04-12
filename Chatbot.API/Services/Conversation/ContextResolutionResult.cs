using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Conversation
{
    public class ContextResolutionResult
    {
        public ParsedIntent EffectiveIntent { get; set; } = new();
        public RecommendationContextDecision ContextDecision { get; set; }
        public bool ShouldResetContext { get; set; }
        public bool ShouldPreserveBudgetOnlyContext { get; set; }
    }
}