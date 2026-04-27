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
        public HashSet<string> ExcludedProducts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

        public string RouteFlow { get; set; } = "unknown";

        public bool IsGreeting { get; set; }
        public bool IsOutOfScope { get; set; }

        public bool IsOrderLookup { get; set; }

        public bool IsDirectProductLookup { get; set; }
        public bool IsProductSearch { get; set; }
        public bool IsOpenRecommendation { get; set; }
        public bool IsDirectCompare { get; set; }
        public bool IsBrandSwitch { get; set; }

        public bool HasFreshConsultationSignal { get; set; }
        public bool HasExpandRecommendationSignal { get; set; }
        public bool HasNarrowRefinementSignal { get; set; }

        // tiện cho debug/log
        public string? RecommendationContextActionHint { get; set; }
        public string? LookupTargetType { get; set; }

        /// <summary>
        /// price, stock, cc, detail
        /// </summary>
        public string? LookupField { get; set; }

        /// <summary>
        /// Structured policy/service slot, for example documents, process, interest, loan_term, warranty_period.
        /// </summary>
        public string? PolicySlot { get; set; }

        /// <summary>
        /// Cho phép ChatService biết đây là câu phải ưu tiên deterministic trước AI.
        /// </summary>
        public bool HasDeterministicProductIntent { get; set; }
        public ParsedIntent Clone()
        {
            return new ParsedIntent
            {
                Category = Category,
                PriceMin = PriceMin,
                PriceMax = PriceMax,
                Brand = Brand,
                Target = Target,
                RawMessage = RawMessage,

                FilterType = FilterType,
                TargetPrice = TargetPrice,

                ExcludedCategories = new HashSet<string>(ExcludedCategories, StringComparer.OrdinalIgnoreCase),
                ExcludedBrands = new HashSet<string>(ExcludedBrands, StringComparer.OrdinalIgnoreCase),
                ExcludedProducts = new HashSet<string>(ExcludedProducts, StringComparer.OrdinalIgnoreCase),

                HeightCm = HeightCm,
                NeedsLowSeat = NeedsLowSeat,

                ForSchool = ForSchool,
                ForWork = ForWork,
                ForCity = ForCity,
                ForTour = ForTour,

                WantsEasyControl = WantsEasyControl,
                WantsFuelSaving = WantsFuelSaving,
                WantsLargeStorage = WantsLargeStorage,

                PrefersMaleStyle = PrefersMaleStyle,
                PrefersFemaleStyle = PrefersFemaleStyle,

                RequestedStyles = new HashSet<string>(RequestedStyles, StringComparer.OrdinalIgnoreCase),

                IntentType = IntentType,
                IsFollowUp = IsFollowUp,
                FollowUpType = FollowUpType,

                MentionedProducts = new List<string>(MentionedProducts),
                ComparisonFeature = ComparisonFeature,

                RouteFlow = RouteFlow,

                IsGreeting = IsGreeting,
                IsOutOfScope = IsOutOfScope,
                IsOrderLookup = IsOrderLookup,
                IsDirectProductLookup = IsDirectProductLookup,
                IsProductSearch = IsProductSearch,
                IsOpenRecommendation = IsOpenRecommendation,
                IsDirectCompare = IsDirectCompare,
                IsBrandSwitch = IsBrandSwitch,

                HasFreshConsultationSignal = HasFreshConsultationSignal,
                HasExpandRecommendationSignal = HasExpandRecommendationSignal,
                HasNarrowRefinementSignal = HasNarrowRefinementSignal,

                RecommendationContextActionHint = RecommendationContextActionHint,
                LookupTargetType = LookupTargetType,
                LookupField = LookupField,
                PolicySlot = PolicySlot,

                HasDeterministicProductIntent = HasDeterministicProductIntent
            };
        }
    }

}
