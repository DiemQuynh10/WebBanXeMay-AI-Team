namespace Chatbot.API.Models.Intent
{
    public class ParsedIntent
    {
        public string? Category { get; set; }
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public string? Brand { get; set; }
        public string? Target { get; set; }
        public string? RawMessage { get; set; }
        public PriceFilterType FilterType { get; set; } = PriceFilterType.None;
        public decimal? TargetPrice { get; set; }

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

        public string IntentType { get; set; } = "unknown";
        
        public bool IsFollowUp { get; set; }

        public string? FollowUpType { get; set; }
       
        public List<string> MentionedProducts { get; set; } = new();

        public string? ComparisonFeature { get; set; }
      
    }
}