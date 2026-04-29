namespace Chatbot.API.Models.Chat
{
    public class ConversationStatePatch
    {
        public string? CurrentDomain { get; set; }
        public string? CurrentGoalType { get; set; }
        public string? CurrentGoalStatus { get; set; }

        public string? LastIntentType { get; set; }
        public string? LastQuestionType { get; set; }
        public string? LastBotQuestionType { get; set; }

        public string? LastResolvedReference { get; set; }

        public Dictionary<string, string?>? Constraints { get; set; }

        public List<int>? CandidateProductIds { get; set; }
        public List<int>? MentionedProductIds { get; set; }
        public List<string>? MentionedProductNames { get; set; }

        public string? TurnSummary { get; set; }

        public decimal? CarryForwardConfidence { get; set; }

        public bool? IsAwaitingClarification { get; set; }

        public bool ClearCandidateProducts { get; set; }
        public bool ClearMentionedProducts { get; set; }
        public bool ClearConstraints { get; set; }
    }
}