namespace Chatbot.API.Models.Chat
{
    public class ConversationState
    {
        public string ConversationId { get; set; } = string.Empty;

        public string? CurrentDomain { get; set; }
        public string? CurrentGoalType { get; set; }
        public string? CurrentGoalStatus { get; set; }

        public string? LastIntentType { get; set; }
        public string? LastQuestionType { get; set; }
        public string? LastBotQuestionType { get; set; }

        public string? LastResolvedReference { get; set; }

        public Dictionary<string, string?> Constraints { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<int> CandidateProductIds { get; set; } = new();
        public List<int> MentionedProductIds { get; set; } = new();
        public List<string> MentionedProductNames { get; set; } = new();

        public string? TurnSummary { get; set; }

        public decimal? CarryForwardConfidence { get; set; }

        public bool IsAwaitingClarification { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}