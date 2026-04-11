using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
namespace Chatbot.API.Services.Conversation
{
    public static class RecommendationContextRules
    {
        public static bool ShouldResetContextForFreshConsultation(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile existingProfile)
        {
            if (existingProfile == null)
                return false;

            bool hasOldContext =
                existingProfile.TurnCount > 0 ||
                existingProfile.HasActiveRecommendationContext ||
                !string.IsNullOrWhiteSpace(existingProfile.PreferredBrand) ||
                !string.IsNullOrWhiteSpace(existingProfile.PreferredCategory) ||
                existingProfile.TargetPrice.HasValue ||
                existingProfile.PriceMin.HasValue ||
                existingProfile.PriceMax.HasValue ||
                existingProfile.HeightCm.HasValue;

            if (!hasOldContext)
                return false;

            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            if (RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, parsedIntent, existingProfile))
                return false;

            if (RecommendationConversationRules.LooksLikeFreshRecommendationRestart(message, parsedIntent))
                return true;

            bool isBudgetOnlyFollowUp =
                parsedIntent.FilterType != PriceFilterType.None &&
                !string.IsNullOrWhiteSpace(existingProfile.ConversationId) &&
                existingProfile.HasActiveRecommendationContext;

            bool looksLikeFollowUp =
                text.StartsWith("còn ") ||
                text.StartsWith("không thích ") ||
                text.StartsWith("không muốn ") ||
                text.StartsWith("ưu tiên ") ||
                text.StartsWith("né ") ||
                text.StartsWith("con nào ") ||
                parsedIntent.IntentType == "followup" ||
                parsedIntent.IntentType == "refine" ||
                parsedIntent.IntentType == "compare" ||
                parsedIntent.IntentType == "brand_switch" ||
                isBudgetOnlyFollowUp ||
                IsFollowUpPreferenceFragment(text);

            if (looksLikeFollowUp)
                return false;

            bool looksLikeFreshStandalone =
                text.StartsWith("tư vấn") ||
                text.StartsWith("xe ") ||
                text.StartsWith("mình ") ||
                text.StartsWith("cho mình ") ||
                text.StartsWith("tôi ") ||
                text.StartsWith("cho nữ") ||
                text.StartsWith("cho nam") ||
                text.StartsWith("giờ t muốn") ||
                text.StartsWith("gio t muon") ||
                text.StartsWith("h t muốn") ||
                text.StartsWith("vậy h t muốn") ||
                text.StartsWith("vay h t muon") ||
                text.StartsWith("ý là h t muốn") ||
                text.StartsWith("y la h t muon") ||
                text.StartsWith("đổi ý") ||
                text.StartsWith("doi y") ||
                text.Contains("chứ không phải") ||
                text.Contains("chu khong phai") ||
                parsedIntent.HeightCm.HasValue ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category);

            return looksLikeFreshStandalone;
        }

        public static RecommendationContextDecision DecideRecommendationContextAction(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile profile,
            string? previousActiveFlow = null)
        {
            if (parsedIntent == null || profile == null)
                return RecommendationContextDecision.None;

            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool hasActiveRecommendationContext =
                profile.HasActiveRecommendationContext &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0;

            bool hasCurrentRecommendationBase =
                profile.BaseRecommendedProducts != null &&
                profile.BaseRecommendedProducts.Count > 0;

            bool hasRecommendationContext = hasActiveRecommendationContext || hasCurrentRecommendationBase;

            bool previousFlowWasLookupOrSearch =
                string.Equals(previousActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(previousActiveFlow, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase);

            bool hasNewUseCaseSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour;

            bool hasBudgetSignal =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None;

            bool hasHardConstraintSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.ExcludedBrands.Any() ||
                parsedIntent.ExcludedCategories.Any();

            bool hasStrongPreferenceSignal =
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.RequestedStyles.Any();

            bool looksFreshRestart =
                RecommendationConversationRules.LooksLikeFreshRecommendationRestart(message, parsedIntent);

            bool looksRecommendationFollowUp =
                RecommendationConversationRules.LooksLikeRecommendationFollowUp(message, parsedIntent, profile);

            bool looksStrongFreshStandaloneRecommendation =
                text.StartsWith("tư vấn ") ||
                text.StartsWith("tu van ") ||
                text.StartsWith("xe ") ||
                text.StartsWith("case ") ||
                text.StartsWith("mình muốn ") ||
                text.StartsWith("minh muon ") ||
                text.StartsWith("mình cần ") ||
                text.StartsWith("minh can ") ||
                text.StartsWith("cho nữ") ||
                text.StartsWith("cho nu") ||
                text.StartsWith("cho nam") ||
                text.StartsWith("đi làm") ||
                text.StartsWith("di lam") ||
                text.StartsWith("đi học") ||
                text.StartsWith("di hoc");

            bool hasEnoughFreshSignals =
                hasNewUseCaseSignal ||
                hasBudgetSignal ||
                hasHardConstraintSignal ||
                hasStrongPreferenceSignal;

            bool currentMessageIntroducesNewUseCase = CurrentMessageIntroducesNewUseCase(message);

            bool currentMessageExplicitlyAddsPreference =
    text.Contains("cốp rộng") ||
    text.Contains("cop rong") ||
    text.Contains("tiết kiệm xăng") ||
    text.Contains("tiet kiem xang") ||
    text.Contains("dễ chống chân") ||
    text.Contains("de chong chan") ||
    text.Contains("dễ đi") ||
    text.Contains("de di") ||
    text.Contains("yên thấp") ||
    text.Contains("yen thap");

            bool isBudgetPivotWithinCurrentGoal =
                hasRecommendationContext &&
                hasBudgetSignal &&
                !currentMessageIntroducesNewUseCase &&
                !hasHardConstraintSignal &&
                !currentMessageExplicitlyAddsPreference &&
                (
                    RecommendationConversationRules.LooksLikeBudgetPivotFollowUp(message) ||
                    text.Contains("không phải") ||
                    text.Contains("khong phai") ||
                    text.Contains("giờ") ||
                    text.Contains("gio") ||
                    text.Contains("quanh") ||
                    text.Contains("khoảng") ||
                    text.Contains("khoang") ||
                    text.Contains("tầm") ||
                    text.Contains("tam")
                );

            bool looksStandaloneFresh =
                text.StartsWith("tư vấn") ||
                text.StartsWith("tu van") ||
                text.StartsWith("xe ") ||
                text.StartsWith("cho mình ") ||
                text.StartsWith("cho minh ") ||
                text.StartsWith("mình cần ") ||
                text.StartsWith("minh can ") ||
                text.StartsWith("tôi muốn ") ||
                text.StartsWith("toi muon ") ||
                text.StartsWith("cho nữ ") ||
                text.StartsWith("cho nu ") ||
                text.StartsWith("cho nam ");

            bool looksShortFollowUpFragment =
                text.StartsWith("nếu ") ||
                text.StartsWith("neu ") ||
                text.StartsWith("ưu tiên ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("chỉ ") ||
                text.StartsWith("chi ") ||
                text.StartsWith("bỏ ") ||
                text.StartsWith("bo ") ||
                text.StartsWith("không thích ") ||
                text.StartsWith("khong thich ") ||
                text.StartsWith("không muốn ") ||
                text.StartsWith("khong muon ") ||
                text.StartsWith("quanh ") ||
                text.StartsWith("khoảng ") ||
                text.StartsWith("khoang ") ||
                text.StartsWith("tầm ") ||
                text.StartsWith("tam ");

            bool looksComparativeQuestion =
                text.Contains(" hơn") ||
                text.Contains(" hon") ||
                text.Contains("nào hơn") ||
                text.Contains("nao hon") ||
                text.Contains("thì sao") ||
                text.Contains("thi sao") ||
                !string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature);

            if (isBudgetPivotWithinCurrentGoal)
            {
                return RecommendationContextDecision.ExpandFromCurrentGoal;
            }

            if (looksFreshRestart)
            {
                return RecommendationContextDecision.StartFreshRecommendation;
            }

            if (looksStrongFreshStandaloneRecommendation && hasEnoughFreshSignals)
            {
                return RecommendationContextDecision.StartFreshRecommendation;
            }

            if (parsedIntent.HasFreshConsultationSignal)
            {
                return RecommendationContextDecision.StartFreshRecommendation;
            }

            if (previousFlowWasLookupOrSearch &&
                looksStandaloneFresh &&
                hasEnoughFreshSignals)
            {
                return RecommendationContextDecision.StartFreshRecommendation;
            }

            if (!hasRecommendationContext)
            {
                return RecommendationContextDecision.None;
            }

            if (looksRecommendationFollowUp)
            {
                bool looksBudgetPivot =
                    isBudgetPivotWithinCurrentGoal ||
                    RecommendationConversationRules.LooksLikeBudgetPivotFollowUp(message);

                if (looksBudgetPivot)
                {
                    return RecommendationContextDecision.ExpandFromCurrentGoal;
                }
                bool asksAlternativeChoice =
    text.Contains("loại khác") ||
    text.Contains("loai khac") ||
    text.Contains("xe khác") ||
    text.Contains("xe khac") ||
    text.Contains("mẫu khác") ||
    text.Contains("mau khac");

                if (asksAlternativeChoice)
                {
                    return RecommendationContextDecision.ExpandFromCurrentGoal;
                }
                bool hasNarrowSignal =
                    parsedIntent.HasNarrowRefinementSignal ||
                    !string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature) ||
                    (
                        (text.Contains("hơn") || text.Contains("hon")) &&
                        !parsedIntent.PriceMin.HasValue &&
                        !parsedIntent.PriceMax.HasValue &&
                        !parsedIntent.TargetPrice.HasValue
                    );

                if (hasNarrowSignal)
                {
                    return RecommendationContextDecision.NarrowWithinCurrentSet;
                }

                return RecommendationContextDecision.ExpandFromCurrentGoal;
            }

            if (parsedIntent.HasExpandRecommendationSignal)
            {
                return RecommendationContextDecision.ExpandFromCurrentGoal;
            }

            if (looksShortFollowUpFragment && (hasBudgetSignal || hasHardConstraintSignal || hasStrongPreferenceSignal))
            {
                return RecommendationContextDecision.ExpandFromCurrentGoal;
            }

            if (parsedIntent.HasNarrowRefinementSignal)
            {
                return RecommendationContextDecision.NarrowWithinCurrentSet;
            }

            if (looksComparativeQuestion)
            {
                return RecommendationContextDecision.NarrowWithinCurrentSet;
            }

            return RecommendationContextDecision.None;
        }

        private static bool CurrentMessageIntroducesNewUseCase(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return
                text.Contains("cho nam") ||
                text.Contains("cho nữ") ||
                text.Contains("cho nu") ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("đi học") ||
                text.Contains("di hoc") ||
                text.Contains("đi phố") ||
                text.Contains("di pho") ||
                text.Contains("đường dài") ||
                text.Contains("duong dai") ||
                text.Contains("xe ga") ||
                text.Contains("xe số") ||
                text.Contains("xe so") ||
                text.Contains("côn tay") ||
                text.Contains("con tay");
        }

        private static bool IsFollowUpPreferenceFragment(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.StartsWith("ưu tiên ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("nếu ") ||
                text.StartsWith("neu ") ||
                text.StartsWith("rẻ hơn") ||
                text.StartsWith("re hon") ||
                text.StartsWith("đắt hơn") ||
                text.StartsWith("dat hon") ||
                text.StartsWith("xe ga") ||
                text.StartsWith("xe số") ||
                text.StartsWith("xe so") ||
                text.StartsWith("honda") ||
                text.StartsWith("yamaha") ||
                text.StartsWith("suzuki") ||
                text.StartsWith("sym") ||
                text.StartsWith("piaggio");
        }
    }
}