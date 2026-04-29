using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;

namespace Chatbot.API.Models.Chat
{
    public class ChatOrchestrationContext
    {
        public ChatRequest Request { get; set; } = new();
        public string ConversationId { get; set; } = string.Empty;

        public string OriginalMessage { get; set; } = string.Empty;
        public string NormalizedMessage { get; set; } = string.Empty;
        public string SemanticQuery { get; set; } = string.Empty;

        public CustomerPreferenceProfile ExistingProfile { get; set; } = new();
        public ConversationState State { get; set; } = new();

        public ParsedIntent ParsedIntent { get; set; } = new();
        public ParsedIntent EffectiveIntent { get; set; } = new();

        public FlowRoutingResult BaseRouting { get; set; } = new();
        public FlowRoutingResult FinalRouting { get; set; } = new();

        public string? PendingClarification { get; set; }

        public bool ShouldStop { get; set; }
    }
}
