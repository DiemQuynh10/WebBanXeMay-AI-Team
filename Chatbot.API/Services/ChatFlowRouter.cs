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

            normalizedMessage ??= string.Empty;

            if (intent == null)
            {
                result.FlowType = ChatFlowType.Unknown;
                result.ShouldUseDeterministicFlow = false;
                result.ShouldUseRag = false;
                result.ShouldUseAiFallback = true;
                result.Reason = "Intent is null";
                return result;
            }

            var text = normalizedMessage.Trim().ToLowerInvariant();

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

            if ((intent.IsDirectProductLookup || !string.IsNullOrWhiteSpace(intent.LookupField)) &&
     intent.MentionedProducts != null &&
     intent.MentionedProducts.Count > 0)
            {
                result.FlowType = ChatFlowType.ProductLookup;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Direct product lookup with explicit product";
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
            bool isRefineFollowUp =
    hasActiveRecommendationContext &&
   RecommendationConversationRules.LooksLikeRefineWithinCurrentRecommendation(
    text,
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
            bool isExpandFollowUp =
                hasActiveRecommendationContext &&
                RecommendationConversationRules.LooksLikeExpandFromCurrentGoal(
    text,
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
            
              if (intent.IntentType == "refine")
            {
                if (profile?.HasActiveCompareContext == true &&
                    profile.LastComparedProducts != null &&
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
                result.FlowType = ChatFlowType.Refinement;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Brand switch routed as refinement";
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
    }
}