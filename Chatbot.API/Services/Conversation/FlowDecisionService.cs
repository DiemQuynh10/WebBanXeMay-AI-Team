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
            bool isLookupFollowUp =
    conversationProfile != null &&
    conversationProfile.LastLookupProductId.HasValue &&
    LooksLikeLookupFollowUp(normalizedMessage);

            if (isLookupFollowUp)
            {
                routing.FlowType = ChatFlowType.ProductLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by lookup follow-up";

                return routing;
            }
            bool forcedRecommendationFollowUp =
    contextDecision == RecommendationContextDecision.None &&
    conversationProfile != null &&
    conversationProfile.HasActiveRecommendationContext &&
    conversationProfile.BaseRecommendedProducts != null &&
    conversationProfile.BaseRecommendedProducts.Count > 0 &&
    routing.FlowType != ChatFlowType.Recommendation &&
    routing.FlowType != ChatFlowType.Refinement &&
    string.Equals(effectiveIntent.IntentType, "refine", StringComparison.OrdinalIgnoreCase);

            if (forcedRecommendationFollowUp)
            {
                routing.FlowType = ChatFlowType.RecommendationFollowUp;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by existing recommendation context + refine intent";

                return routing;
            }

            bool hasActiveRecommendationContext =
     conversationProfile != null &&
     conversationProfile.HasActiveRecommendationContext &&
     conversationProfile.BaseRecommendedProducts != null &&
     conversationProfile.BaseRecommendedProducts.Count > 0;

            bool currentMessageExplicitlyNamesTwoProducts =
                effectiveIntent.MentionedProducts != null &&
                effectiveIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() >= 2;

            bool compareShouldYieldToRecommendationNarrowing =
                hasActiveRecommendationContext &&
                contextDecision == RecommendationContextDecision.NarrowWithinCurrentSet &&
                !currentMessageExplicitlyNamesTwoProducts;

            bool mustKeepCompareFlow =
                string.Equals(effectiveIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(effectiveIntent.RouteFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase);

            bool forcedDirectCompare =
                effectiveIntent.IsDirectCompare && currentMessageExplicitlyNamesTwoProducts;

            if (!compareShouldYieldToRecommendationNarrowing &&
                (mustKeepCompareFlow || forcedDirectCompare))
            {
                routing.FlowType = ChatFlowType.Compare;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = forcedDirectCompare
                    ? "Forced by direct compare phrase"
                    : "Forced by compare intent";

                return routing;
            }

            bool forcedDirectLookup = LooksLikeDirectProductLookup(normalizedMessage, effectiveIntent);

            if (forcedDirectLookup)
            {
                routing.FlowType = ChatFlowType.ProductLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by direct product lookup phrase";

                return routing;
            }

            if (contextDecision == RecommendationContextDecision.StartFreshRecommendation)
            {
                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Forced by context decision: fresh recommendation";

                return routing;
            }

            if (contextDecision == RecommendationContextDecision.ExpandFromCurrentGoal)
            {
                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Forced by context decision: expand recommendation goal";

                return routing;
            }

            if (contextDecision == RecommendationContextDecision.NarrowWithinCurrentSet)
            {
                routing.FlowType = ChatFlowType.Refinement;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.Reason = "Forced by context decision: narrow refinement";

                return routing;
            }

            return routing;
        }
        private static bool LooksLikeLookupFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return
                text.Contains("còn hàng") ||
                text.Contains("con hang") ||
                text.Contains("còn không") ||
                text.Contains("con khong") ||
                text.Contains("còn không vậy") ||
                text.Contains("con khong vay") ||
                text.Contains("còn ko") ||
                text.Contains("con ko") ||
                text.Contains("hết hàng") ||
                text.Contains("het hang") ||
                text.Contains("tồn kho") ||
                text.Contains("ton kho") ||
                text.Contains("còn mấy chiếc") ||
                text.Contains("con may chiec") ||
                text.Contains("bao nhiêu chiếc") ||
                text.Contains("bao nhieu chiec") ||
                text.Contains("số lượng còn") ||
                text.Contains("so luong con");
        }
        private static bool LooksLikeDirectProductLookup(string message, ParsedIntent intent)
        {
            return intent.IsDirectProductLookup;
        }

        private static bool LooksLikeDirectCompareRequest(string message, ParsedIntent intent)
        {
            return intent.IsDirectCompare;
        }
    }
}