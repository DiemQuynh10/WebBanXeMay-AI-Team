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

            if (string.Equals(intent.IntentType, ChatFlowType.ServiceInfo, StringComparison.OrdinalIgnoreCase))
            {
                result.FlowType = ChatFlowType.ServiceInfo;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = true;
                result.Reason = "Service information detected";
                return result;
            }

            if (string.Equals(intent.IntentType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase))
            {
                result.FlowType = ChatFlowType.PolicyInfo;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = true;
                result.Reason = "Policy information detected";
                return result;
            }

            if (FlowIntentHeuristics.IsStaticKnowledgeQuestion(text))
            {
                result.FlowType = ChatFlowType.Unknown;
                result.ShouldUseDeterministicFlow = false;
                result.ShouldUseRag = true;
                result.ShouldUseAiFallback = true;
                result.Reason = "Static knowledge question detected";
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
            if (profile?.HasActiveCompareContext == true &&
                profile.LastComparedProducts.Count >= 2 &&
                string.Equals(intent.IntentType, "followup", StringComparison.OrdinalIgnoreCase))
            {
                result.FlowType = ChatFlowType.Compare;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Compare follow-up detected";
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
            bool hasActiveRecommendationContext =
    profile?.HasActiveRecommendationContext == true &&
    profile.LastRecommendedProducts.Count > 0;

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
                result.Reason = intent.PriceMin.HasValue || intent.PriceMax.HasValue || intent.TargetPrice.HasValue || intent.FilterType != PriceFilterType.None
                    ? "Recommendation context + filter fragment detected"
                    : (string.Equals(intent.IntentType, "followup", StringComparison.OrdinalIgnoreCase)
                        ? "Recommendation follow-up with hard refinement signals"
                        : "Recommendation context + hard refinement signals");
                return result;
            }

            if (hasActiveRecommendationContext &&
                string.Equals(intent.IntentType, "followup", StringComparison.OrdinalIgnoreCase))
            {
                result.FlowType = ChatFlowType.RecommendationFollowUp;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = true;
                result.Reason = "Recommendation follow-up detected";
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
                result.Reason = intent.IsProductSearch
                    ? "Product search detected"
                    : (isPriceOnlySearch ? "Price-only product search detected" : "Hard filter only search");
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
    }
}
