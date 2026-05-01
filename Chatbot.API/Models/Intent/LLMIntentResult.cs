namespace Chatbot.API.Models.Intent
{
    public class LLMIntentResult
    {
        public string? IntentType { get; set; }
        public bool IsFollowUp { get; set; }
        public bool ResetContext { get; set; }
        public string? FollowUpType { get; set; }
        public string? Reason { get; set; }

        public bool IsDirectLookup { get; set; }
        public bool IsFreshSearch { get; set; }

        public double Confidence { get; set; }

        public bool ShouldAskClarification { get; set; }
        public string? ClarificationQuestion { get; set; }

        public string? Brand { get; set; }
        public string? Category { get; set; }
        public string? Target { get; set; }

        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public decimal? TargetPrice { get; set; }
        public string? PriceFilterType { get; set; }

        public bool ForWork { get; set; }
        public bool ForSchool { get; set; }
        public bool ForCity { get; set; }
        public bool ForTour { get; set; }

        public bool WantsFuelSaving { get; set; }
        public bool WantsLargeStorage { get; set; }
        public bool WantsEasyControl { get; set; }
        public bool NeedsLowSeat { get; set; }

        public string? Action { get; set; }
        public bool KeepConstraints { get; set; }
        public bool ExcludePreviousProducts { get; set; }
        public bool ExcludePreviousBrands { get; set; }
        public int? HeightCm { get; set; }

        public List<string> MentionedProducts { get; set; } = new();
        public List<string> ExcludedBrands { get; set; } = new();
        public List<string> ExcludedCategories { get; set; } = new();
        public List<string> RequestedStyles { get; set; } = new();
        public List<string> RejectedStyles { get; set; } = new();

        public string? ComparisonFeature { get; set; }
    }
}