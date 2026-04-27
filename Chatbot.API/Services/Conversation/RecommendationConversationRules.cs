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
                parsedIntent.IsBrandSwitch ||
                ContainsBrand(text) ||
                ContainsAlternativeChoice(text);

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

            if (LooksLikeFreshStandaloneConsultation(text, parsedIntent))
                return false;

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
                parsedIntent.RequestedStyles.Any() ||
                !string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature) ||
                ContainsAlternativeChoice(text) ||
                ContainsBrand(text);

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
            if (LooksLikeFreshStandaloneConsultation(message, parsedIntent))
                return false;

            return LooksLikeExpandFromCurrentGoal(message, parsedIntent, profile)
                || LooksLikeRefineWithinCurrentRecommendation(message, parsedIntent, profile);
        }

        public static bool LooksLikeAlternativeRequestAfterRejection(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null || !HasActiveRecommendationContext(profile))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasRejectSignal =
                text.Contains("không mua") ||
                text.Contains("khong mua") ||
                text.Contains("không lấy") ||
                text.Contains("khong lay") ||
                text.Contains("không thích xe đó") ||
                text.Contains("khong thich xe do") ||
                text.Contains("không ưng") ||
                text.Contains("khong ung") ||
                text.Contains("xe này không hợp") ||
                text.Contains("xe nay khong hop") ||
                text.Contains("mẫu này không hợp") ||
                text.Contains("mau nay khong hop") ||
                text.Contains("không chốt") ||
                text.Contains("khong chot");

            bool asksAlternative =
                text.Contains("xe khác") ||
                text.Contains("xe khac") ||
                text.Contains("mẫu khác") ||
                text.Contains("mau khac") ||
                text.Contains("loại khác") ||
                text.Contains("loai khac") ||
                text.Contains("đổi xe") ||
                text.Contains("doi xe") ||
                text.Contains("đổi mẫu") ||
                text.Contains("doi mau") ||
                parsedIntent.HasExpandRecommendationSignal ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target);

            return hasRejectSignal && (asksAlternative || text.Contains("xe đó") || text.Contains("xe do"));
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

        private static bool LooksLikeFreshStandaloneConsultation(string message, ParsedIntent parsedIntent)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool startsFresh =
                text.StartsWith("tư vấn ") ||
                text.StartsWith("tu van ") ||
                text.StartsWith("gợi ý ") ||
                text.StartsWith("goi y ") ||
                text.StartsWith("mua xe ") ||
                text.StartsWith("xe ") ||
                text.StartsWith("tôi muốn ") ||
                text.StartsWith("toi muon ") ||
                text.StartsWith("mình muốn ") ||
                text.StartsWith("minh muon ");

            bool hasFreshSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue;

            return startsFresh && hasFreshSignal;
        }

        private static bool ContainsAlternativeChoice(string text)
        {
            return text.Contains("loại khác") ||
                   text.Contains("loai khac") ||
                   text.Contains("xe khác") ||
                   text.Contains("xe khac") ||
                   text.Contains("mẫu khác") ||
                   text.Contains("mau khac");
        }

        private static bool ContainsBrand(string text)
        {
            return text.Contains("honda") ||
                   text.Contains("yamaha") ||
                   text.Contains("suzuki") ||
                   text.Contains("piaggio") ||
                   text.Contains("sym");
        }
    }
}
