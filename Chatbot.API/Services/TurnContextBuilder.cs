using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services.Conversation
{
    public class TurnContextBuilder : ITurnContextBuilder
    {
        private readonly IConversationPolicyService _conversationPolicyService;

        public TurnContextBuilder(IConversationPolicyService conversationPolicyService)
        {
            _conversationPolicyService = conversationPolicyService;
        }

        public Task<TurnContextBuildResult> BuildAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile existingProfile,
    ConversationState state)
        {
            var (effectiveIntent, contextDecision) = _conversationPolicyService.ResolveEffectiveIntent(
                conversationId,
                normalizedMessage,
                parsedIntent,
                existingProfile);

            var goalContinuity = ResolveGoalContinuity(
                effectiveIntent,
                existingProfile,
                state,
                normalizedMessage);

            var resolvedReference = ResolveReference(existingProfile, state);

            // Chỉ chỉnh những case thật sự an toàn:
            // - order lookup: xóa context xe
            // - product lookup rõ ràng: xóa context tư vấn
            // - follow-up refine/continue: chỉ đánh dấu, không tự kéo hết constraint cũ
            effectiveIntent = ApplySafeGoalContinuityAdjustments(
                effectiveIntent,
                goalContinuity,
                normalizedMessage,
                existingProfile,
                state,
                resolvedReference);

            var result = new TurnContextBuildResult
            {
                EffectiveIntent = effectiveIntent,
                EffectiveProfile = existingProfile,
                IsFollowUp = IsFollowUpCandidate(normalizedMessage, effectiveIntent, existingProfile, state),
                IsShortFollowUp = IsShortFollowUp(normalizedMessage),
                GoalContinuity = goalContinuity,
                Reason = contextDecision.ToString(),
                ResolvedReference = resolvedReference
            };

            result.IsGoalSwitch = string.Equals(
                result.GoalContinuity,
                "new_goal",
                StringComparison.OrdinalIgnoreCase);

            result.CarryForwardFields = ResolveCarryForwardFields(
                result.GoalContinuity,
                existingProfile,
                state);

            return Task.FromResult(result);
        }

        private static bool IsShortFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasReferenceSignal =
                text.Contains("còn mẫu đó") ||
text.Contains("con mau do") ||
                text.Contains("thì sao") ||
                text.Contains("thế còn") ||
                text.Contains("vậy còn") ||
                text.Contains("mẫu đó") ||
                text.Contains("xe đó") ||
                text.Contains("con đó") ||
                text.Contains("mẫu kia") ||
                text.Contains("xe kia") ||
                text.Contains("con kia");

            return text.Length <= 25 && hasReferenceSignal;
        }

        private static bool IsFollowUpCandidate(
     string normalizedMessage,
     ParsedIntent effectiveIntent,
     CustomerPreferenceProfile profile,
     ConversationState state)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return false;

            if (effectiveIntent == null)
                return false;

            var text = normalizedMessage.Trim().ToLowerInvariant();

            if (effectiveIntent.IsOutOfScope ||
                effectiveIntent.IsNoise ||
                effectiveIntent.IsAck ||
                effectiveIntent.IsGreeting)
            {
                return false;
            }

            bool hasConversationContext =
                profile.HasActiveRecommendationContext ||
                profile.HasActiveCompareContext ||
                !string.IsNullOrWhiteSpace(profile.LastLookupProductName) ||
                (!string.IsNullOrWhiteSpace(state.CurrentGoalType) &&
                 !string.Equals(state.CurrentGoalType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase)) ||
                (state.MentionedProductNames != null && state.MentionedProductNames.Count > 0);

            if (!hasConversationContext)
                return false;

            bool hasReferenceSignal = HasReferenceSignal(text);
            bool hasConstraintSignal = HasFollowUpDomainConstraint(text);

            return hasReferenceSignal || hasConstraintSignal || effectiveIntent.IsFollowUp;
        }
        private static bool LooksLikeAckOnly(string text)
        {
            return text is "ok" or "oke" or "oki" or "vang" or "u" or "uhm" or "da" or "roi";
        }

        private static bool LooksLikeNoise(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[.,?!_/@#$%^&*()+=-]+$"))
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^[a-z]{4,}$") &&
                !text.Contains("vision") &&
                !text.Contains("winner") &&
                !text.Contains("blade") &&
                !text.Contains("honda") &&
                !text.Contains("yamaha"))
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeOutOfScopeLite(string text)
        {
            return text.Contains("thoi tiet")
                || text.Contains("bitcoin")
                || text.Contains("bong da")
                || text.Contains("code java")
                || text.Contains("code python")
                || text.Contains("chung khoan");
        }
        private static string ResolveGoalContinuity(
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile,
    ConversationState state,
    string normalizedMessage)
        {
            if (effectiveIntent == null)
                return "new_goal";

            if (effectiveIntent.IsOutOfScope ||
                effectiveIntent.IsNoise ||
                effectiveIntent.IsAck ||
                effectiveIntent.IsGreeting)
            {
                return "new_goal";
            }

            if (IsStandaloneNewRequest(normalizedMessage, effectiveIntent, profile))
                return "new_goal";

            if (LooksLikeHardResetTurn(normalizedMessage, effectiveIntent))
                return "new_goal";

            if (effectiveIntent.IsOrderLookup)
            {
                return string.Equals(state.CurrentDomain, "don_hang", StringComparison.OrdinalIgnoreCase)
                    ? "continue"
                    : "new_goal";
            }

            if (effectiveIntent.IsDirectCompare)
            {
                return profile.HasActiveCompareContext ? "continue" : "pivot";
            }

            if (effectiveIntent.IsDirectProductLookup)
            {
                return !string.IsNullOrWhiteSpace(profile.LastLookupProductName) ? "continue" : "pivot";
            }

            if (effectiveIntent.IsProductSearch)
            {
                if (LooksLikeRefinement(normalizedMessage))
                    return "refine";

                return IsFollowUpCandidate(normalizedMessage, effectiveIntent, profile, state)
    ? "continue"
    : "new_goal";
            }

            if (effectiveIntent.IsOpenRecommendation && LooksLikeFreshRecommendation(normalizedMessage))
                return "new_goal";

            if (LooksLikeRefinement(normalizedMessage))
            {
                if (profile.HasActiveRecommendationContext ||
                    string.Equals(state.CurrentGoalType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(state.CurrentGoalType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase))
                {
                    return "refine";
                }
            }

            if (profile.HasActiveRecommendationContext)
            {
                return IsFollowUpCandidate(normalizedMessage, effectiveIntent, profile, state)
    ? "continue"
    : "new_goal";
            }

            if (!string.IsNullOrWhiteSpace(state.CurrentGoalType) &&
                !string.Equals(state.CurrentGoalType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                return IsFollowUpCandidate(normalizedMessage, effectiveIntent, profile, state)
     ? "continue"
     : "new_goal";
            }

            return "new_goal";
        }
        private static List<string> ResolveCarryForwardFields(
     string? goalContinuity,
     CustomerPreferenceProfile profile,
     ConversationState state)
        {
            var fields = new List<string>();
            var constraints = state.Constraints ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (string.Equals(goalContinuity, "continue", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(goalContinuity, "refine", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(profile.PreferredBrand) || constraints.ContainsKey("brand"))
                    fields.Add("brand");

                if (!string.IsNullOrWhiteSpace(profile.PreferredCategory) || constraints.ContainsKey("category"))
                    fields.Add("category");

                if (profile.PriceMin.HasValue || constraints.ContainsKey("priceMin"))
                    fields.Add("priceMin");

                if (profile.PriceMax.HasValue || constraints.ContainsKey("priceMax"))
                    fields.Add("priceMax");

                if (!string.IsNullOrWhiteSpace(profile.Target) || constraints.ContainsKey("target"))
                    fields.Add("target");

                if (profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count > 0)
                    fields.Add("recommendedProducts");

                if (state.MentionedProductNames != null && state.MentionedProductNames.Count > 0)
                    fields.Add("mentionedProducts");

                if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                    fields.Add("lookupProduct");
            }
            else if (string.Equals(goalContinuity, "pivot", StringComparison.OrdinalIgnoreCase))
            {
                if (profile.PriceMin.HasValue || constraints.ContainsKey("priceMin"))
                    fields.Add("priceMin");

                if (profile.PriceMax.HasValue || constraints.ContainsKey("priceMax"))
                    fields.Add("priceMax");

                if (!string.IsNullOrWhiteSpace(profile.Target) || constraints.ContainsKey("target"))
                    fields.Add("target");

                if (state.MentionedProductNames != null && state.MentionedProductNames.Count > 0)
                    fields.Add("mentionedProducts");
            }

            return fields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static string? ResolveReference(
     CustomerPreferenceProfile profile,
     ConversationState state)
        {
            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                return profile.LastLookupProductName;

            if (!string.IsNullOrWhiteSpace(state.LastResolvedReference))
                return state.LastResolvedReference;

            if (profile.LastMentionedProducts != null && profile.LastMentionedProducts.Count > 0)
                return profile.LastMentionedProducts.First();

            if (state.MentionedProductNames != null && state.MentionedProductNames.Count > 0)
                return state.MentionedProductNames.First();

            return null;
        }
        private static bool LooksLikeRefinement(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().ToLowerInvariant();

            var strongFollowUpMarkers =
                text.Contains("con ") ||
                text.Contains("thi sao") ||
                text.Contains("the con") ||
                text.Contains("xe ga thoi") ||
                text.Contains("xe so thoi") ||
                text.Contains("duoi ") ||
                text.Contains("tren ") ||
                text.Contains("khoang ") ||
                text.Contains("cop rong") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("di lam") ||
                text.Contains("di hoc");

            if (strongFollowUpMarkers)
                return true;

            var shortRefinement =
                text.Length <= 25 &&
                (
                    text.Contains("honda") ||
                    text.Contains("yamaha") ||
                    text.Contains("sym") ||
                    text.Contains("suzuki") ||
                    text.Contains("nam") ||
                    text.Contains("nu")
                );

            return shortRefinement;
        }

        private static bool LooksLikeFreshRecommendation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().ToLowerInvariant();

            return text.Contains("tư vấn xe")
                   || text.Contains("gợi ý xe")
                   || text.Contains("nên mua xe nào")
                   || text.Contains("xe nào phù hợp");
        }
        private static bool LooksLikeHardResetTurn(string normalizedMessage, ParsedIntent intent)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasResetPhrase =
                text.Contains("đổi sang") ||
                text.Contains("doi sang") ||
                text.Contains("đổi qua") ||
                text.Contains("doi qua") ||
                text.Contains("giờ tư vấn") ||
                text.Contains("gio tu van") ||
                text.Contains("tư vấn xe mới") ||
                text.Contains("tu van xe moi") ||
                text.Contains("quay lại từ đầu") ||
                text.Contains("reset") ||
                text.Contains("bỏ cái trước") ||
                text.Contains("bo cai truoc");

            bool hasNewStrongIntent =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.ForWork ||
                intent.ForSchool ||
                intent.ForCity ||
                intent.ForTour ||
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue;

            return hasResetPhrase && hasNewStrongIntent;
        }
        private static bool IsStandaloneNewRequest(string normalizedMessage, ParsedIntent intent, CustomerPreferenceProfile? profile)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasReferenceMarker =
               text.Contains("còn mẫu đó") ||
text.Contains("con mau do") ||
                text.Contains("thì sao") ||
                text.Contains("mẫu đó") ||
                text.Contains("con đó") ||
                text.Contains("xe đó") ||
                text.Contains("mẫu kia") ||
                text.Contains("con kia") ||
                text.Contains("xe kia") ||
                text.Contains("đầu tiên") ||
                text.Contains("thứ 2") ||
                text.Contains("thứ hai");

            if (hasReferenceMarker)
                return false;

            int strongSignals = 0;

            if (!string.IsNullOrWhiteSpace(intent.Brand)) strongSignals++;
            if (!string.IsNullOrWhiteSpace(intent.Category)) strongSignals++;
            if (!string.IsNullOrWhiteSpace(intent.Target)) strongSignals++;
            if (intent.ForWork || intent.ForSchool || intent.ForCity || intent.ForTour) strongSignals++;
            if (intent.PriceMin.HasValue || intent.PriceMax.HasValue || intent.TargetPrice.HasValue) strongSignals++;

            bool hasFreshConsultationPhrase =
                text.Contains("tư vấn") ||
                text.Contains("tu van") ||
                text.Contains("gợi ý") ||
                text.Contains("goi y") ||
                text.Contains("nên mua") ||
                text.Contains("nen mua");

            if (!hasFreshConsultationPhrase || strongSignals < 1)
                return false;

            // Nếu không có context cũ → đây chắc chắn là yêu cầu mới
            bool hasActiveContext =
                profile?.HasActiveRecommendationContext == true &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0;

            if (!hasActiveContext)
                return true;

            bool hasTarget = !string.IsNullOrWhiteSpace(intent.Target);
            bool hasMultipleSignals = strongSignals >= 2;

            bool hasHardRestartPhrase =
                text.Contains("tu van lai") ||
                text.Contains("bat dau lai") ||
                text.Contains("doi y") ||
                text.Contains("doi sang") ||
                text.Contains("tu dau") ||
                text.Contains("reset") ||
                text.Contains("bo tieu chi cu") ||
                text.Contains("thoi khong can");

            return hasHardRestartPhrase || hasTarget || hasMultipleSignals;
        }
        private static ParsedIntent ApplySafeGoalContinuityAdjustments(
     ParsedIntent effectiveIntent,
     string? goalContinuity,
     string normalizedMessage,
     CustomerPreferenceProfile profile,
     ConversationState state,
     string? resolvedReference)
        {
            if (effectiveIntent == null)
                return new ParsedIntent();

            var adjusted = CloneIntent(effectiveIntent);

            // 1. Đơn hàng là domain riêng, tuyệt đối không kéo context xe sang
            if (adjusted.IsOrderLookup)
                return ResetForOrderLookup(adjusted);

            // 2. Lookup sản phẩm rõ ràng thì không kéo nhu cầu tư vấn cũ
            if (adjusted.IsDirectProductLookup)
            {
                adjusted = ResetForProductLookup(adjusted);
                return adjusted;
            }

            // 3. Chỉ dùng reference cũ cho câu thật sự nói lửng kiểu "xe đó", "con kia"
            if (IsVeryShortReferenceLikeMessage(normalizedMessage) &&
                !string.IsNullOrWhiteSpace(resolvedReference) &&
                !adjusted.IsDirectCompare &&
                !adjusted.IsOrderLookup)
            {
                adjusted.MentionedProducts ??= new List<string>();

                if (!adjusted.MentionedProducts.Any(x =>
                        string.Equals(x, resolvedReference, StringComparison.OrdinalIgnoreCase)))
                {
                    adjusted.MentionedProducts.Insert(0, resolvedReference);
                }

                if (!adjusted.IsDirectProductLookup &&
                    string.IsNullOrWhiteSpace(adjusted.IntentType))
                {
                    adjusted.IntentType = "product_lookup";
                }

                return adjusted;
            }

            // 4. Refine/continue chỉ đánh dấu follow-up.
            // Không tự carry brand/category/price ở đây nữa.
            // Việc merge constraint đã để ConversationPolicyService + ConversationPreferenceService xử lý.
            if (string.Equals(goalContinuity, "refine", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(goalContinuity, "continue", StringComparison.OrdinalIgnoreCase))
            {
                adjusted.IsFollowUp = true;

                if (!adjusted.IsDirectProductLookup &&
                    !adjusted.IsDirectCompare &&
                    !adjusted.IsOrderLookup &&
                    !adjusted.IsProductSearch)
                {
                    adjusted.IntentType = string.IsNullOrWhiteSpace(adjusted.IntentType) ||
                                          string.Equals(adjusted.IntentType, "unknown", StringComparison.OrdinalIgnoreCase)
                        ? "refine"
                        : adjusted.IntentType;
                }

                return adjusted;
            }

            if (string.Equals(goalContinuity, "pivot", StringComparison.OrdinalIgnoreCase))
            {
                adjusted.IsFollowUp = false;
                return adjusted;
            }

            if (string.Equals(goalContinuity, "new_goal", StringComparison.OrdinalIgnoreCase))
            {
                adjusted.IsFollowUp = false;
                return adjusted;
            }

            return adjusted;
        }
        private static ParsedIntent CloneIntent(ParsedIntent source)
        {
            return new ParsedIntent
            {
                IntentType = source.IntentType,
                Brand = source.Brand,
                Category = source.Category,
                Target = source.Target,
                PriceMin = source.PriceMin,
                PriceMax = source.PriceMax,
                TargetPrice = source.TargetPrice,
                FilterType = source.FilterType,

                ForWork = source.ForWork,
                ForSchool = source.ForSchool,
                ForCity = source.ForCity,
                ForTour = source.ForTour,
                WantsFuelSaving = source.WantsFuelSaving,
                WantsLargeStorage = source.WantsLargeStorage,
                WantsEasyControl = source.WantsEasyControl,
                NeedsLowSeat = source.NeedsLowSeat,
                PrefersMaleStyle = source.PrefersMaleStyle,
                PrefersFemaleStyle = source.PrefersFemaleStyle,
                HeightCm = source.HeightCm,

                IsFollowUp = source.IsFollowUp,
                FollowUpType = source.FollowUpType,
                LookupField = source.LookupField,
                ComparisonFeature = source.ComparisonFeature,

                IsDirectProductLookup = source.IsDirectProductLookup,
                IsProductSearch = source.IsProductSearch,
                IsOpenRecommendation = source.IsOpenRecommendation,
                IsDirectCompare = source.IsDirectCompare,
                IsOrderLookup = source.IsOrderLookup,
                IsOutOfScope = source.IsOutOfScope,

                HasExpandRecommendationSignal = source.HasExpandRecommendationSignal,
                HasNarrowRefinementSignal = source.HasNarrowRefinementSignal,
                HasFreshConsultationSignal = source.HasFreshConsultationSignal,

                MentionedProducts = source.MentionedProducts != null
                    ? new List<string>(source.MentionedProducts)
                    : new List<string>(),

                ExcludedBrands = source.ExcludedBrands != null
                    ? new HashSet<string>(source.ExcludedBrands, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),

                ExcludedCategories = source.ExcludedCategories != null
                    ? new HashSet<string>(source.ExcludedCategories, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),

                RequestedStyles = source.RequestedStyles != null
                    ? new HashSet<string>(source.RequestedStyles, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };
        }
        private static ParsedIntent ResetForProductLookup(ParsedIntent adjusted)
        {
            adjusted.Target = null;
            adjusted.ForWork = false;
            adjusted.ForSchool = false;
            adjusted.ForCity = false;
            adjusted.ForTour = false;
            adjusted.WantsFuelSaving = false;
            adjusted.WantsLargeStorage = false;
            adjusted.WantsEasyControl = false;
            adjusted.NeedsLowSeat = false;
            adjusted.PrefersMaleStyle = false;
            adjusted.PrefersFemaleStyle = false;

            adjusted.PriceMin = null;
            adjusted.PriceMax = null;
            adjusted.TargetPrice = null;
            adjusted.FilterType = PriceFilterType.None;

            return adjusted;
        }

        private static ParsedIntent ResetForOrderLookup(ParsedIntent adjusted)
        {
            adjusted.Brand = null;
            adjusted.Category = null;
            adjusted.Target = null;

            adjusted.ForWork = false;
            adjusted.ForSchool = false;
            adjusted.ForCity = false;
            adjusted.ForTour = false;
            adjusted.WantsFuelSaving = false;
            adjusted.WantsLargeStorage = false;
            adjusted.WantsEasyControl = false;
            adjusted.NeedsLowSeat = false;
            adjusted.PrefersMaleStyle = false;
            adjusted.PrefersFemaleStyle = false;

            adjusted.PriceMin = null;
            adjusted.PriceMax = null;
            adjusted.TargetPrice = null;
            adjusted.FilterType = PriceFilterType.None;

            adjusted.MentionedProducts = new List<string>();
            adjusted.ExcludedBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            adjusted.ExcludedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            adjusted.RequestedStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return adjusted;
        }
        private static bool HasReferenceSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return  text.Contains("thi sao") ||
                   text.Contains("vay con") ||
                   text.Contains("the con") ||
                   text.Contains("mau do") ||
                   text.Contains("xe do") ||
                   text.Contains("con do") ||
                   text.Contains("mau kia") ||
                   text.Contains("xe kia") ||
                   text.Contains("loai do") ||
                   text.Contains("loai kia") ||
                   text.Contains("neu ") ||
                   text.Contains("duoi ") ||
                   text.Contains("tren ") ||
                   text.Contains("xe ga thoi") ||
                   text.Contains("xe so thoi");
        }
        private static bool HasFollowUpDomainConstraint(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return false;

    return text.Contains("honda") ||
           text.Contains("yamaha") ||
           text.Contains("suzuki") ||
           text.Contains("sym") ||
           text.Contains("piaggio") ||
           text.Contains("xe ga") ||
           text.Contains("xe so") ||
           text.Contains("con tay") ||
           text.Contains("cop rong") ||
           text.Contains("tiet kiem xang") ||
           text.Contains("de chong chan") ||
           text.Contains("di lam") ||
           text.Contains("di hoc") ||
           text.Contains("duoi ") ||
           text.Contains("tren ") ||
           text.Contains("tam ") ||
           text.Contains("khoang ");
}
        private static bool IsVeryShortReferenceLikeMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().ToLowerInvariant();

            return text.Length <= 20 &&
                   (
                       text.Contains("mau do") ||
                       text.Contains("con do") ||
                       text.Contains("xe do") ||
                       text.Contains("mau kia") ||
                       text.Contains("con kia") ||
                       text.Contains("xe kia") ||
                       text.Contains("dau tien") ||
                       text.Contains("thu 2") ||
                       text.Contains("thu hai")
                   );
        }
   
    }


}