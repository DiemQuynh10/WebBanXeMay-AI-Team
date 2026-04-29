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
            if (parsedIntent == null)
                return false;

            if (parsedIntent.IsOutOfScope || parsedIntent.IsNoise || parsedIntent.IsGreeting || parsedIntent.IsAck)
                return false;

            if (string.IsNullOrWhiteSpace(message) || !HasActiveRecommendationContext(profile))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetSignal =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None ||
                text.Contains("dưới") || text.Contains("duoi") ||
                text.Contains("trên") || text.Contains("tren") ||
                text.Contains("rẻ hơn") || text.Contains("re hon") ||
                text.Contains("đắt hơn") || text.Contains("dat hon") ||
                text.Contains("tầm") || text.Contains("tam") ||
                text.Contains("khoảng") || text.Contains("khoang");

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

            bool hasReferenceSignal =
                text.Contains("còn ") || text.Contains("con ") ||
                text.Contains("thì sao") || text.Contains("thi sao") ||
                text.Contains("thế còn") || text.Contains("the con") ||
                text.Contains("vậy còn") || text.Contains("vay con") ||
                text.Contains("mẫu đó") || text.Contains("mau do") ||
                text.Contains("xe đó") || text.Contains("xe do") ||
                text.Contains("con đó") || text.Contains("con do") ||
                text.Contains("mẫu kia") || text.Contains("mau kia") ||
                text.Contains("xe kia") ||
                text.Contains("con kia");

            bool looksLikeShortFollowUp =
                hasReferenceSignal ||
                parsedIntent.IsFollowUp;

            bool hasExplicitCompareWords = HasExplicitCompareWords(text);

            bool hasFeatureRefinementSignal = HasFeatureRefinementSignal(text, parsedIntent);

            bool looksLikeFreshStandaloneRecommendation =
                text.StartsWith("tư vấn ") ||
                text.StartsWith("tu van ") ||
                text.StartsWith("mình muốn ") ||
                text.StartsWith("minh muon ") ||
                text.StartsWith("mình cần ") ||
                text.StartsWith("minh can ");

            if (looksLikeFreshStandaloneRecommendation && !string.IsNullOrWhiteSpace(parsedIntent.Target))
                return false;

            if (hasFeatureRefinementSignal && !hasExplicitCompareWords)
                return true;

            if (hasExplicitCompareWords)
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
        private static bool HasExplicitCompareWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("so sánh") ||
                   text.Contains("so sanh") ||
                   text.Contains("so với") ||
                   text.Contains("so voi") ||
                   text.Contains("khác nhau") ||
                   text.Contains("khac nhau") ||
                   text.Contains("cái nào hơn") ||
                   text.Contains("cai nao hon");
        }
        public static bool LooksLikeAlternativeRequestAfterRejection(
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? existingProfile)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage))
            {
                return false;
            }

            var text = normalizedMessage.Trim().ToLowerInvariant();

            var hasAlternativeSignal =
                text.Contains("khác") ||
                text.Contains("khac") ||
                text.Contains("mẫu khác") ||
                text.Contains("mau khac") ||
                text.Contains("xe khác") ||
                text.Contains("xe khac") ||
                text.Contains("gợi ý khác") ||
                text.Contains("goi y khac") ||
                text.Contains("tư vấn khác") ||
                text.Contains("tu van khac") ||
                text.Contains("còn mẫu nào") ||
                text.Contains("con mau nao") ||
                text.Contains("còn xe nào") ||
                text.Contains("con xe nao") ||
                text.Contains("lựa chọn khác") ||
                text.Contains("lua chon khac");

            var hasRejectionSignal =
                text.Contains("không thích") ||
                text.Contains("khong thich") ||
                text.Contains("không ưng") ||
                text.Contains("khong ung") ||
                text.Contains("không hợp") ||
                text.Contains("khong hop") ||
                text.Contains("không phù hợp") ||
                text.Contains("khong phu hop") ||
                text.Contains("chưa ưng") ||
                text.Contains("chua ung") ||
                text.Contains("không muốn") ||
                text.Contains("khong muon");

            if (hasAlternativeSignal || hasRejectionSignal)
            {
                return true;
            }

            return existingProfile?.HasActiveRecommendationContext == true
                   && parsedIntent.IntentType == "recommendation"
                   && hasAlternativeSignal;
        }
        private static bool HasFeatureRefinementSignal(string text, ParsedIntent parsedIntent)
        {
            if (string.IsNullOrWhiteSpace(text) || parsedIntent == null)
                return false;

            return text.Contains("nếu ") || text.Contains("neu ") ||
       text.Contains("cốp rộng") || text.Contains("cop rong") ||
       text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang") ||
       text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
       text.Contains("đi làm") || text.Contains("di lam") ||
       text.Contains("đi học") || text.Contains("di hoc") ||
       parsedIntent.WantsLargeStorage ||
       parsedIntent.WantsFuelSaving ||
       parsedIntent.WantsEasyControl ||
       parsedIntent.NeedsLowSeat ||
       parsedIntent.ForWork ||
       parsedIntent.ForSchool;
        }
    }
}