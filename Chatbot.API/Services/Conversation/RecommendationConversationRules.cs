using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Conversation
{
    public static class RecommendationConversationRules
    {
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
                text.Contains("quanh");

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

            return hasBudgetSignal && startsLikeFollowUp && asksFollowUpStyle;
        }

        public static bool LooksLikeRecommendationFollowUp(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null || profile == null)
                return false;

            bool hasRecommendationContext =
                profile.HasActiveRecommendationContext &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0;

            if (!hasRecommendationContext)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetSignal =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None;

            bool hasUseCaseSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour;

            bool hasHardConstraintSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category);

            bool hasStrongPreferenceSignal =
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.ExcludedBrands.Any() ||
                parsedIntent.ExcludedCategories.Any() ||
                parsedIntent.RequestedStyles.Any();

            bool looksFreshStandaloneRecommendation =
                text.StartsWith("tư vấn ") ||
                text.StartsWith("tu van ") ||
                text.StartsWith("xe ") ||
                text.StartsWith("mình muốn ") ||
                text.StartsWith("minh muon ") ||
                text.StartsWith("mình cần ") ||
                text.StartsWith("minh can ") ||
                text.StartsWith("cho nam") ||
                text.StartsWith("cho nữ") ||
                text.StartsWith("cho nu") ||
                text.StartsWith("đi làm") ||
                text.StartsWith("di lam") ||
                text.StartsWith("đi học") ||
                text.StartsWith("di hoc");

            bool hasEnoughFreshSignals =
                hasBudgetSignal ||
                hasUseCaseSignal ||
                hasHardConstraintSignal ||
                hasStrongPreferenceSignal;
            bool looksDirectBudgetConsultation =
    hasBudgetSignal &&
    (
        text.StartsWith("tư vấn xe") ||
        text.StartsWith("tu van xe") ||
        text.StartsWith("xe ") ||
        text.StartsWith("mua xe") ||
        text.StartsWith("mua một xe") ||
        text.StartsWith("mua 1 xe")
    );
            if ((looksFreshStandaloneRecommendation && hasEnoughFreshSignals) || looksDirectBudgetConsultation)
                return false;
            bool startsLikeFollowUp =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.StartsWith("rẻ hơn") ||
                text.StartsWith("re hon") ||
                text.StartsWith("đắt hơn") ||
                text.StartsWith("dat hon") ||
                text.StartsWith("ưu tiên ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("đừng ") ||
                text.StartsWith("dung ") ||
                text.StartsWith("không thích ") ||
                text.StartsWith("khong thich ") ||
                text.StartsWith("không muốn ") ||
                text.StartsWith("khong muon ") ||
                text.StartsWith("xe ga") ||
                text.StartsWith("xe số") ||
                text.StartsWith("xe so") ||
                text.StartsWith("honda") ||
                text.StartsWith("yamaha") ||
                text.StartsWith("suzuki") ||
                text.StartsWith("sym") ||
                text.StartsWith("piaggio");

            bool containsSoftFollowUpPhrase =
                text.Contains("thì sao") ||
                text.Contains("thi sao") ||
                text.Contains("hơn chút") ||
                text.Contains("hon chut") ||
                text.Contains("rộng hơn") ||
                text.Contains("rong hon") ||
                text.Contains("gọn hơn") ||
                text.Contains("gon hon") ||
                text.Contains("mềm hơn") ||
                text.Contains("mem hon") ||
                text.Contains("tiết kiệm hơn") ||
                text.Contains("tiet kiem hon");

            bool looksShortFollowUpFragment =
                text.Length <= 80 &&
                (
                    startsLikeFollowUp ||
                    containsSoftFollowUpPhrase ||
                    parsedIntent.IsFollowUp ||
                    string.Equals(parsedIntent.FollowUpType, "refine", StringComparison.OrdinalIgnoreCase)
                );

            return looksShortFollowUpFragment;
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