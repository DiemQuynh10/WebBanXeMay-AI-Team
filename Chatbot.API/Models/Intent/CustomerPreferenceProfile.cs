namespace Chatbot.API.Models.Intent
{
    public class CustomerPreferenceProfile
    {
        public string ConversationId { get; set; } = string.Empty;

        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public decimal? TargetPrice { get; set; }
        public PriceFilterType FilterType { get; set; } = PriceFilterType.None;

        public string? PreferredCategory { get; set; }
        public string? PreferredBrand { get; set; }
        public string? Target { get; set; }

        public HashSet<string> ExcludedCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedBrands { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public int? HeightCm { get; set; }
        public bool NeedsLowSeat { get; set; }

        public bool ForSchool { get; set; }
        public bool ForWork { get; set; }
        public bool ForCity { get; set; }
        public bool ForTour { get; set; }

        public bool WantsEasyControl { get; set; }
        public bool WantsFuelSaving { get; set; }
        public bool WantsLargeStorage { get; set; }

        public bool PrefersMaleStyle { get; set; }
        public bool PrefersFemaleStyle { get; set; }

        public HashSet<string> RequestedStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> LastRecommendedProducts { get; set; } = new();
        public List<string> LastMentionedProducts { get; set; } = new();
        public List<string> LastComparedProducts { get; set; } = new();
        public List<int> LastRecommendedProductIds { get; set; } = new();

        public bool HasActiveRecommendationContext { get; set; }
        public bool HasActiveCompareContext { get; set; }

        public string? LastAnswerMode { get; set; } 
        public string? LastComparisonFeature { get; set; }

        public string? ActiveFlow { get; set; } 

        public string? LastLookupProductName { get; set; }
        public string? LastLookupField { get; set; }
        public int? LastLookupProductId { get; set; }

        public List<string> LastSearchProductNames { get; set; } = new();
        public List<int> LastSearchProductIds { get; set; } = new();

        public string? LastResolvedBrandSwitchFrom { get; set; }
        public string? LastResolvedBrandSwitchTo { get; set; }

        public int TurnCount { get; set; }

        public string? LastUserMessage { get; set; }
        public string? LastIntentType { get; set; }
        public bool HasPendingOrderLookup { get; set; }
        public int? LastResolvedProductId { get; set; }
        public string? LastResolvedProductName { get; set; }
        public List<string> LastLookupCandidateNames { get; set; } = new();

        public int? LastResolvedOrderId { get; set; }
        public string? LastResolvedOrderPhone { get; set; }
        public int? PendingOrderId { get; set; }
        public string? PendingOrderPhone { get; set; }
        public List<string> BaseRecommendedProducts { get; set; } = new();
        public List<int> BaseRecommendedProductIds { get; set; } = new();
        public List<string>? CurrentRecommendedProducts { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}