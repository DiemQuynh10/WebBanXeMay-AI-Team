using System.Text.RegularExpressions;
using Chatbot.API.Helpers;
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
            parsedIntent ??= new ParsedIntent();
            normalizedMessage ??= string.Empty;

            if (parsedIntent.IsOutOfScope || parsedIntent.IsNoise || parsedIntent.IsGreeting || parsedIntent.IsAck)
            {
                return new ContextResolutionResult
                {
                    EffectiveIntent = parsedIntent.Clone(),
                    ContextDecision = RecommendationContextDecision.None,
                    ShouldResetContext = false,
                    ShouldPreserveBudgetOnlyContext = false
                };
            }

            EnsureProfileCollections(existingProfile);
            EnsureIntentCollections(parsedIntent);
            if (LooksLikeBudgetRestartWithSameGoal(normalizedMessage, parsedIntent, existingProfile))
            {
                var restartIntent = parsedIntent.Clone();
                EnsureIntentCollections(restartIntent);
                var explicitRestartTarget = ResolveExplicitGenderTarget(normalizedMessage, parsedIntent);
                bool hasExplicitRestartTarget = !string.IsNullOrWhiteSpace(explicitRestartTarget);

                if (hasExplicitRestartTarget)
                {
                    ApplyExplicitTarget(restartIntent, explicitRestartTarget!);
                }

                ApplyMissingContextFromProfile(
    restartIntent,
    existingProfile,
    includeBudget: false,
    includeBrandCategory: false,
    includeTarget: !hasExplicitRestartTarget,
    includeSoftPreferences: true);

                RemoveResolvedExclusions(restartIntent);

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
                EnsureIntentCollections(pivotIntent);
                ApplyMissingContextFromProfile(
     pivotIntent,
     existingProfile,
     includeBudget: false,
     includeBrandCategory: true,
     includeTarget: true,
     includeSoftPreferences: true);

                if (string.IsNullOrWhiteSpace(pivotIntent.Target))
                    pivotIntent.Target = existingProfile.Target;

                pivotIntent.PrefersMaleStyle = pivotIntent.PrefersMaleStyle || existingProfile.PrefersMaleStyle;
                pivotIntent.PrefersFemaleStyle = pivotIntent.PrefersFemaleStyle || existingProfile.PrefersFemaleStyle;

                RemoveResolvedExclusions(pivotIntent);

                return new ContextResolutionResult
                {
                    EffectiveIntent = pivotIntent,
                    ContextDecision = RecommendationContextDecision.ExpandFromCurrentGoal,
                    ShouldResetContext = false,
                    ShouldPreserveBudgetOnlyContext = false
                };
            }

            bool isOnlyPriceChangeFromUserInput = IsOnlyPriceChangeFromIntent(parsedIntent);
            bool isOnlyPriceChangeByText = LooksLikePureBudgetChangeText(normalizedMessage, parsedIntent);
            bool shouldPreserveContextForBudgetOnly = isOnlyPriceChangeFromUserInput || isOnlyPriceChangeByText;

            var contextDecision = RecommendationContextRules.DecideRecommendationContextAction(
                normalizedMessage,
                parsedIntent,
                existingProfile,
                previousActiveFlow);

            var effectiveIntent = parsedIntent.Clone();
            EnsureIntentCollections(effectiveIntent);
            bool shouldCarryBudgetBase =
                ShouldCarryBudgetBase(parsedIntent, normalizedMessage, existingProfile);

            bool shouldCarryIdentityBase =
                ShouldCarryIdentityBase(parsedIntent, normalizedMessage, existingProfile, previousActiveFlow);

            ApplyMissingContextFromProfile(
                effectiveIntent,
                existingProfile,
                includeBudget: shouldCarryBudgetBase,
                includeBrandCategory: shouldCarryIdentityBase,
                includeTarget: shouldCarryIdentityBase,
                includeSoftPreferences: true);

            MergePreferenceCollectionsFromProfile(effectiveIntent, existingProfile);

            effectiveIntent = EnrichFollowUpIntent(
                effectiveIntent,
                existingProfile,
                normalizedMessage);

            ApplyCurrentTurnExclusionOverride(parsedIntent, effectiveIntent);

            var explicitTarget = ResolveExplicitGenderTarget(normalizedMessage, parsedIntent);
            bool hasExplicitTarget = !string.IsNullOrWhiteSpace(explicitTarget);
            if (hasExplicitTarget)
            {
                ApplyExplicitTarget(effectiveIntent, explicitTarget!);
            }

            if (shouldPreserveContextForBudgetOnly && !hasExplicitTarget)
            {
                effectiveIntent.Target = existingProfile.Target;
                effectiveIntent.PrefersMaleStyle = existingProfile.PrefersMaleStyle;
                effectiveIntent.PrefersFemaleStyle = existingProfile.PrefersFemaleStyle;
            }

            ApplyCurrentTurnExclusionOverride(parsedIntent, effectiveIntent);
            RemoveResolvedExclusions(effectiveIntent);

            if (contextDecision == RecommendationContextDecision.ExpandFromCurrentGoal)
            {
                bool currentTurnHasBudgetSignal = HasAnyBudgetSignal(parsedIntent);

                bool currentTurnHasIdentitySignal =
    !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
    !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
    !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
    !string.IsNullOrWhiteSpace(ResolveExplicitGenderTarget(normalizedMessage, parsedIntent));

                ApplyMissingContextFromProfile(
                    effectiveIntent,
                    existingProfile,
                    includeBudget: !currentTurnHasBudgetSignal,
                    includeBrandCategory: !currentTurnHasIdentitySignal,
                    includeTarget: !currentTurnHasIdentitySignal,
                    includeSoftPreferences: true);

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
                    effectiveIntent.PrefersMaleStyle = existingProfile.PrefersMaleStyle;
                    effectiveIntent.PrefersFemaleStyle = existingProfile.PrefersFemaleStyle;
                }

                var preservedExcludedBrands = (existingProfile.ExcludedBrands ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
     .Where(x => string.IsNullOrWhiteSpace(effectiveIntent.Brand) ||
                 !string.Equals(x, effectiveIntent.Brand, StringComparison.OrdinalIgnoreCase));

                var preservedExcludedCategories = (existingProfile.ExcludedCategories ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase))
                    .Where(x => string.IsNullOrWhiteSpace(effectiveIntent.Category) ||
                                !string.Equals(x, effectiveIntent.Category, StringComparison.OrdinalIgnoreCase));

                effectiveIntent.ExcludedBrands.UnionWith(preservedExcludedBrands);
                effectiveIntent.ExcludedCategories.UnionWith(preservedExcludedCategories);
                effectiveIntent.RequestedStyles.UnionWith(
                    existingProfile.RequestedStyles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                ApplyCurrentTurnExclusionOverride(parsedIntent, effectiveIntent);
                RemoveResolvedExclusions(effectiveIntent);
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

        private static string? ResolveExplicitGenderTarget(string message, ParsedIntent? parsedIntent = null)
        {
            if (!string.IsNullOrWhiteSpace(parsedIntent?.Target))
            {
                var target = parsedIntent.Target.Trim().ToLowerInvariant();
                if (target is "nam" or "nữ" or "nu")
                    return target == "nu" ? "nữ" : target;
            }

            if (string.IsNullOrWhiteSpace(message))
                return null;

            var text = message.Trim().ToLowerInvariant();

            bool mentionsMale =
                ContainsAny(text,
                    "cho nam", "xe cho nam", "muốn xe nam", "muon xe nam", "tư vấn cho nam", "tu van cho nam",
                    "tư vấn xe cho nam", "tu van xe cho nam", "giờ tư vấn xe cho nam", "gio tu van xe cho nam") ||
                Regex.IsMatch(text, @"\b(nam tính|manly|dáng nam|dang nam)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            bool mentionsFemale =
                ContainsAny(text,
                    "cho nữ", "cho nu", "xe cho nữ", "xe cho nu", "muốn xe nữ", "muon xe nu", "tư vấn cho nữ", "tu van cho nu",
                    "tư vấn xe cho nữ", "tu van xe cho nu", "giờ tư vấn xe cho nữ", "gio tu van xe cho nu") ||
                Regex.IsMatch(text, @"\b(nữ tính|nu tinh|dáng nữ|dang nu)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (ContainsAny(text, "không phải nữ", "khong phai nu", "bớt nữ", "bot nu", "đỡ nữ", "do nu") && mentionsMale)
                return "nam";

            if (ContainsAny(text, "không phải nam", "khong phai nam", "bớt nam", "bot nam", "đỡ nam", "do nam") && mentionsFemale)
                return "nữ";

            if (mentionsMale && !mentionsFemale) return "nam";
            if (mentionsFemale && !mentionsMale) return "nữ";

            return null;
        }
        private static bool ShouldCarryBudgetBase(
    ParsedIntent parsedIntent,
    string normalizedMessage,
    CustomerPreferenceProfile profile)
        {
            if (parsedIntent == null || profile == null)
                return false;

            if (HasAnyBudgetSignal(parsedIntent))
                return false;

            if (LooksLikeHardContextReset(normalizedMessage, parsedIntent))
                return false;

            bool hasRecommendationContext =
                profile.HasActiveRecommendationContext &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0;

            bool hasLookupContext =
                !string.IsNullOrWhiteSpace(profile.LastLookupProductName);

            bool hasCompareContext =
                profile.HasActiveCompareContext &&
                profile.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 2;

            bool looksLikeFollowUp =
                parsedIntent.IsFollowUp ||
                !string.IsNullOrWhiteSpace(parsedIntent.FollowUpType) ||
                parsedIntent.HasExpandRecommendationSignal ||
                parsedIntent.HasNarrowRefinementSignal ||
               LooksLikeFollowUpReference(normalizedMessage);

            return looksLikeFollowUp || hasRecommendationContext || hasLookupContext || hasCompareContext;
        }
        private static bool ShouldCarryIdentityBase(
    ParsedIntent parsedIntent,
    string normalizedMessage,
    CustomerPreferenceProfile profile,
    string? previousActiveFlow)
        {
            if (parsedIntent == null || profile == null)
                return false;

            if (LooksLikeHardContextReset(normalizedMessage, parsedIntent))
                return false;

            if (!string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target))
            {
                return false;
            }

            bool hasExplicitGenderTarget =
                !string.IsNullOrWhiteSpace(ResolveExplicitGenderTarget(normalizedMessage, parsedIntent));

            if (hasExplicitGenderTarget)
                return false;

            bool looksLikeFollowUp =
                parsedIntent.IsFollowUp ||
                !string.IsNullOrWhiteSpace(parsedIntent.FollowUpType) ||
                parsedIntent.HasExpandRecommendationSignal ||
                parsedIntent.HasNarrowRefinementSignal ||
                LooksLikeFollowUpReference(normalizedMessage);

            bool currentlyInRecommendationFamily =
                string.Equals(previousActiveFlow, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(previousActiveFlow, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase);

            return looksLikeFollowUp && currentlyInRecommendationFamily;
        }
        private ParsedIntent EnrichFollowUpIntent(
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile,
            string normalizedMessage) 
        {
            if (parsedIntent == null || profile == null)
                return parsedIntent;

            bool alreadySpecificAction =
                string.Equals(parsedIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parsedIntent.IntentType, "product_lookup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parsedIntent.IntentType, "product_search", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parsedIntent.IntentType, "order_lookup", StringComparison.OrdinalIgnoreCase);

            if (alreadySpecificAction && !string.Equals(parsedIntent.IntentType, "followup", StringComparison.OrdinalIgnoreCase))
                return parsedIntent;

            bool looksLikeFreshRestart =
                FollowUpHeuristics.LooksLikeFreshRecommendationRequest(normalizedMessage) ||
                LooksLikeHardContextReset(normalizedMessage, parsedIntent);

            if (looksLikeFreshRestart)
                return parsedIntent;

            if (profile.HasActiveCompareContext &&
                profile.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 2)
            {
                if (FollowUpHeuristics.LooksLikeCompareUseCaseFollowUp(normalizedMessage) ||
                    IsCompareFollowUp(parsedIntent, normalizedMessage))
                {
                    parsedIntent.IntentType = "compare";
                    parsedIntent.ComparisonFeature ??= InferComparisonFeature(normalizedMessage);

                    foreach (var product in profile.LastComparedProducts)
                    {
                        if (!parsedIntent.MentionedProducts.Contains(product, StringComparer.OrdinalIgnoreCase))
                        {
                            parsedIntent.MentionedProducts.Add(product);
                        }
                    }

                    return parsedIntent;
                }
            }

            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName) &&
                IsLookupFollowUp(parsedIntent, normalizedMessage))
            {
                parsedIntent.IntentType = "product_lookup";

                if (!parsedIntent.MentionedProducts.Contains(profile.LastLookupProductName, StringComparer.OrdinalIgnoreCase))
                {
                    parsedIntent.MentionedProducts.Add(profile.LastLookupProductName);
                }

                return parsedIntent;
            }

            if (!string.IsNullOrWhiteSpace(profile.ActiveFlow) &&
                string.Equals(profile.ActiveFlow, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) &&
                IsRecommendationFollowUp(parsedIntent, normalizedMessage))
            {
                parsedIntent.IntentType = string.Equals(parsedIntent.IntentType, "product_search", StringComparison.OrdinalIgnoreCase)
                    ? "product_search"
                    : "recommend";

                return parsedIntent;
            }

            return parsedIntent;
        }

        private bool IsCompareFollowUp(ParsedIntent parsedIntent, string normalizedMessage)
        {
            EnsureIntentCollections(parsedIntent);

            if (string.Equals(parsedIntent.IntentType, "followup", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature))
                return true;

            if (parsedIntent.MentionedProducts?.Count >= 2)
                return true;

            return FollowUpHeuristics.LooksLikeCompareFollowUp(normalizedMessage)
                   || FollowUpHeuristics.LooksLikeCompareUseCaseFollowUp(normalizedMessage)
                   || Regex.IsMatch(normalizedMessage ?? string.Empty, @"\b(hơn|tot hon|tốt hơn|rộng hơn|rong hon|êm hơn|manh hon|mạnh hơn|tiết kiệm hơn|tiet kiem hon)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private bool IsLookupFollowUp(ParsedIntent parsedIntent, string normalizedMessage)
        {
            if (string.Equals(parsedIntent.IntentType, "followup", StringComparison.OrdinalIgnoreCase))
                return true;

            if (parsedIntent.MentionedProducts?.Count > 0 &&
                string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                string.IsNullOrWhiteSpace(parsedIntent.Category))
                return true;

            return FollowUpHeuristics.LooksLikeLookupFollowUp(normalizedMessage)
                   || Regex.IsMatch(normalizedMessage ?? string.Empty, @"\b(thông số|thong so|chi tiết|chi tiet|giá bao nhiêu|gia bao nhieu|trả góp|tra gop|màu gì|mau gi|còn hàng|con hang)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private bool IsRecommendationFollowUp(ParsedIntent parsedIntent, string normalizedMessage)
        {
            if (string.Equals(parsedIntent.IntentType, "followup", StringComparison.OrdinalIgnoreCase))
                return true;

            if (HasRefinementSignal(parsedIntent))
                return true;

            return FollowUpHeuristics.LooksLikeRecommendationFollowUp(normalizedMessage)
                   || Regex.IsMatch(normalizedMessage ?? string.Empty, @"\b(còn mẫu nào|con mau nao|còn xe nào|con xe nao|gợi ý thêm|goi y them|lọc tiếp|loc tiep|ưu tiên|uu tien|đỡ|do|bớt|bot)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool LooksLikePureBudgetChangeText(string message, ParsedIntent parsedIntent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (!HasAnyBudgetSignal(parsedIntent) &&
                !Regex.IsMatch(message, @"\b\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetAdjustPhrase =
                ContainsAny(text,
                    "không phải", "khong phai", "giờ", "gio", "xuống", "xuong", "tăng lên", "tang len",
                    "nhỉnh hơn", "nhinh hon", "cao hơn", "thấp hơn", "thap hon", "rẻ hơn", "re hon",
                    "đắt hơn", "dat hon", "tầm", "tam", "khoảng", "khoang", "quanh", "tầm giá", "tam gia");

            bool hasExplicitGoalChange = HasAnyNonBudgetConstraint(parsedIntent) ||
                ContainsAny(text,
                    "xe ga", "xe số", "xe so", "côn tay", "con tay", "đi làm", "di lam", "đi học", "di hoc",
                    "cốp rộng", "cop rong", "tiết kiệm xăng", "tiet kiem xang", "cho nam", "cho nữ", "cho nu");

            return hasBudgetAdjustPhrase && !hasExplicitGoalChange;
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
                ContainsAny(text,
                    "đổi ý", "doi y", "không phải", "khong phai", "chứ không phải", "chu khong phai",
                    "muốn mua", "muon mua", "đổi sang", "doi sang", "quay lại", "reset") ||
                text.StartsWith("giờ ") || text.StartsWith("gio ") || text.StartsWith("h t ") || text.StartsWith("nay ");

            bool hasBudgetSignal = HasAnyBudgetSignal(parsedIntent) ||
                Regex.IsMatch(text, @"\b\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            bool hadOldRecommendationGoal =
                profile.HasActiveRecommendationContext &&
                (!string.IsNullOrWhiteSpace(profile.Target) ||
                 profile.ForWork ||
                 profile.ForSchool ||
                 profile.ForCity ||
                 profile.ForTour ||
                 profile.WantsFuelSaving ||
                 profile.WantsLargeStorage ||
                 profile.WantsEasyControl ||
                 profile.NeedsLowSeat);

            bool hasNoStrongNewConstraint =
                string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                !HasAnyNonBudgetConstraint(parsedIntent);

            return hasRestartPhrase && hasBudgetSignal && hadOldRecommendationGoal && hasNoStrongNewConstraint;
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

            bool hasBudgetSignal = HasAnyBudgetSignal(parsedIntent) ||
                Regex.IsMatch(text, @"\b\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
                ContainsAny(text, "xe khoảng", "xe khoang", "xe tầm", "xe tam") ||
                Regex.IsMatch(text, @"\bxe\s+\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            bool asksComparisonStyleQuestion =
                ContainsAny(text, "thì sao", "thi sao", "ổn không", "on khong", "được không", "duoc khong", "thế nào", "the nao");

            bool hasNoNewHardConstraint =
                string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                string.IsNullOrWhiteSpace(parsedIntent.Target) &&
                !HasAnyNonBudgetConstraint(parsedIntent);

            return (startsLikeBudgetPivot || containsBudgetPivotPhrase) &&
                   asksComparisonStyleQuestion &&
                   hasNoNewHardConstraint;
        }

        private static void ApplyMissingContextFromProfile(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    bool includeBudget,
    bool includeBrandCategory,
    bool includeTarget,
    bool includeSoftPreferences = true)
        { 
            if (intent == null || profile == null)
                return;

            if (includeBrandCategory)
            {
                if (string.IsNullOrWhiteSpace(intent.Brand))
                    intent.Brand = profile.PreferredBrand;

                if (string.IsNullOrWhiteSpace(intent.Category))
                    intent.Category = profile.PreferredCategory;
            }

            if (includeTarget && string.IsNullOrWhiteSpace(intent.Target))
            {
                intent.Target = profile.Target;
                intent.PrefersMaleStyle = profile.PrefersMaleStyle;
                intent.PrefersFemaleStyle = profile.PrefersFemaleStyle;
            }

            if (includeSoftPreferences)
            {
                if (!intent.ForWork)
                    intent.ForWork = profile.ForWork;

                if (!intent.ForSchool)
                    intent.ForSchool = profile.ForSchool;

                if (!intent.ForCity)
                    intent.ForCity = profile.ForCity;

                if (!intent.ForTour)
                    intent.ForTour = profile.ForTour;

                if (!intent.WantsFuelSaving)
                    intent.WantsFuelSaving = profile.WantsFuelSaving;

                if (!intent.WantsLargeStorage)
                    intent.WantsLargeStorage = profile.WantsLargeStorage;

                if (!intent.WantsEasyControl)
                    intent.WantsEasyControl = profile.WantsEasyControl;

                if (!intent.NeedsLowSeat)
                    intent.NeedsLowSeat = profile.NeedsLowSeat;

                if (!intent.HeightCm.HasValue && profile.HeightCm.HasValue)
                    intent.HeightCm = profile.HeightCm;
            }

            if (includeBudget)
            {
                if (!intent.TargetPrice.HasValue && profile.TargetPrice.HasValue)
                    intent.TargetPrice = profile.TargetPrice;

                if (!intent.PriceMin.HasValue && profile.PriceMin.HasValue)
                    intent.PriceMin = profile.PriceMin;

                if (!intent.PriceMax.HasValue && profile.PriceMax.HasValue)
                    intent.PriceMax = profile.PriceMax;

                if (intent.FilterType == PriceFilterType.None &&
                    profile.FilterType != PriceFilterType.None)
                {
                    intent.FilterType = profile.FilterType;
                }
            }
        }

        private static void RemoveResolvedExclusions(ParsedIntent intent)
        {
            if (intent == null)
                return;

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                intent.ExcludedCategories.RemoveWhere(x =>
                    string.Equals(x, intent.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                intent.ExcludedBrands.RemoveWhere(x =>
                    string.Equals(x, intent.Brand, StringComparison.OrdinalIgnoreCase));
            }
        }


        private static void ApplyCurrentTurnExclusionOverride(
            ParsedIntent parsedIntent,
            ParsedIntent effectiveIntent)
        {
            if (parsedIntent == null || effectiveIntent == null)
                return;
            EnsureIntentCollections(parsedIntent);
            EnsureIntentCollections(effectiveIntent);
            if (parsedIntent.ExcludedBrands.Any())
            {
                effectiveIntent.ExcludedBrands.UnionWith(parsedIntent.ExcludedBrands);

                if (!string.IsNullOrWhiteSpace(effectiveIntent.Brand) &&
                    parsedIntent.ExcludedBrands.Contains(effectiveIntent.Brand, StringComparer.OrdinalIgnoreCase))
                {
                    effectiveIntent.Brand = null;
                }
            }

            if (parsedIntent.ExcludedCategories.Any())
            {
                effectiveIntent.ExcludedCategories.UnionWith(parsedIntent.ExcludedCategories);

                if (!string.IsNullOrWhiteSpace(effectiveIntent.Category) &&
                    parsedIntent.ExcludedCategories.Contains(effectiveIntent.Category, StringComparer.OrdinalIgnoreCase))
                {
                    effectiveIntent.Category = null;
                }
            }
        }

        private static void MergePreferenceCollectionsFromProfile(
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            if (intent == null || profile == null)
                return;

            EnsureIntentCollections(intent);
            EnsureProfileCollections(profile);

            intent.ExcludedBrands.UnionWith(profile.ExcludedBrands);
            intent.ExcludedCategories.UnionWith(profile.ExcludedCategories);
            intent.RequestedStyles.UnionWith(profile.RequestedStyles);
        }

        private static bool HasAnyBudgetSignal(ParsedIntent parsedIntent)
        {
            return parsedIntent.TargetPrice.HasValue ||
                   parsedIntent.PriceMin.HasValue ||
                   parsedIntent.PriceMax.HasValue ||
                   parsedIntent.FilterType != PriceFilterType.None;
        }

        private static bool IsOnlyPriceChangeFromIntent(ParsedIntent parsedIntent)
        {
            return HasAnyBudgetSignal(parsedIntent) && !HasAnyNonBudgetConstraint(parsedIntent);
        }

        private static bool HasAnyNonBudgetConstraint(ParsedIntent parsedIntent)
        {
            return !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                   !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                   !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                   parsedIntent.ForWork ||
                   parsedIntent.ForSchool ||
                   parsedIntent.ForCity ||
                   parsedIntent.ForTour ||
                   parsedIntent.WantsFuelSaving ||
                   parsedIntent.WantsLargeStorage ||
                   parsedIntent.WantsEasyControl ||
                   parsedIntent.NeedsLowSeat ||
                   parsedIntent.ExcludedBrands.Any() ||
                   parsedIntent.ExcludedCategories.Any() ||
                   parsedIntent.RequestedStyles.Any() ||
                   parsedIntent.PrefersMaleStyle ||
                   parsedIntent.PrefersFemaleStyle ||
                   (parsedIntent.MentionedProducts?.Count > 0);
        }

        private static bool HasRefinementSignal(ParsedIntent parsedIntent)
        {
            return !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                   !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                   parsedIntent.ForWork ||
                   parsedIntent.ForSchool ||
                   parsedIntent.ForCity ||
                   parsedIntent.ForTour ||
                   parsedIntent.WantsFuelSaving ||
                   parsedIntent.WantsLargeStorage ||
                   parsedIntent.WantsEasyControl ||
                   parsedIntent.NeedsLowSeat ||
                   parsedIntent.ExcludedBrands.Any() ||
                   parsedIntent.ExcludedCategories.Any() ||
                   parsedIntent.RequestedStyles.Any() ||
                   HasAnyBudgetSignal(parsedIntent);
        }

        private static void ApplyExplicitTarget(ParsedIntent intent, string explicitTarget)
        {
            intent.Target = explicitTarget;

            if (string.Equals(explicitTarget, "nam", StringComparison.OrdinalIgnoreCase))
            {
                intent.PrefersMaleStyle = true;
                intent.PrefersFemaleStyle = false;
            }
            else if (string.Equals(explicitTarget, "nữ", StringComparison.OrdinalIgnoreCase))
            {
                intent.PrefersFemaleStyle = true;
                intent.PrefersMaleStyle = false;
            }
        }

        private static bool LooksLikeHardContextReset(string normalizedMessage, ParsedIntent parsedIntent)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            return ContainsAny(text, "đổi chủ đề", "doi chu de", "bỏ cái kia", "bo cai kia", "quay lại từ đầu", "reset")
                   || (HasAnyBudgetSignal(parsedIntent) && ContainsAny(text, "giờ", "gio", "đổi sang", "doi sang", "thôi", "thoi"));
        }

        private static string? InferComparisonFeature(string normalizedMessage)
        {
            var text = (normalizedMessage ?? string.Empty).ToLowerInvariant();

            if (ContainsAny(text, "đi làm", "di lam")) return "work_fit";
            if (ContainsAny(text, "đi học", "di hoc")) return "school_fit";
            if (ContainsAny(text, "cốp", "cop")) return "storage";
            if (ContainsAny(text, "tiết kiệm xăng", "tiet kiem xang", "hao xăng", "hao xang")) return "fuel_saving";
            if (ContainsAny(text, "dễ lái", "de lai", "dễ đi", "de di", "dễ điều khiển", "de dieu khien")) return "easy_control";
            if (ContainsAny(text, "thấp", "thap", "chống chân", "chong chan")) return "low_seat";

            return null;
        }
        private static bool LooksLikeFollowUpReference(string normalizedMessage)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return false;

            var text = normalizedMessage.Trim().ToLowerInvariant();

            return ContainsAny(text,
                "mẫu đó", "xe đó", "con đó", "cái đó",
                "mẫu kia", "xe kia", "con kia", "cái kia",
                "loại đó", "loại kia", "bản đó", "bản kia",
                "cái đầu", "con đầu", "mẫu đầu",
                "hôm nãy", "lúc nãy", "vừa nãy",
                "còn con đó", "còn mẫu đó", "còn xe đó",
                "thế còn", "vậy còn", "con này", "mẫu này", "xe này");
        }
        private static void EnsureIntentCollections(ParsedIntent intent)
        {
            if (intent == null)
                return;

            intent.MentionedProducts ??= new List<string>();
            intent.ExcludedBrands ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            intent.ExcludedCategories ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            intent.RequestedStyles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureProfileCollections(CustomerPreferenceProfile profile)
        {
            if (profile == null)
                return;

            profile.LastComparedProducts ??= new List<string>();
            profile.LastRecommendedProducts ??= new List<string>();
            profile.ExcludedBrands ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            profile.ExcludedCategories ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            profile.RequestedStyles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        private static bool ContainsAny(string text, params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
