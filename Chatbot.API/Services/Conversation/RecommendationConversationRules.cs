using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Conversation
{
    public static class RecommendationConversationRules
    {
        public static bool HasActiveRecommendationContext(CustomerPreferenceProfile? profile)
        {
            return profile != null
                && profile.HasActiveRecommendationContext
                && profile.LastRecommendedProducts != null
                && profile.LastRecommendedProducts.Count > 0;
        }

        public static bool LooksLikeBudgetPivotFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetSignal =
                Regex.IsMatch(text, @"\b\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase) ||
                text.Contains("khoảng") ||
                text.Contains("khoang") ||
                text.Contains("tầm") ||
                text.Contains("tam") ||
                text.Contains("quanh") ||
                text.Contains("dưới") ||
                text.Contains("duoi") ||
                text.Contains("trên") ||
                text.Contains("tren");

            bool startsLikeFollowUp =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.StartsWith("thế ") ||
                text.StartsWith("the ") ||
                text.StartsWith("vậy ") ||
                text.StartsWith("vay ");

            bool asksFollowUpStyle =
                text.Contains("thì sao") ||
                text.Contains("thi sao");

            return hasBudgetSignal && (startsLikeFollowUp || asksFollowUpStyle);
        }

        public static bool LooksLikeExpandFromCurrentGoal(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null || !HasActiveRecommendationContext(profile))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasExpandSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.IsBrandSwitch;

            bool looksLikeFollowUpPhrase =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.StartsWith("thế ") ||
                text.StartsWith("the ") ||
                text.StartsWith("vậy ") ||
                text.StartsWith("vay ") ||
                text.Contains("thì sao") ||
                text.Contains("thi sao");

            bool isNotFreshRestart = !LooksLikeFreshRecommendationRestart(message, parsedIntent);

            return hasExpandSignal && looksLikeFollowUpPhrase && isNotFreshRestart;
        }

        public static bool LooksLikeRefineWithinCurrentRecommendation(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null || !HasActiveRecommendationContext(profile))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetSignal =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None ||
                text.Contains("dưới") ||
                text.Contains("duoi") ||
                text.Contains("trên") ||
                text.Contains("tren") ||
                text.Contains("rẻ hơn") ||
                text.Contains("re hon") ||
                text.Contains("đắt hơn") ||
                text.Contains("dat hon") ||
                text.Contains("tầm") ||
                text.Contains("tam") ||
                text.Contains("khoảng") ||
                text.Contains("khoang");

            bool hasConstraintSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.ExcludedBrands.Any() ||
                parsedIntent.ExcludedCategories.Any() ||
                parsedIntent.RequestedStyles.Any();

            bool looksLikeShortFollowUp =
                text.Length <= 80 ||
                parsedIntent.IsFollowUp ||
                string.Equals(parsedIntent.FollowUpType, "refine", StringComparison.OrdinalIgnoreCase);

            bool looksLikeFreshStandaloneRecommendation =
                text.StartsWith("tư vấn ") ||
                text.StartsWith("tu van ") ||
                text.StartsWith("mình muốn ") ||
                text.StartsWith("minh muon ") ||
                text.StartsWith("mình cần ") ||
                text.StartsWith("minh can ");

            if (looksLikeFreshStandaloneRecommendation && !string.IsNullOrWhiteSpace(parsedIntent.Target))
                return false;

            return looksLikeShortFollowUp && (hasBudgetSignal || hasConstraintSignal);
        }

        public static bool LooksLikeRecommendationFollowUp(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            return LooksLikeExpandFromCurrentGoal(message, parsedIntent, profile)
                || LooksLikeRefineWithinCurrentRecommendation(message, parsedIntent, profile);
        }

        public static bool LooksLikeFreshRecommendationRestart(
            string message,
            ParsedIntent parsedIntent)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasRestartPhrase =
                text.StartsWith("đổi ý") ||
                text.StartsWith("doi y") ||
                text.StartsWith("giờ ") ||
                text.StartsWith("gio ") ||
                text.StartsWith("giờ t muốn") ||
                text.StartsWith("gio t muon") ||
                text.StartsWith("h t muốn") ||
                text.StartsWith("ý là") ||
                text.StartsWith("y la") ||
                text.Contains("không phải") ||
                text.Contains("khong phai") ||
                text.Contains("chứ không phải") ||
                text.Contains("chu khong phai");

            bool hasFreshConsultationSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsEasyControl;

            bool hasOnlyBudgetChange =
                (parsedIntent.PriceMin.HasValue ||
                 parsedIntent.PriceMax.HasValue ||
                 parsedIntent.TargetPrice.HasValue ||
                 parsedIntent.FilterType != PriceFilterType.None) &&
                string.IsNullOrWhiteSpace(parsedIntent.Target) &&
                string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                !parsedIntent.ForWork &&
                !parsedIntent.ForSchool &&
                !parsedIntent.ForCity &&
                !parsedIntent.ForTour &&
                !parsedIntent.WantsLargeStorage &&
                !parsedIntent.NeedsLowSeat &&
                !parsedIntent.WantsFuelSaving &&
                !parsedIntent.WantsEasyControl;

            if (hasRestartPhrase && hasOnlyBudgetChange)
                return false;

            return hasRestartPhrase && hasFreshConsultationSignal;
        }
    }
}