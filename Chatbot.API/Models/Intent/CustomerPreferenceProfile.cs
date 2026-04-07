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

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}