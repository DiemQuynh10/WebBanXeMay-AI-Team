using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Conversation;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ChatFlowRouter : IChatFlowRouter
    {
        public FlowRoutingResult Route(
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            var result = new FlowRoutingResult();
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();

            if (intent.IsGreeting)
            {
                result.FlowType = ChatFlowType.Greeting;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Greeting detected";
                return result;
            }

            if (intent.IsOutOfScope)
            {
                result.FlowType = ChatFlowType.OutOfScope;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Out-of-scope detected";
                return result;
            }

            if (intent.IsOrderLookup)
            {
                result.FlowType = ChatFlowType.OrderLookup;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Order lookup detected";
                return result;
            }

            if (intent.IsDirectCompare)
            {
                result.FlowType = ChatFlowType.Compare;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Direct compare detected";
                return result;
            }

            if (intent.IsDirectProductLookup)
            {
                result.FlowType = ChatFlowType.ProductLookup;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Direct product lookup detected";
                return result;
            }
            if (!string.IsNullOrWhiteSpace(intent.LookupField) &&
    !string.IsNullOrWhiteSpace(profile?.LastLookupProductName))
            {
                result.FlowType = ChatFlowType.ProductLookup;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Lookup follow-up detected from previous product context";
                return result;
            }
            bool hasActiveRecommendationContext = HasRecommendationContext(profile);

            bool isExpandFollowUp =
                hasActiveRecommendationContext &&
                RecommendationConversationRules.LooksLikeExpandFromCurrentGoal(
                    normalizedMessage,
                    intent,
                    profile);

            if (isExpandFollowUp)
            {
                result.FlowType = ChatFlowType.Recommendation;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = true;
                result.Reason = "Recommendation follow-up routed to expand";
                return result;
            }
            bool shouldForceExpandFollowUp =
    ShouldForceExpandFromCurrentGoal(
        normalizedMessage,
        intent,
        profile);

            if (shouldForceExpandFollowUp)
            {
                result.FlowType = ChatFlowType.Recommendation;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = true;
                result.Reason = "Partial follow-up routed to expand from current goal";
                return result;
            }

            bool isRefineFollowUp =
                hasActiveRecommendationContext &&
                RecommendationConversationRules.LooksLikeRefineWithinCurrentRecommendation(
                    normalizedMessage,
                    intent,
                    profile);

            if (isRefineFollowUp)
            {
                result.FlowType = ChatFlowType.Refinement;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Recommendation follow-up routed to refinement";
                return result;
            }

            if (intent.IntentType == "refine")
            {
                if (profile?.HasActiveCompareContext == true &&
                    profile.LastComparedProducts.Count >= 2 &&
                    !string.IsNullOrWhiteSpace(intent.ComparisonFeature))
                {
                    result.FlowType = ChatFlowType.Compare;
                    result.ShouldUseDeterministicFlow = true;
                    result.ShouldUseAiFallback = false;
                    result.Reason = "Compare context overrides refine";
                    return result;
                }

                result.FlowType = ChatFlowType.Refinement;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Refinement detected";
                return result;
            }

            if (intent.IsBrandSwitch)
            {
                result.FlowType = ChatFlowType.BrandSwitch;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Brand switch detected";
                return result;
            }

            bool isHardFilterOnlySearch =
    FlowIntentHeuristics.IsHardFilterOnlySearch(intent, normalizedMessage);

            bool isPriceOnlySearch =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue;

            if (!hasActiveRecommendationContext &&
                (isHardFilterOnlySearch || intent.IsProductSearch || isPriceOnlySearch))
            {
                result.FlowType = ChatFlowType.ProductSearch;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = isHardFilterOnlySearch
                    ? "Hard filter only search"
                    : (intent.IsProductSearch ? "Product search detected" : "Price-only product search detected");
                return result;
            }

            if (intent.IsOpenRecommendation)
            {
                result.FlowType = ChatFlowType.Recommendation;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseRag = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Open recommendation detected";
                return result;
            }

            result.FlowType = ChatFlowType.Unknown;
            result.ShouldUseDeterministicFlow = false;
            result.ShouldUseRag = false;
            result.ShouldUseAiFallback = true;
            result.Reason = "Fallback";
            return result;
        }
        private static bool HasRecommendationContext(CustomerPreferenceProfile? profile)
        {
            return profile?.HasActiveRecommendationContext == true &&
                   profile.LastRecommendedProducts.Count > 0;
        }
        private static bool IsPartialRecommendationFollowUp(
    string normalizedMessage,
    ParsedIntent intent)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();

            bool hasStructuredFilter =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                intent.IsBrandSwitch ||
                intent.IntentType == "brand_switch";

            if (hasStructuredFilter)
                return true;

            if (text.Contains("xe ga") ||
                text.Contains("xe số") ||
                text.Contains("xe so") ||
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio") ||
                text.Contains("tầm") ||
                text.Contains("tam") ||
                text.Contains("khoảng") ||
                text.Contains("khoang") ||
                text.Contains("trên") ||
                text.Contains("duới") ||
                text.Contains("dưới"))
            {
                return true;
            }

            return false;
        }
        private static bool ShouldForceExpandFromCurrentGoal(
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile? profile)
        {
            if (!HasRecommendationContext(profile))
                return false;

            if (!IsPartialRecommendationFollowUp(normalizedMessage, intent))
                return false;

            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();

            bool looksLikeComparison =
                text.Contains("cái nào") ||
                text.Contains("cai nao") ||
                text.Contains("so sánh") ||
                text.Contains("so sanh") ||
                text.Contains("hợp hơn") ||
                text.Contains("tot hon");

            if (looksLikeComparison)
                return false;

            return true;
        }
    }
}