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

            if (FlowIntentHeuristics.IsStaticKnowledgeQuestion(normalizedMessage))
            {
                routing.FlowType = ChatFlowType.Unknown;
                routing.ShouldUseDeterministicFlow = false;
                routing.ShouldUseAiFallback = true;
                routing.ShouldUseRag = true;
                routing.Reason = "Static knowledge question overrides conversation context";
                return routing;
            }

            bool hasRecommendationContext =
                conversationProfile != null &&
                conversationProfile.HasActiveRecommendationContext &&
                conversationProfile.BaseRecommendedProducts != null &&
                conversationProfile.BaseRecommendedProducts.Count > 0;

            bool hasCompareContext =
                conversationProfile != null &&
                conversationProfile.HasActiveCompareContext &&
                conversationProfile.LastComparedProducts != null &&
                conversationProfile.LastComparedProducts.Count >= 2;

            bool isLookupFollowUp =
                conversationProfile != null &&
                conversationProfile.LastLookupProductId.HasValue &&
                LookupConversationRules.LooksLikeLookupFollowUp(normalizedMessage);

            if (isLookupFollowUp)
            {
                routing.FlowType = ChatFlowType.ProductLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by lookup follow-up";
                return routing;
            }

            bool isCompareFollowUp =
                hasCompareContext &&
                (
                    CompareConversationRules.IsCompareFeatureFollowUpQuestion(
                        effectiveIntent,
                        conversationProfile,
                        normalizedMessage)
                    || IsShortComparePriceFollowUp(normalizedMessage, effectiveIntent)
                );

            if (isCompareFollowUp)
            {
                routing.FlowType = ChatFlowType.Compare;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by compare follow-up context";
                return routing;
            }

            bool currentMessageExplicitlyNamesTwoProducts =
                effectiveIntent.MentionedProducts != null &&
                effectiveIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() >= 2;

            bool forcedDirectCompare =
                effectiveIntent.IsDirectCompare && currentMessageExplicitlyNamesTwoProducts;

            if (contextDecision == RecommendationContextDecision.NarrowWithinCurrentSet &&
                hasRecommendationContext &&
                !currentMessageExplicitlyNamesTwoProducts)
            {
                routing.FlowType = ChatFlowType.Refinement;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Mapped from NarrowWithinCurrentSet";
                return routing;
            }

            if (forcedDirectCompare ||
                string.Equals(effectiveIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(effectiveIntent.RouteFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
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

            if (effectiveIntent.IsDirectProductLookup)
            {
                routing.FlowType = ChatFlowType.ProductLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by direct product lookup phrase";
                return routing;
            }

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

            if (hasRecommendationContext &&
                string.Equals(effectiveIntent.IntentType, "refine", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(routing.FlowType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                routing.FlowType = ChatFlowType.RecommendationFollowUp;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by recommendation follow-up context";
                return routing;
            }

            if (FlowIntentHeuristics.IsHardFilterOnlySearch(effectiveIntent, normalizedMessage) &&
                !hasRecommendationContext)
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

        private static bool IsShortComparePriceFollowUp(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool doesNotNameTwoNewProducts =
                intent?.MentionedProducts == null ||
                intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() < 2;

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
    }
}
