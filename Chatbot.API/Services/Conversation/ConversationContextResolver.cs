using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Conversation
{
    public class ConversationContextResolver : IConversationContextResolver
    {
        public ContextResolutionResult Resolve(
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile existingProfile,
            string? previousActiveFlow = null)
        {
            existingProfile ??= new CustomerPreferenceProfile();
            if (LooksLikeBudgetRestartWithSameGoal(normalizedMessage, parsedIntent, existingProfile))
            {
                var restartIntent = parsedIntent.Clone();
                var explicitRestartTarget = ResolveExplicitGenderTarget(normalizedMessage);
                bool hasExplicitRestartTarget = !string.IsNullOrWhiteSpace(explicitRestartTarget);

                if (hasExplicitRestartTarget)
                {
                    restartIntent.Target = explicitRestartTarget;

                    if (string.Equals(explicitRestartTarget, "nam", StringComparison.OrdinalIgnoreCase))
                    {
                        restartIntent.PrefersMaleStyle = true;
                        restartIntent.PrefersFemaleStyle = false;
                    }
                    else if (string.Equals(explicitRestartTarget, "nữ", StringComparison.OrdinalIgnoreCase))
                    {
                        restartIntent.PrefersFemaleStyle = true;
                        restartIntent.PrefersMaleStyle = false;
                    }
                }
                else if (string.IsNullOrWhiteSpace(restartIntent.Target))
                {
                    restartIntent.Target = existingProfile.Target;
                    restartIntent.PrefersMaleStyle = existingProfile.PrefersMaleStyle;
                    restartIntent.PrefersFemaleStyle = existingProfile.PrefersFemaleStyle;
                }

                if (!restartIntent.ForWork)
                    restartIntent.ForWork = existingProfile.ForWork;

                if (!restartIntent.ForSchool)
                    restartIntent.ForSchool = existingProfile.ForSchool;

                if (!restartIntent.ForCity)
                    restartIntent.ForCity = existingProfile.ForCity;

                if (!restartIntent.ForTour)
                    restartIntent.ForTour = existingProfile.ForTour;

                if (!restartIntent.WantsFuelSaving)
                    restartIntent.WantsFuelSaving = existingProfile.WantsFuelSaving;

                if (!restartIntent.WantsLargeStorage)
                    restartIntent.WantsLargeStorage = existingProfile.WantsLargeStorage;

                if (!restartIntent.WantsEasyControl)
                    restartIntent.WantsEasyControl = existingProfile.WantsEasyControl;

                if (!restartIntent.NeedsLowSeat)
                    restartIntent.NeedsLowSeat = existingProfile.NeedsLowSeat;

                if (!restartIntent.HeightCm.HasValue && existingProfile.HeightCm.HasValue)
                    restartIntent.HeightCm = existingProfile.HeightCm;

                return new ContextResolutionResult
                {
                    EffectiveIntent = restartIntent,
                    ContextDecision = RecommendationContextDecision.StartFreshRecommendation,
                    ShouldResetContext = true,
                    ShouldPreserveBudgetOnlyContext = false
                };
            }
            if (LooksLikeBudgetPivotQuestion(normalizedMessage, parsedIntent, existingProfile))
            {
                var pivotIntent = parsedIntent.Clone();

                if (string.IsNullOrWhiteSpace(pivotIntent.Target))
                    pivotIntent.Target = existingProfile.Target;

                pivotIntent.PrefersMaleStyle = pivotIntent.PrefersMaleStyle || existingProfile.PrefersMaleStyle;
                pivotIntent.PrefersFemaleStyle = pivotIntent.PrefersFemaleStyle || existingProfile.PrefersFemaleStyle;

                if (!pivotIntent.ForWork)
                    pivotIntent.ForWork = existingProfile.ForWork;

                if (!pivotIntent.ForSchool)
                    pivotIntent.ForSchool = existingProfile.ForSchool;

                if (!pivotIntent.ForCity)
                    pivotIntent.ForCity = existingProfile.ForCity;

                if (!pivotIntent.ForTour)
                    pivotIntent.ForTour = existingProfile.ForTour;

                if (!pivotIntent.WantsFuelSaving)
                    pivotIntent.WantsFuelSaving = existingProfile.WantsFuelSaving;

                if (!pivotIntent.WantsLargeStorage)
                    pivotIntent.WantsLargeStorage = existingProfile.WantsLargeStorage;

                if (!pivotIntent.WantsEasyControl)
                    pivotIntent.WantsEasyControl = existingProfile.WantsEasyControl;

                if (!pivotIntent.NeedsLowSeat)
                    pivotIntent.NeedsLowSeat = existingProfile.NeedsLowSeat;

                if (!pivotIntent.HeightCm.HasValue && existingProfile.HeightCm.HasValue)
                    pivotIntent.HeightCm = existingProfile.HeightCm;

                if (string.IsNullOrWhiteSpace(pivotIntent.Brand))
                    pivotIntent.Brand = existingProfile.PreferredBrand;

                if (string.IsNullOrWhiteSpace(pivotIntent.Category))
                    pivotIntent.Category = existingProfile.PreferredCategory;

                return new ContextResolutionResult
                {
                    EffectiveIntent = pivotIntent,
                    ContextDecision = RecommendationContextDecision.ExpandFromCurrentGoal,
                    ShouldResetContext = false,
                    ShouldPreserveBudgetOnlyContext = false
                };
            }
            bool isOnlyPriceChangeFromUserInput =
                (parsedIntent.TargetPrice.HasValue ||
                 parsedIntent.PriceMin.HasValue ||
                 parsedIntent.PriceMax.HasValue ||
                 parsedIntent.FilterType != PriceFilterType.None) &&
                string.IsNullOrWhiteSpace(parsedIntent.Target) &&
                string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                !parsedIntent.ForWork &&
                !parsedIntent.ForSchool &&
                !parsedIntent.ForCity &&
                !parsedIntent.ForTour &&
                !parsedIntent.WantsFuelSaving &&
                !parsedIntent.WantsLargeStorage &&
                !parsedIntent.WantsEasyControl &&
                !parsedIntent.NeedsLowSeat &&
                !parsedIntent.ExcludedBrands.Any() &&
                !parsedIntent.ExcludedCategories.Any() &&
                !parsedIntent.RequestedStyles.Any();

            bool isOnlyPriceChangeByText = LooksLikePureBudgetChangeText(normalizedMessage);
            bool shouldPreserveContextForBudgetOnly =
                isOnlyPriceChangeFromUserInput || isOnlyPriceChangeByText;

            var contextDecision = RecommendationContextRules.DecideRecommendationContextAction(
    normalizedMessage,
    parsedIntent,
    existingProfile,
    previousActiveFlow);

            var effectiveIntent = parsedIntent.Clone();
            var explicitTarget = ResolveExplicitGenderTarget(normalizedMessage);
            bool hasExplicitTarget = !string.IsNullOrWhiteSpace(explicitTarget);

            if (hasExplicitTarget)
            {
                effectiveIntent.Target = explicitTarget;

                if (string.Equals(explicitTarget, "nam", StringComparison.OrdinalIgnoreCase))
                {
                    effectiveIntent.PrefersMaleStyle = true;
                    effectiveIntent.PrefersFemaleStyle = false;
                }
                else if (string.Equals(explicitTarget, "nữ", StringComparison.OrdinalIgnoreCase))
                {
                    effectiveIntent.PrefersFemaleStyle = true;
                    effectiveIntent.PrefersMaleStyle = false;
                }
            }

            if (shouldPreserveContextForBudgetOnly && !hasExplicitTarget)
            {
                effectiveIntent.Target = existingProfile.Target;
                effectiveIntent.PrefersMaleStyle = existingProfile.PrefersMaleStyle;
                effectiveIntent.PrefersFemaleStyle = existingProfile.PrefersFemaleStyle;
            }

            if (!string.IsNullOrWhiteSpace(effectiveIntent.Category))
            {
                effectiveIntent.ExcludedCategories.RemoveWhere(x =>
                    string.Equals(x, effectiveIntent.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(effectiveIntent.Brand))
            {
                effectiveIntent.ExcludedBrands.RemoveWhere(x =>
                    string.Equals(x, effectiveIntent.Brand, StringComparison.OrdinalIgnoreCase));
            }

            if (contextDecision == RecommendationContextDecision.ExpandFromCurrentGoal)
            {
                bool currentTurnHasBudgetSignal =
                    parsedIntent.TargetPrice.HasValue ||
                    parsedIntent.PriceMin.HasValue ||
                    parsedIntent.PriceMax.HasValue ||
                    parsedIntent.FilterType != PriceFilterType.None;

                if (currentTurnHasBudgetSignal)
                {
                    effectiveIntent.TargetPrice = parsedIntent.TargetPrice;
                    effectiveIntent.PriceMin = parsedIntent.PriceMin;
                    effectiveIntent.PriceMax = parsedIntent.PriceMax;
                    effectiveIntent.FilterType = parsedIntent.FilterType;
                }

                if (shouldPreserveContextForBudgetOnly)
                {
                    effectiveIntent.Target = existingProfile.Target;
                }
                else if (string.IsNullOrWhiteSpace(effectiveIntent.Target))
                {
                    effectiveIntent.Target = existingProfile.Target;
                }

                if (!effectiveIntent.ForWork)
                    effectiveIntent.ForWork = existingProfile.ForWork;

                if (!effectiveIntent.ForSchool)
                    effectiveIntent.ForSchool = existingProfile.ForSchool;

                if (!effectiveIntent.ForCity)
                    effectiveIntent.ForCity = existingProfile.ForCity;

                if (!effectiveIntent.ForTour)
                    effectiveIntent.ForTour = existingProfile.ForTour;

                if (!effectiveIntent.WantsFuelSaving)
                    effectiveIntent.WantsFuelSaving = existingProfile.WantsFuelSaving;

                if (!effectiveIntent.WantsLargeStorage)
                    effectiveIntent.WantsLargeStorage = existingProfile.WantsLargeStorage;

                if (!effectiveIntent.WantsEasyControl)
                    effectiveIntent.WantsEasyControl = existingProfile.WantsEasyControl;

                if (!effectiveIntent.NeedsLowSeat)
                    effectiveIntent.NeedsLowSeat = existingProfile.NeedsLowSeat;

                if (!effectiveIntent.HeightCm.HasValue && existingProfile.HeightCm.HasValue)
                    effectiveIntent.HeightCm = existingProfile.HeightCm;

                if (string.IsNullOrWhiteSpace(effectiveIntent.Category))
                    effectiveIntent.Category = existingProfile.PreferredCategory;

                if (string.IsNullOrWhiteSpace(effectiveIntent.Brand))
                    effectiveIntent.Brand = existingProfile.PreferredBrand;

                if (!currentTurnHasBudgetSignal)
                {
                    if (!effectiveIntent.TargetPrice.HasValue && existingProfile.TargetPrice.HasValue)
                        effectiveIntent.TargetPrice = existingProfile.TargetPrice;

                    if (!effectiveIntent.PriceMin.HasValue && existingProfile.PriceMin.HasValue)
                        effectiveIntent.PriceMin = existingProfile.PriceMin;

                    if (!effectiveIntent.PriceMax.HasValue && existingProfile.PriceMax.HasValue)
                        effectiveIntent.PriceMax = existingProfile.PriceMax;

                    if (effectiveIntent.FilterType == PriceFilterType.None &&
                        existingProfile.FilterType != PriceFilterType.None)
                    {
                        effectiveIntent.FilterType = existingProfile.FilterType;
                    }
                }

                var preservedExcludedBrands = existingProfile.ExcludedBrands
                    .Where(x => string.IsNullOrWhiteSpace(effectiveIntent.Brand) ||
                                !string.Equals(x, effectiveIntent.Brand, StringComparison.OrdinalIgnoreCase));

                var preservedExcludedCategories = existingProfile.ExcludedCategories
                    .Where(x => string.IsNullOrWhiteSpace(effectiveIntent.Category) ||
                                !string.Equals(x, effectiveIntent.Category, StringComparison.OrdinalIgnoreCase));

                effectiveIntent.ExcludedBrands.UnionWith(preservedExcludedBrands);
                effectiveIntent.ExcludedCategories.UnionWith(preservedExcludedCategories);
                effectiveIntent.RequestedStyles.UnionWith(existingProfile.RequestedStyles);
            }

            bool shouldResetContext =
    !shouldPreserveContextForBudgetOnly &&
    (contextDecision == RecommendationContextDecision.StartFreshRecommendation ||
     RecommendationContextRules.ShouldResetContextForFreshConsultation(
         normalizedMessage,
         parsedIntent,
         existingProfile));

            return new ContextResolutionResult
            {
                EffectiveIntent = effectiveIntent,
                ContextDecision = contextDecision,
                ShouldResetContext = shouldResetContext,
                ShouldPreserveBudgetOnlyContext = shouldPreserveContextForBudgetOnly
            };
        }
        private static string? ResolveExplicitGenderTarget(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var text = message.Trim().ToLowerInvariant();

            bool mentionsMale =
     text.Contains("cho nam") ||
     text.EndsWith(" nam") ||
     text.Contains(" xe nam") ||
     text.Contains(" tư vấn xe cho nam") ||
     text.Contains(" tư vấn cho nam") ||
     text.Contains(" tư vấn nam") ||
     text.Contains(" xe cho nam") ||
     text.Contains(" muốn xe nam") ||
     text.Contains(" giờ tư vấn xe cho nam");

            bool mentionsFemale =
                text.Contains("cho nữ") ||
                text.Contains("cho nu") ||
                text.EndsWith(" nữ") ||
                text.EndsWith(" nu") ||
                text.Contains(" xe nữ") ||
                text.Contains(" xe nu") ||
                text.Contains(" tư vấn xe cho nữ") ||
                text.Contains(" tư vấn xe cho nu") ||
                text.Contains(" tư vấn nữ") ||
                text.Contains(" tư vấn nu") ||
                text.Contains(" xe cho nữ") ||
                text.Contains(" xe cho nu") ||
                text.Contains(" muốn xe nữ") ||
                text.Contains(" muốn xe nu") ||
                text.Contains(" giờ tư vấn xe cho nữ") ||
                text.Contains(" giờ tư vấn xe cho nu");
            // nếu câu có cả "không phải nữ nữa" và "cho nam" thì ưu tiên target mới ở phía sau
            if (text.Contains("không phải nữ") || text.Contains("khong phai nu"))
            {
                if (mentionsMale) return "nam";
            }

            if (text.Contains("không phải nam") || text.Contains("khong phai nam"))
            {
                if (mentionsFemale) return "nữ";
            }

            if (mentionsMale && !mentionsFemale) return "nam";
            if (mentionsFemale && !mentionsMale) return "nữ";

            return null;
        }

        

        private static bool LooksLikePureBudgetChangeText(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetSignal =
                text.Contains("không phải") ||
                text.Contains("khong phai") ||
                text.Contains("giờ") ||
                text.Contains("gio") ||
                text.Contains("xuống") ||
                text.Contains("xuong") ||
                text.Contains("tầm") ||
                text.Contains("tam") ||
                text.Contains("khoảng") ||
                text.Contains("khoang") ||
                text.Contains("quanh") ||
                Regex.IsMatch(text, @"\b\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase);

            bool hasExplicitGoalChange =
                text.Contains("cho nữ") ||
                text.Contains("cho nu") ||
                text.Contains("cho nam") ||
                text.Contains("xe ga") ||
                text.Contains("xe số") ||
                text.Contains("xe so") ||
                text.Contains("côn tay") ||
                text.Contains("con tay") ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("đi học") ||
                text.Contains("di hoc") ||
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio");

            return hasBudgetSignal && !hasExplicitGoalChange;
        }

        

        
        private static bool LooksLikeBudgetRestartWithSameGoal(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(message) || profile == null)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasRestartPhrase =
    text.Contains("đổi ý") ||
    text.Contains("doi y") ||
    text.Contains("không phải") ||
    text.Contains("khong phai") ||
    text.Contains("chứ không phải") ||
    text.Contains("chu khong phai") ||
    text.StartsWith("giờ ") ||
    text.StartsWith("gio ") ||
    text.StartsWith("h t ") ||
    text.Contains("muốn mua") ||
    text.Contains("muon mua") ||
    text.Contains("đổi sang") ||
    text.Contains("doi sang");

            bool hasBudgetSignal =
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None;

            bool hadOldRecommendationGoal =
                profile.HasActiveRecommendationContext &&
                (
                    !string.IsNullOrWhiteSpace(profile.Target) ||
                    profile.ForWork ||
                    profile.ForSchool ||
                    profile.ForCity ||
                    profile.ForTour
                );

            return hasRestartPhrase && hasBudgetSignal && hadOldRecommendationGoal;
        }
        private static bool LooksLikeBudgetPivotQuestion(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(message) || profile == null)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasRecommendationContext =
                profile.HasActiveRecommendationContext &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0;

            if (!hasRecommendationContext)
                return false;

            bool hasBudgetSignal =
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None;

            if (!hasBudgetSignal)
                return false;

            bool startsLikeBudgetPivot =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.StartsWith("thế ") ||
                text.StartsWith("the ") ||
                text.StartsWith("vậy ") ||
                text.StartsWith("vay ");

            bool containsBudgetPivotPhrase =
                text.Contains("xe 30 triệu") ||
                text.Contains("xe 30 trieu") ||
                text.Contains("xe khoảng") ||
                text.Contains("xe khoang") ||
                text.Contains("xe tầm") ||
                text.Contains("xe tam");

            bool asksComparisonStyleQuestion =
                text.Contains("thì sao") ||
                text.Contains("thi sao");

            bool hasNoNewHardConstraint =
                string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                string.IsNullOrWhiteSpace(parsedIntent.Target) &&
                !parsedIntent.ForWork &&
                !parsedIntent.ForSchool &&
                !parsedIntent.ForCity &&
                !parsedIntent.ForTour &&
                !parsedIntent.WantsFuelSaving &&
                !parsedIntent.WantsLargeStorage &&
                !parsedIntent.WantsEasyControl &&
                !parsedIntent.NeedsLowSeat &&
                !parsedIntent.ExcludedBrands.Any() &&
                !parsedIntent.ExcludedCategories.Any() &&
                !parsedIntent.RequestedStyles.Any();

            return (startsLikeBudgetPivot || containsBudgetPivotPhrase) &&
                   asksComparisonStyleQuestion &&
                   hasNoNewHardConstraint;
        }
    }
}