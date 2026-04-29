using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services.Conversation
{
    public class FlowDecisionService : IFlowDecisionService
    {
        public FlowRoutingResult ResolveFinalRouting(
            string normalizedMessage,
            ParsedIntent effectiveIntent,
            CustomerPreferenceProfile conversationProfile,
            RecommendationContextDecision contextDecision,
            FlowRoutingResult baseRouting)
        {
            var routing = baseRouting ?? new FlowRoutingResult();
            var text = (normalizedMessage ?? string.Empty).Trim();

            bool hasRecommendationContext = HasRecommendationContext(conversationProfile);
            bool hasCompareContext = HasCompareContext(conversationProfile);
            bool hasLookupContext = HasLookupContext(conversationProfile);

            // 1) Trust explicit intent first.
            if (ShouldForceOrderLookup(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.OrderLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit order lookup intent";
                return routing;
            }

            if (ShouldForceDirectCompare(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.Compare;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit compare intent";
                return routing;
            }

            if (ShouldForceDirectProductLookup(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.ProductLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit product lookup intent";
                return routing;
            }
            if (string.Equals(conversationProfile?.ActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) &&
    IsLookupReferenceFollowUp(text))
            {
                routing.FlowType = ChatFlowType.ProductLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by product lookup reference follow-up";
                return routing;
            }

            if (string.Equals(conversationProfile?.ActiveFlow, ChatFlowType.OrderLookup, StringComparison.OrdinalIgnoreCase) &&
                IsOrderReferenceFollowUp(text))
            {
                routing.FlowType = ChatFlowType.OrderLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by order lookup reference follow-up";
                return routing;
            }

            // 2) Then trust resolved conversation decision.
            switch (contextDecision)
            {
                case RecommendationContextDecision.StartFreshRecommendation:
                    routing.FlowType = ChatFlowType.Recommendation;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = true;
                    routing.Reason = "Mapped from StartFreshRecommendation";
                    return routing;

                case RecommendationContextDecision.ExpandFromCurrentGoal:
                    if (ShouldTreatExpandAsRefinement(effectiveIntent, text))
                    {
                        routing.FlowType = ChatFlowType.Refinement;
                        routing.ShouldUseDeterministicFlow = true;
                        routing.ShouldUseAiFallback = false;
                        routing.ShouldUseRag = false;
                        routing.Reason = "Expand decision converted to refinement because user added constraints";
                        return routing;
                    }

                    routing.FlowType = ChatFlowType.Recommendation;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = true;
                    routing.Reason = "Mapped from ExpandFromCurrentGoal";
                    return routing;

                case RecommendationContextDecision.NarrowWithinCurrentSet:
                    routing.FlowType = ChatFlowType.Refinement;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Mapped from NarrowWithinCurrentSet";
                    return routing;
            }

            if (ShouldForceRecommendationFromIntent(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Forced by explicit recommendation intent";
                return routing;
            }

            if (ShouldForceSearchFromIntent(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.ProductSearch;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit product search intent";
                return routing;
            }


            // 3) Use follow-up context only as a lightweight fallback
            // when policy service did not already produce a strong decision.
            bool hasStrongPolicyDecision =
                contextDecision == RecommendationContextDecision.StartFreshRecommendation ||
                contextDecision == RecommendationContextDecision.ExpandFromCurrentGoal ||
                contextDecision == RecommendationContextDecision.NarrowWithinCurrentSet;

            if (!hasStrongPolicyDecision && !LooksLikeNewStandaloneRequest(text, effectiveIntent))
            {
                if (ShouldUseLookupFollowUp(hasLookupContext, text, effectiveIntent))
                {
                    routing.FlowType = ChatFlowType.ProductLookup;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Forced by lookup follow-up context fallback";
                    return routing;
                }

                if (ShouldUseCompareFollowUp(hasCompareContext, text, effectiveIntent, conversationProfile))
                {
                    routing.FlowType = ChatFlowType.Compare;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Forced by compare follow-up context fallback";
                    return routing;
                }

                if (ShouldUseRecommendationRefinement(hasRecommendationContext, effectiveIntent, text))
                {
                    routing.FlowType = ChatFlowType.Refinement;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Forced by recommendation follow-up refinement fallback";
                    return routing;
                }
            }
            // 4) Hard filter only search should remain deterministic, but only when recommendation context is not active.
            if (FlowIntentHeuristics.IsHardFilterOnlySearch(effectiveIntent, text) && !hasRecommendationContext)
            {
                routing.FlowType = ChatFlowType.ProductSearch;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by hard-filter-only search rule";
                return routing;
            }

            return routing;
        }

        private static bool HasRecommendationContext(CustomerPreferenceProfile? profile)
        {
            return profile != null &&
                   profile.HasActiveRecommendationContext &&
                   (
                       (profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count > 0) ||
                       (profile.BaseRecommendedProducts != null && profile.BaseRecommendedProducts.Count > 0)
                   );
        }

        private static bool HasCompareContext(CustomerPreferenceProfile? profile)
        {
            return profile != null &&
                   profile.HasActiveCompareContext &&
                   profile.LastComparedProducts != null &&
                   profile.LastComparedProducts.Count >= 2;
        }

        private static bool HasLookupContext(CustomerPreferenceProfile? profile)
        {
            return profile != null && profile.LastLookupProductId.HasValue;
        }

        private static bool ShouldForceOrderLookup(ParsedIntent? intent)
        {
            if (intent == null) return false;

            return string.Equals(intent.IntentType, "order_lookup", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.OrderLookup, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceDirectCompare(ParsedIntent? intent)
        {
            if (intent == null) return false;

            bool namesTwoProducts = intent.MentionedProducts != null &&
                                    intent.MentionedProducts
                                        .Where(x => !string.IsNullOrWhiteSpace(x))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .Count() >= 2;

            return (intent.IsDirectCompare && namesTwoProducts) ||
                   string.Equals(intent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceDirectProductLookup(ParsedIntent? intent)
        {
            if (intent == null) return false;

            return intent.IsDirectProductLookup ||
                   string.Equals(intent.IntentType, "product_lookup", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceRecommendationFromIntent(ParsedIntent? intent)
        {
            if (intent == null) return false;

            return string.Equals(intent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceSearchFromIntent(ParsedIntent? intent)
        {
            if (intent == null) return false;

            return string.Equals(intent.IntentType, "product_search", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldUseLookupFollowUp(
            bool hasLookupContext,
            string message,
            ParsedIntent effectiveIntent)
        {
            if (!hasLookupContext)
                return false;

            if (ShouldForceDirectCompare(effectiveIntent) ||
                ShouldForceRecommendationFromIntent(effectiveIntent) ||
                ShouldForceSearchFromIntent(effectiveIntent) ||
                HasAnyNamedProducts(effectiveIntent, minimumCount: 2))
            {
                return false;
            }

            if (effectiveIntent.IsDirectProductLookup)
                return true;

            return LookupConversationRules.LooksLikeLookupFollowUp(message);
        }

        private static bool ShouldUseCompareFollowUp(
            bool hasCompareContext,
            string message,
            ParsedIntent effectiveIntent,
            CustomerPreferenceProfile profile)
        {
            if (!hasCompareContext)
                return false;

            if (ShouldForceRecommendationFromIntent(effectiveIntent) ||
                ShouldForceSearchFromIntent(effectiveIntent) ||
                ShouldForceDirectProductLookup(effectiveIntent))
            {
                return false;
            }

            if (string.Equals(effectiveIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!effectiveIntent.IsFollowUp && HasAnyNamedProducts(effectiveIntent, minimumCount: 2))
                return true;

            return CompareConversationRules.IsCompareFeatureFollowUpQuestion(
                       effectiveIntent,
                       profile,
                       message)
                   || IsShortComparePriceFollowUp(message, effectiveIntent)
                   || LooksLikeExplicitCompareQuestion(message, effectiveIntent);
        }
        private static bool IsLookupReferenceFollowUp(string text)
        {
            text = (text ?? string.Empty).Trim().ToLowerInvariant();

            return text.Contains("con đầu tiên") ||
                   text.Contains("mẫu đầu tiên") ||
                   text.Contains("xe đầu tiên") ||
                   text.Contains("mẫu đó") ||
                   text.Contains("con đó") ||
                   text.Contains("xe đó") ||
                   text.Contains("mẫu kia") ||
                   text.Contains("con kia") ||
                   text.Contains("xe kia");
        }

        private static bool IsOrderReferenceFollowUp(string text)
        {
            text = (text ?? string.Empty).Trim().ToLowerInvariant();

            return text.Contains("đơn kia") ||
                   text.Contains("don kia") ||
                   text.Contains("đơn đó") ||
                   text.Contains("don do") ||
                   text.Contains("mã kia") ||
                   text.Contains("ma kia");
        }
        private static bool ShouldUseRecommendationRefinement(
            bool hasRecommendationContext,
            ParsedIntent effectiveIntent,
            string message)
        {
            if (!hasRecommendationContext)
                return false;

            if (ShouldForceDirectCompare(effectiveIntent) ||
                ShouldForceDirectProductLookup(effectiveIntent) ||
                ShouldForceSearchFromIntent(effectiveIntent))
            {
                return false;
            }

            bool hasRefinementSignals =
                effectiveIntent.IsFollowUp ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Brand) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Category) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Target) ||
                effectiveIntent.TargetPrice.HasValue ||
                effectiveIntent.PriceMin.HasValue ||
                effectiveIntent.PriceMax.HasValue ||
                effectiveIntent.ForWork ||
                effectiveIntent.ForSchool ||
                effectiveIntent.ForCity ||
                effectiveIntent.ForTour ||
                effectiveIntent.WantsFuelSaving ||
                effectiveIntent.WantsLargeStorage ||
                effectiveIntent.WantsEasyControl ||
                effectiveIntent.NeedsLowSeat ||
                effectiveIntent.PrefersMaleStyle ||
                effectiveIntent.PrefersFemaleStyle ||
                (effectiveIntent.RequestedStyles != null && effectiveIntent.RequestedStyles.Count > 0) ||
                (effectiveIntent.ExcludedBrands != null && effectiveIntent.ExcludedBrands.Count > 0) ||
                (effectiveIntent.ExcludedCategories != null && effectiveIntent.ExcludedCategories.Count > 0);

            if (!hasRefinementSignals)
                return false;

            if (LooksLikeNewStandaloneRequest(message, effectiveIntent))
                return false;

            return true;
        }

        private static bool HasAnyNamedProducts(ParsedIntent? intent, int minimumCount)
        {
            if (intent?.MentionedProducts == null)
                return false;

            return intent.MentionedProducts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() >= minimumCount;
        }

        private static bool LooksLikeNewStandaloneRequest(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (ShouldForceOrderLookup(intent) ||
                ShouldForceDirectCompare(intent) ||
                ShouldForceDirectProductLookup(intent) ||
                ShouldForceRecommendationFromIntent(intent) ||
                ShouldForceSearchFromIntent(intent))
            {
                return true;
            }

            var text = message.Trim().ToLowerInvariant();

            bool hasRestartSignal =
                text.Contains("tư vấn") ||
                text.Contains("tu van") ||
                text.Contains("gợi ý") ||
                text.Contains("goi y") ||
                text.StartsWith("xe ") ||
                text.StartsWith("tìm xe") ||
                text.StartsWith("tim xe") ||
                text.StartsWith("mua xe") ||
                text.StartsWith("so sánh") ||
                text.StartsWith("so sanh") ||
                text.StartsWith("đơn hàng") ||
                text.StartsWith("don hang");

            return hasRestartSignal && !intent.IsFollowUp;
        }

        private static bool LooksLikeExplicitCompareQuestion(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (HasAnyNamedProducts(intent, minimumCount: 2))
                return true;

            var text = message.Trim().ToLowerInvariant();

            bool hasCompareMarker =
                text.Contains("so sánh") ||
                text.Contains("so sanh") ||
                text.Contains("cái nào") ||
                text.Contains("xe nào") ||
                text.Contains("mẫu nào") ||
                text.Contains("ổn hơn") ||
                text.Contains("tot hon") ||
                text.Contains("tốt hơn") ||
                text.Contains("hợp hơn") ||
                text.Contains("hop hon") ||
                text.Contains("nhỉnh hơn") ||
                text.Contains("hon") ||
                text.Contains("khác nhau") ||
                text.Contains("khac nhau");

            return hasCompareMarker;
        }

        private static bool IsShortComparePriceFollowUp(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool doesNotNameTwoNewProducts = !HasAnyNamedProducts(intent, minimumCount: 2);

            bool looksLikeShortPriceQuestion =
                text == "giá bao nhiêu" ||
                text == "gia bao nhieu" ||
                text == "bao nhiêu" ||
                text == "bao nhieu" ||
                text == "mức giá" ||
                text == "gia sao" ||
                text.Contains("giá bao nhiêu") ||
                text.Contains("gia bao nhieu");

            return doesNotNameTwoNewProducts && looksLikeShortPriceQuestion;
        }
        private static bool ShouldTreatExpandAsRefinement(ParsedIntent? intent, string message)
        {
            if (intent == null)
                return false;

            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool hasStructuredConstraint =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                intent.ForWork ||
                intent.ForSchool ||
                intent.ForCity ||
                intent.ForTour ||
                intent.IsBrandSwitch;

            bool hasTextConstraint =
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio") ||
                text.Contains("xe ga") ||
                text.Contains("xe số") ||
                text.Contains("xe so") ||
                text.Contains("cốp") ||
                text.Contains("cop") ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("dễ đi") ||
                text.Contains("de di") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("dưới") ||
                text.Contains("duoi") ||
                text.Contains("trên") ||
                text.Contains("tren") ||
                text.Contains("tầm") ||
                text.Contains("tam") ||
                text.Contains("khoảng") ||
                text.Contains("khoang");

            return hasStructuredConstraint || hasTextConstraint;
        }
    }
}
