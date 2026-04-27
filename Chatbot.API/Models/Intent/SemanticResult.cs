using System.Text.Json.Serialization;

namespace Chatbot.API.Models.Intent
{
    public class SemanticResult
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "unknown";

        [JsonPropertyName("flow_type")]
        public string FlowType { get; set; } = ChatFlowType.Unknown;

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("target")]
        public string? Target { get; set; }

        [JsonPropertyName("price_min")]
        public decimal? PriceMin { get; set; }

        [JsonPropertyName("price_max")]
        public decimal? PriceMax { get; set; }

        [JsonPropertyName("target_price")]
        public decimal? TargetPrice { get; set; }

        [JsonPropertyName("price_filter_type")]
        public string PriceFilterType { get; set; } = "none";

        [JsonPropertyName("price_level")]
        public string PriceLevel { get; set; } = "khong_ro";

        [JsonPropertyName("installment")]
        public string Installment { get; set; } = "khong_ro";

        [JsonPropertyName("interest")]
        public string Interest { get; set; } = "khong_ro";

        [JsonPropertyName("preference")]
        public string Preference { get; set; } = string.Empty;

        [JsonPropertyName("for_work")]
        public bool ForWork { get; set; }

        [JsonPropertyName("for_school")]
        public bool ForSchool { get; set; }

        [JsonPropertyName("for_city")]
        public bool ForCity { get; set; }

        [JsonPropertyName("for_tour")]
        public bool ForTour { get; set; }

        [JsonPropertyName("wants_fuel_saving")]
        public bool WantsFuelSaving { get; set; }

        [JsonPropertyName("wants_large_storage")]
        public bool WantsLargeStorage { get; set; }

        [JsonPropertyName("wants_easy_control")]
        public bool WantsEasyControl { get; set; }

        [JsonPropertyName("needs_low_seat")]
        public bool NeedsLowSeat { get; set; }

        [JsonPropertyName("height_cm")]
        public int? HeightCm { get; set; }

        [JsonPropertyName("mentioned_products")]
        public List<string> MentionedProducts { get; set; } = new();

        [JsonPropertyName("excluded_brands")]
        public List<string> ExcludedBrands { get; set; } = new();

        [JsonPropertyName("excluded_categories")]
        public List<string> ExcludedCategories { get; set; } = new();

        [JsonPropertyName("excluded_products")]
        public List<string> ExcludedProducts { get; set; } = new();

        [JsonPropertyName("requested_styles")]
        public List<string> RequestedStyles { get; set; } = new();

        [JsonPropertyName("comparison_feature")]
        public string? ComparisonFeature { get; set; }

        [JsonPropertyName("lookup_field")]
        public string? LookupField { get; set; }

        [JsonPropertyName("policy_slot")]
        public string? PolicySlot { get; set; }

        [JsonPropertyName("normalized_meaning")]
        public string NormalizedMeaning { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsFallback { get; set; }

        public SemanticResult Clone()
        {
            return new SemanticResult
            {
                Intent = Intent,
                FlowType = FlowType,
                Brand = Brand,
                Category = Category,
                Target = Target,
                PriceMin = PriceMin,
                PriceMax = PriceMax,
                TargetPrice = TargetPrice,
                PriceFilterType = PriceFilterType,
                PriceLevel = PriceLevel,
                Installment = Installment,
                Interest = Interest,
                Preference = Preference,
                ForWork = ForWork,
                ForSchool = ForSchool,
                ForCity = ForCity,
                ForTour = ForTour,
                WantsFuelSaving = WantsFuelSaving,
                WantsLargeStorage = WantsLargeStorage,
                WantsEasyControl = WantsEasyControl,
                NeedsLowSeat = NeedsLowSeat,
                HeightCm = HeightCm,
                MentionedProducts = new List<string>(MentionedProducts),
                ExcludedBrands = new List<string>(ExcludedBrands),
                ExcludedCategories = new List<string>(ExcludedCategories),
                ExcludedProducts = new List<string>(ExcludedProducts),
                RequestedStyles = new List<string>(RequestedStyles),
                ComparisonFeature = ComparisonFeature,
                LookupField = LookupField,
                PolicySlot = PolicySlot,
                NormalizedMeaning = NormalizedMeaning,
                IsFallback = IsFallback
            };
        }

        public static SemanticResult Unknown(string rawMessage)
        {
            var safeMessage = rawMessage?.Trim() ?? string.Empty;

            return new SemanticResult
            {
                Intent = "unknown",
                FlowType = ChatFlowType.Unknown,
                PriceFilterType = "none",
                PriceLevel = "khong_ro",
                Installment = "khong_ro",
                Interest = "khong_ro",
                Preference = string.Empty,
                NormalizedMeaning = string.IsNullOrWhiteSpace(safeMessage)
                    ? "chua du thong tin"
                    : safeMessage,
                IsFallback = true
            };
        }
    }
}
