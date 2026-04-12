using Chatbot.API.Models.Intent;

namespace Chatbot.API.Helpers
{
    public static class FlowIntentHeuristics
    {
        public static bool IsHardFilterOnlySearch(ParsedIntent intent, string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool hasHardFilter =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category);

            bool hasSoftNeed =
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.ForWork ||
                intent.ForSchool ||
                intent.ForCity ||
                intent.ForTour ||
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                intent.RequestedStyles.Count > 0;

            bool hasRecommendationCue =
                text.Contains("tư vấn") ||
                text.Contains("tu van") ||
                text.Contains("gợi ý") ||
                text.Contains("goi y") ||
                text.Contains("phù hợp") ||
                text.Contains("phu hop") ||
                text.Contains("nên mua") ||
                text.Contains("nen mua");

            return hasHardFilter && !hasSoftNeed && !hasRecommendationCue;
        }
    }
}