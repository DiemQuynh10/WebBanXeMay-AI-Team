using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using System.Text.RegularExpressions;
using static Chatbot.API.Models.Intent.ParsedIntent;

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

            if (conversationProfile?.HasPendingOrderLookup == true)
            {
                routing.FlowType = ChatFlowType.OrderLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by pending order lookup state";
                return routing;
            }
            bool hasRecommendationContext = HasRecommendationContext(conversationProfile);
            bool hasCompareContext = HasCompareContext(conversationProfile);
            ResolveOrdinalProductsIfNeeded(effectiveIntent, conversationProfile, normalizedMessage);
            bool hasLookupContext = HasLookupContext(conversationProfile);
            if (string.Equals(effectiveIntent.FollowUpType, "alternative_after_compare", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.RouteFlow = ChatFlowType.Recommendation;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.IsOpenRecommendation = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.ExcludePreviousProducts = false;

                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Alternative recommendation after compare";
                return routing;
            }
            Console.WriteLine(
    $"[FLOW DECISION INPUT] Text={text} | IntentType={effectiveIntent.IntentType} | RouteFlow={effectiveIntent.RouteFlow} | FollowUpType={effectiveIntent.FollowUpType} | " +
    $"HasCompareContext={hasCompareContext} | " +
    $"ExcludedBrands={string.Join(",", effectiveIntent.ExcludedBrands)} | " +
    $"ExcludedProducts={string.Join(",", effectiveIntent.ExcludedProducts)} | " +
    $"MentionedProducts={string.Join(",", effectiveIntent.MentionedProducts)}");
            if (LooksLikePickOneRequest(text) &&
    (
        hasRecommendationContext ||
        (conversationProfile?.CurrentRecommendedProducts?.Count > 0) ||
        (conversationProfile?.BaseRecommendedProducts?.Count > 0) ||
        (conversationProfile?.LastRecommendedProducts?.Count > 0) ||
        (conversationProfile?.LastMentionedProducts?.Count > 0)
    ))
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.RouteFlow = ChatFlowType.RecommendationFollowUp;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "pick_best";
                effectiveIntent.IsOpenRecommendation = false;
                effectiveIntent.IsDirectCompare = false;

                routing.FlowType = ChatFlowType.RecommendationFollowUp;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Pick best from current recommendation list by text rule";
                return routing;
            }
            if (string.Equals(effectiveIntent.FollowUpType, "pick_best", StringComparison.OrdinalIgnoreCase))
            {
                bool hasAnyProductContext =
                    hasRecommendationContext ||
                    hasCompareContext ||
                    (conversationProfile?.CurrentRecommendedProducts?.Count > 0) ||
                    (conversationProfile?.BaseRecommendedProducts?.Count > 0) ||
                    (conversationProfile?.LastRecommendedProducts?.Count > 0) ||
                    (conversationProfile?.LastMentionedProducts?.Count > 0) ||
                    (conversationProfile?.LastComparedProducts?.Count > 0);

                if (hasAnyProductContext)
                {
                    routing.FlowType = ChatFlowType.RecommendationFollowUp;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Pick best from active product context";
                    return routing;
                }

                routing.FlowType = ChatFlowType.Unknown;
                routing.ShouldUseDeterministicFlow = false;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Pick best requested but no active context";
                return routing;
            }
            if (hasRecommendationContext &&
     !MessageAsksGlobalScope(text) &&
     LooksLikeCheapestInRecommendationList(text))
            {
                effectiveIntent.IntentType = "followup";
                effectiveIntent.RouteFlow = ChatFlowType.RecommendationFollowUp;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "cheapest_in_list";
                effectiveIntent.ComparisonFeature = "price";
                effectiveIntent.KeepConstraints = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsOpenRecommendation = false;

                routing.FlowType = ChatFlowType.RecommendationFollowUp;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Cheapest product from current recommendation list";
                return routing;
            }
            if (hasRecommendationContext &&
    LooksLikeCompareFeatureWithinRecommendation(text, effectiveIntent))
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.RouteFlow = ChatFlowType.RecommendationFollowUp;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "pick_best";
                effectiveIntent.KeepConstraints = true;
                effectiveIntent.ExcludePreviousProducts = false;
                effectiveIntent.HasNarrowRefinementSignal = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsOpenRecommendation = false;

                routing.FlowType = ChatFlowType.RecommendationFollowUp;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Pick best by feature within current recommendation list";
                return routing;
            }
            if (IsProductCategoryQuestion(text, effectiveIntent))
            {
                effectiveIntent.IntentType = "product_lookup";
                effectiveIntent.IsDirectProductLookup = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.LookupField = "category";

                return RouteTo(
                    ChatFlowType.ProductLookup,
                    "Forced by product category question before follow-up rerank");
            }
            if (ShouldRouteToRagPolicy(effectiveIntent, text))
            {
                routing.FlowType = ChatFlowType.RagPolicy;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Forced by policy/FAQ intent before recommendation follow-up";
                return routing;
            }
            if (string.Equals(effectiveIntent.FollowUpType, "rerank_previous_list", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.RouteFlow = ChatFlowType.Refinement;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsOpenRecommendation = false;
                effectiveIntent.KeepConstraints = true;
                effectiveIntent.ExcludePreviousProducts = true;
                effectiveIntent.HasNarrowRefinementSignal = true;

                routing.FlowType = ChatFlowType.Refinement;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Retry recommendation while keeping active constraints";

                return routing;
            }
            if (string.Equals(effectiveIntent.FollowUpType, "restart_recommendation", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.RouteFlow = ChatFlowType.Recommendation;
                effectiveIntent.IsFollowUp = false;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsOpenRecommendation = true;

                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Restart recommendation after full profile reset";
                return routing;
            }
            if (LooksLikeValueForMoneyQuestion(text))
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.RouteFlow = ChatFlowType.Recommendation;
                effectiveIntent.IsOpenRecommendation = true;
                effectiveIntent.HasFreshConsultationSignal = true;
                effectiveIntent.IsFollowUp = false;
                effectiveIntent.FollowUpType = null;
                effectiveIntent.WantsFuelSaving = true;
                effectiveIntent.WantsEasyControl = true;
                effectiveIntent.IsOutOfScope = false;
                effectiveIntent.IsNoise = false;

                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Value-for-money recommendation";
                return routing;
            }
            if (ShouldForceOrderLookup(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.OrderLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit order lookup intent";
                return routing;
            }
            if (ShouldRouteToRagPolicy(effectiveIntent, text))
            {
                routing.FlowType = ChatFlowType.RagPolicy;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Forced by policy/FAQ question";
                return routing;
            }
            if (LooksLikeAlternativeRecommendationRequest(text) && hasRecommendationContext)
            {
                routing.FlowType = ChatFlowType.Refinement;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced alternative recommendation before compare";
                return routing;
            }
            if (hasCompareContext &&
    string.Equals(effectiveIntent.FollowUpType, "compare_feature", StringComparison.OrdinalIgnoreCase))
            {
                routing.FlowType = ChatFlowType.Compare;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced compare feature follow-up";
                return routing;
            }
            if (LooksLikeAlternativeRecommendationRequest(text) && hasCompareContext)
            {
                PrepareAlternativeRecommendationIntent(effectiveIntent, conversationProfile);

                routing.FlowType = ChatFlowType.Recommendation;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = true;
                routing.Reason = "Forced alternative recommendation from compare context";
                return routing;
            }
            if (IsProductCategoryQuestion(text, effectiveIntent))
            {
                effectiveIntent.IntentType = "product_lookup";
                effectiveIntent.IsDirectProductLookup = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.LookupField = "category";

                return RouteTo(
                    ChatFlowType.ProductLookup,
                    "Forced by product category question");
            }
            if (hasCompareContext &&
      HasExclusionConstraint(effectiveIntent) &&
      string.Equals(effectiveIntent.FollowUpType, "exclude", StringComparison.OrdinalIgnoreCase))
            {
                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Unknown,
                    ShouldUseDeterministicFlow = false,
                    ShouldUseAiFallback = false,
                    ShouldUseRag = false,
                    Reason = "Ambiguous exclusion while compare context is active"
                };
            }
            if (string.Equals(effectiveIntent.FollowUpType, "compare_missing_product", StringComparison.OrdinalIgnoreCase))
            {
                // Case 1: user vừa trả lời sản phẩm thứ 2
                if (hasCompareContext &&
                    effectiveIntent.MentionedProducts.Count == 1)
                {
                    var existingProducts = conversationProfile?.LastComparedProducts ?? new List<string>();

                    var combinedProducts = existingProducts
                        .Concat(effectiveIntent.MentionedProducts)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (combinedProducts.Count >= 2)
                    {
                        effectiveIntent.MentionedProducts = combinedProducts;

                        return new FlowRoutingResult
                        {
                            FlowType = ChatFlowType.Compare,
                            ShouldUseDeterministicFlow = true,
                            ShouldUseAiFallback = false,
                            ShouldUseRag = false,
                            Reason = "Recovered compare from missing product follow-up"
                        };
                    }
                }

                // Case 2: thiếu sản phẩm → hỏi lại
                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Unknown,
                    ShouldUseDeterministicFlow = false,
                    ShouldUseAiFallback = false,
                    ShouldUseRag = false,
                    Reason = "Compare missing second product"
                };
            }
            if (LooksLikePickOneRequest(text) &&
     hasCompareContext &&
     conversationProfile?.LastComparedProducts != null &&
     conversationProfile.LastComparedProducts.Count >= 2)
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.RouteFlow = ChatFlowType.Recommendation;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "pick_best";

                routing.FlowType = ChatFlowType.RecommendationFollowUp;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Pick one from active compare context";
                return routing;
            }
            effectiveIntent.IsOpenRecommendation = false;
            if (ShouldForceDirectCompare(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.Compare;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit compare intent";
                return routing;
            }
            if (IsExplicitCompareRequest(text, effectiveIntent) &&
    effectiveIntent.MentionedProducts.Count == 1)
            {
                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Unknown,
                    ShouldUseDeterministicFlow = false,
                    ShouldUseAiFallback = false,
                    ShouldUseRag = false,
                    Reason = "Explicit compare but only one product resolved"
                };
            }
            if (hasCompareContext &&
     effectiveIntent.MentionedProducts.Count == 1 &&
     string.IsNullOrWhiteSpace(effectiveIntent.FollowUpType) &&
     !effectiveIntent.IsDirectProductLookup &&
     string.IsNullOrWhiteSpace(effectiveIntent.LookupField))
            {
                var existingProducts = conversationProfile?.LastComparedProducts ?? new List<string>();

                var combinedProducts = existingProducts
                    .Concat(effectiveIntent.MentionedProducts)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (combinedProducts.Count >= 2)
                {
                    effectiveIntent.MentionedProducts = combinedProducts;
                    effectiveIntent.IntentType = "compare";
                    effectiveIntent.IsDirectCompare = true;
                    effectiveIntent.IsDirectProductLookup = false;
                    effectiveIntent.LookupField = null;
                    effectiveIntent.RouteFlow = ChatFlowType.Compare;

                    return new FlowRoutingResult
                    {
                        FlowType = ChatFlowType.Compare,
                        ShouldUseDeterministicFlow = true,
                        ShouldUseAiFallback = false,
                        ShouldUseRag = false,
                        Reason = "Recovered compare from single product answer"
                    };
                }
            }
            if (LooksLikeBudgetExpansion(text) && hasRecommendationContext)
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.RouteFlow = ChatFlowType.Refinement;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "expand_recommendation";
                effectiveIntent.IsDirectProductLookup = false;
                effectiveIntent.LookupField = null;

                routing.FlowType = ChatFlowType.Refinement;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Budget expansion refinement";
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
            if (LooksLikeBareCompareCurrentListRequest(text) &&
    hasRecommendationContext)
            {
                var currentCount = conversationProfile?.CurrentRecommendedProducts?.Count
                    ?? conversationProfile?.LastRecommendedProducts?.Count
                    ?? 0;

                if (currentCount > 2)
                {
                    routing.FlowType = ChatFlowType.Unknown;
                    routing.ShouldUseDeterministicFlow = false;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Bare compare request with multiple recommendation candidates";
                    return routing;
                }

                effectiveIntent.IntentType = "compare";
                effectiveIntent.RouteFlow = ChatFlowType.Compare;
                effectiveIntent.IsDirectCompare = true;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "compare";
                effectiveIntent.IsOpenRecommendation = false;

                effectiveIntent.ExcludedBrands.Clear();
                effectiveIntent.ExcludedProducts.Clear();
                effectiveIntent.ExcludedCategories.Clear();

                routing.FlowType = ChatFlowType.Compare;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Bare compare current recommendation list";
                return routing;
            }
            bool hasPriceRefinement =
    effectiveIntent.PriceMin.HasValue ||
    effectiveIntent.PriceMax.HasValue ||
    effectiveIntent.TargetPrice.HasValue ||
    effectiveIntent.FilterType != PriceFilterType.None;

            if (hasRecommendationContext &&
                HasExclusionConstraint(effectiveIntent) &&
                ExcludedBrandNotInCurrentRecommendation(effectiveIntent, conversationProfile))
            {
                if (hasPriceRefinement)
                {
                    effectiveIntent.IntentType = "refine";
                    effectiveIntent.RouteFlow = ChatFlowType.Refinement;
                    effectiveIntent.IsFollowUp = true;
                    effectiveIntent.FollowUpType = "price_refine_after_exclusion";
                    effectiveIntent.HasNarrowRefinementSignal = true;

                    routing.FlowType = ChatFlowType.Refinement;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Price refinement after exclusion";
                    return routing;
                }

                routing.FlowType = ChatFlowType.Unknown;
                routing.ShouldUseDeterministicFlow = false;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Excluded brand not present in current recommendation list";
                return routing;
            }
            if (!IsExplicitCompareRequest(text, effectiveIntent) &&
     HasExclusionConstraint(effectiveIntent))
            {
                if (IsFreshRecommendationWithExclusion(effectiveIntent) &&
                    LooksLikeExplicitFreshRecommendationRequest(text, effectiveIntent))
                {
                    MarkAsFreshRecommendation(effectiveIntent);
                    ClearCompareContext(conversationProfile);

                    return RouteTo(
                        ChatFlowType.Recommendation,
                        "Fresh recommendation with exclusion",
                        useRag: true);
                }

                if (hasRecommendationContext || effectiveIntent.IsFollowUp)
                {
                    MarkAsRefinementExclude(effectiveIntent);
                    ClearCompareContext(conversationProfile);

                    return RouteTo(
                        ChatFlowType.Refinement,
                        "Recommendation refinement with exclusion");
                }

                if (IsFreshRecommendationWithExclusion(effectiveIntent))
                {
                    MarkAsFreshRecommendation(effectiveIntent);
                    ClearCompareContext(conversationProfile);

                    return RouteTo(
                        ChatFlowType.Recommendation,
                        "Fresh recommendation with exclusion",
                        useRag: true);
                }
            }

            if (ShouldForceSearchFromIntent(effectiveIntent))
            {
                if (hasRecommendationContext && LooksLikeCategoryRefinement(text, effectiveIntent))
                {
                    effectiveIntent.IntentType = "refine";
                    effectiveIntent.RouteFlow = ChatFlowType.Refinement;
                    effectiveIntent.IsFollowUp = true;
                    effectiveIntent.FollowUpType = "narrow_refinement";

                    routing.FlowType = ChatFlowType.Refinement;
                    routing.ShouldUseDeterministicFlow = true;
                    routing.ShouldUseAiFallback = false;
                    routing.ShouldUseRag = false;
                    routing.Reason = "Category search converted to recommendation refinement";
                    return routing;
                }

                routing.FlowType = ChatFlowType.ProductSearch;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit product search intent";
                return routing;
            }
            if (effectiveIntent.Action == ConversationAction.ChangeProduct ||
    string.Equals(effectiveIntent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase))
            {
                routing.FlowType = ChatFlowType.Refinement;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by ChangeProduct action";
                return routing;
            }
            // 2) ContextDecision là quyết định hội thoại chính
            switch (contextDecision)
            {
                case RecommendationContextDecision.New:
                    ClearCompareContext(conversationProfile);

                    return RouteTo(
                        ChatFlowType.Recommendation,
                        "ContextDecision:New",
                        useRag: true);

                case RecommendationContextDecision.Continue:
                    if (ShouldTreatExpandAsRefinement(effectiveIntent, text))
                    {
                        return RouteTo(
                            ChatFlowType.Refinement,
                            "ContextDecision:Continue converted to Refinement");
                    }

                    return RouteTo(
                        ChatFlowType.Recommendation,
                        "ContextDecision:Continue",
                        useRag: true);

                case RecommendationContextDecision.Refine:
                    ClearCompareContext(conversationProfile);

                    return RouteTo(
                        ChatFlowType.Refinement,
                        "ContextDecision:Refine");

                case RecommendationContextDecision.Pivot:
                    if (IsClearBaseRouting(baseRouting))
                    {
                        baseRouting.ShouldUseDeterministicFlow = true;
                        baseRouting.ShouldUseAiFallback = false;
                        baseRouting.Reason = $"ContextDecision:Pivot -> {baseRouting.Reason}";
                        return baseRouting;
                    }

                    return RouteTo(
                        ChatFlowType.Unknown,
                        "ContextDecision:Pivot but base routing is unknown",
                        useAiFallback: true);

                case RecommendationContextDecision.Ambiguous:
                    return new FlowRoutingResult
                    {
                        FlowType = ChatFlowType.Unknown,
                        ShouldUseDeterministicFlow = false,
                        ShouldUseAiFallback = false,
                        ShouldUseRag = false,
                        Reason = "ContextDecision:Ambiguous - ask clarification"
                    };
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

            bool hasStrongPolicyDecision =
     contextDecision == RecommendationContextDecision.New ||
     contextDecision == RecommendationContextDecision.Continue ||
     contextDecision == RecommendationContextDecision.Refine ||
     contextDecision == RecommendationContextDecision.Pivot ||
     contextDecision == RecommendationContextDecision.Ambiguous;

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

            if (FlowIntentHeuristics.IsHardFilterOnlySearch(effectiveIntent, text) &&
    !hasRecommendationContext &&
    !HasExclusionConstraint(effectiveIntent))
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
            if (profile == null)
                return false;

            bool activeCompare =
                string.Equals(profile.ActiveFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase);

            bool hasComparedProducts =
                profile.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 1;

            bool hasMentionedCompareProducts =
                profile.LastMentionedProducts != null &&
                profile.LastMentionedProducts.Count >= 2;

            return activeCompare ||
                   (profile.HasActiveCompareContext && (hasComparedProducts || hasMentionedCompareProducts));
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

            if (!namesTwoProducts)
                return false;

            return intent.IsDirectCompare ||
                   string.Equals(intent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase);
        }
        private static bool ShouldForceDirectProductLookup(ParsedIntent? intent)
        {
            if (intent == null) return false;

            bool hasProduct =
                intent.MentionedProducts != null &&
                intent.MentionedProducts
                    .Any(x => !string.IsNullOrWhiteSpace(x));

            bool hasLookupField =
                !string.IsNullOrWhiteSpace(intent.LookupField);

            return hasProduct &&
                   (
                       intent.IsDirectProductLookup ||
                       string.Equals(intent.IntentType, "product_lookup", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(intent.RouteFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) ||
                       hasLookupField
                   );
        }

        private static bool ShouldForceRecommendationFromIntent(ParsedIntent? intent)
        {
            if (intent == null) return false;

            if (ShouldForceOrderLookup(intent) ||
                ShouldForceDirectCompare(intent) ||
                ShouldForceDirectProductLookup(intent) ||
                ShouldForceSearchFromIntent(intent))
            {
                return false;
            }

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
            if (string.Equals(effectiveIntent.FollowUpType, "pick_best", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(effectiveIntent.FollowUpType, "rerank_previous_list", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (HasExclusionConstraint(effectiveIntent) ||
                string.Equals(effectiveIntent.FollowUpType, "exclude", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(effectiveIntent.FollowUpType, "exclude_product", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(effectiveIntent.IntentType, "refine", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (LooksLikeAlternativeRecommendationRequest(message))
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
            text = NormalizeText(text);

            return text.Contains("con nay") ||
                   text.Contains("mau nay") ||
                   text.Contains("xe nay") ||
                   text.Contains("con do") ||
                   text.Contains("mau do") ||
                   text.Contains("xe do") ||
                   text.Contains("con kia") ||
                   text.Contains("mau kia") ||
                   text.Contains("xe kia") ||
                   text.Contains("con dau tien") ||
                   text.Contains("mau dau tien") ||
                   text.Contains("xe dau tien");
        }

        private static bool IsOrderReferenceFollowUp(string text)
        {
            text = NormalizeText(text);

            return text.Contains("don kia") ||
                   text.Contains("don do") ||
                   text.Contains("don nay") ||
                   text.Contains("ma kia") ||
                   text.Contains("ma do") ||
                   text.Contains("ma nay") ||
                   text.Contains("co giao chua") ||
                   text.Contains("giao chua") ||
                   text.Contains("dang giao chua") ||
                   text.Contains("trang thai sao") ||
                   text.Contains("tinh trang sao") ||
                   text.Contains("don toi dau roi") ||
                   text.Contains("don den dau roi");
        }
        private static bool ShouldUseRecommendationRefinement(
            bool hasRecommendationContext,
            ParsedIntent effectiveIntent,
            string message)
        {
            if (!hasRecommendationContext)
                return false;
            if (LooksLikeExplicitFreshRecommendationRequest(message, effectiveIntent))
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
                (effectiveIntent.ExcludedCategories != null && effectiveIntent.ExcludedCategories.Count > 0)
                || LooksLikeAlternativeRecommendationRequest(message);
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
        private static bool LooksLikeExplicitFreshRecommendationRequest(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message) || intent == null)
                return false;

            var text = NormalizeText(message);

            bool hasFreshVerb =
                text.Contains("tu van") ||
                text.Contains("goi y") ||
                text.Contains("nen mua") ||
                text.Contains("chon xe") ||
                text.Contains("tim xe") ||
                text.Contains("muon mua") ||
                text.Contains("can xe");

            bool hasConstraint =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                intent.ForWork ||
                intent.ForSchool ||
                intent.ForCity ||
                intent.ForTour ||
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                intent.PrefersMaleStyle ||
                intent.PrefersFemaleStyle;

            bool hasReferenceSignal =
                text.Contains("con ") ||
                text.Contains("con nay") ||
                text.Contains("con do") ||
                text.Contains("mau nay") ||
                text.Contains("mau do") ||
                text.Contains("xe nay") ||
                text.Contains("xe do") ||
                text.Contains("thi sao") ||
                text.Contains("vay con") ||
                text.Contains("the con");

            return hasFreshVerb && hasConstraint && !hasReferenceSignal;
        }

        private static bool LooksLikeNewStandaloneRequest(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (ShouldForceOrderLookup(intent) ||
                ShouldForceDirectCompare(intent) ||
                ShouldForceDirectProductLookup(intent) ||
                ShouldForceSearchFromIntent(intent))
            {
                return true;
            }

            if (LooksLikeExplicitFreshRecommendationRequest(message, intent))
                return true;

            var text = NormalizeText(message);

            bool hasRestartSignal =
                text.Contains("doi chu de") ||
                text.Contains("bo cai truoc") ||
                text.Contains("quay lai tu dau") ||
                text.Contains("reset") ||
                text.StartsWith("don hang") ||
                text.StartsWith("so sanh");

            return hasRestartSignal && !intent.IsFollowUp;
        }
        private static bool LooksLikeAlternativeRecommendationRequest(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);

            bool asksOtherOption =
                text.Contains("con xe nao") ||
                text.Contains("xe nao") ||
                text.Contains("mau nao") ||
                text.Contains("con nao") ||
                text.Contains("xe khac") ||
                text.Contains("mau khac") ||
                text.Contains("con khac") ||
                text.Contains("hang khac") || 
                text.Contains("lua chon khac");

            return asksOtherOption;
        }
        private static bool LooksLikeExplicitCompareQuestion(string message, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            if (HasAnyNamedProducts(intent, minimumCount: 2))
                return true;

            var text = NormalizeText(message);
            if (LooksLikeAlternativeRecommendationRequest(message))
                return false;

            bool hasCompareMarker =
                text.Contains("so sanh") ||
                text.Contains("so voi") ||
                text.Contains("khac nhau") ||
                text.Contains("cai nao") ||
                text.Contains("xe nao") ||
                text.Contains("mau nao") ||
                text.Contains("on hon") ||
                text.Contains("tot hon") ||
                text.Contains("hop hon") ||
                text.Contains("nhinh hon") ||
                text.Contains("re hon") ||
                text.Contains("dat hon") ||
                text.Contains("rong hon") ||
                text.Contains("thap hon") ||
                text.Contains("nhe hon");

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
        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim().ToLowerInvariant();

            text = text
                .Replace('à', 'a').Replace('á', 'a').Replace('ạ', 'a').Replace('ả', 'a').Replace('ã', 'a')
                .Replace('â', 'a').Replace('ầ', 'a').Replace('ấ', 'a').Replace('ậ', 'a').Replace('ẩ', 'a').Replace('ẫ', 'a')
                .Replace('ă', 'a').Replace('ằ', 'a').Replace('ắ', 'a').Replace('ặ', 'a').Replace('ẳ', 'a').Replace('ẵ', 'a')
                .Replace('è', 'e').Replace('é', 'e').Replace('ẹ', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e')
                .Replace('ê', 'e').Replace('ề', 'e').Replace('ế', 'e').Replace('ệ', 'e').Replace('ể', 'e').Replace('ễ', 'e')
                .Replace('ì', 'i').Replace('í', 'i').Replace('ị', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i')
                .Replace('ò', 'o').Replace('ó', 'o').Replace('ọ', 'o').Replace('ỏ', 'o').Replace('õ', 'o')
                .Replace('ô', 'o').Replace('ồ', 'o').Replace('ố', 'o').Replace('ộ', 'o').Replace('ổ', 'o').Replace('ỗ', 'o')
                .Replace('ơ', 'o').Replace('ờ', 'o').Replace('ớ', 'o').Replace('ợ', 'o').Replace('ở', 'o').Replace('ỡ', 'o')
                .Replace('ù', 'u').Replace('ú', 'u').Replace('ụ', 'u').Replace('ủ', 'u').Replace('ũ', 'u')
                .Replace('ư', 'u').Replace('ừ', 'u').Replace('ứ', 'u').Replace('ự', 'u').Replace('ử', 'u').Replace('ữ', 'u')
                .Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỵ', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y')
                .Replace('đ', 'd');

            return text;
        }
        private static void PrepareAlternativeRecommendationIntent(
    ParsedIntent intent,
    CustomerPreferenceProfile? profile)
        {
            if (intent == null)
                return;

            intent.IntentType = "recommend";
            intent.RouteFlow = ChatFlowType.Recommendation;
            intent.IsDirectCompare = false;
            intent.IsFollowUp = true;
            intent.FollowUpType = "alternative";
            intent.HasExpandRecommendationSignal = true;
            intent.HasNarrowRefinementSignal = false;
            intent.ComparisonFeature = "price";
            intent.ExcludePreviousProducts = true;

            intent.MentionedProducts?.Clear();

            if (profile?.LastComparedProducts != null)
            {
                foreach (var productName in profile.LastComparedProducts)
                {
                    if (!string.IsNullOrWhiteSpace(productName))
                        intent.ExcludedProducts.Add(productName.Trim());
                }
            }

            if (profile?.LastRecommendedProducts != null)
            {
                foreach (var productName in profile.LastRecommendedProducts)
                {
                    if (!string.IsNullOrWhiteSpace(productName))
                        intent.ExcludedProducts.Add(productName.Trim());
                }
            }
        }
        private static FlowRoutingResult RouteTo(
     string flowType,
     string reason,
     bool useRag = false,
     bool useAiFallback = false)
        {
            return new FlowRoutingResult
            {
                FlowType = flowType,
                ShouldUseDeterministicFlow = !useAiFallback,
                ShouldUseAiFallback = useAiFallback,
                ShouldUseRag = useRag,
                Reason = reason
            };
        }
        private static bool IsClearBaseRouting(FlowRoutingResult? routing)
        {
            if (routing == null)
                return false;

            if (string.IsNullOrWhiteSpace(routing.FlowType))
                return false;

            return !string.Equals(routing.FlowType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExplicitCompareRequest(string message, ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            var text = NormalizeText(message);

            return intent.IsDirectCompare ||
                   string.Equals(intent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("so sanh") ||
                   text.Contains("so voi") ||
                   text.Contains("khac nhau") ||
                   text.Contains("uu nhuoc diem");
        }

        private static bool HasExclusionConstraint(ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            return intent.ExcludedBrands.Any() ||
                   intent.ExcludedProducts.Any() ||
                   intent.ExcludedCategories.Any();
        }

        private static bool IsFreshRecommendationWithExclusion(ParsedIntent intent)
        {
            if (!HasExclusionConstraint(intent))
                return false;

            return string.Equals(intent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) ||
                   intent.IsOpenRecommendation ||
                   intent.HasFreshConsultationSignal ||
                   (
                       !intent.IsFollowUp &&
                       (
                           !string.IsNullOrWhiteSpace(intent.Brand) ||
                           !string.IsNullOrWhiteSpace(intent.Category) ||
                           !string.IsNullOrWhiteSpace(intent.Target) ||
                           intent.ForSchool ||
                           intent.ForWork ||
                           intent.ForCity ||
                           intent.ForTour ||
                           intent.PriceMin.HasValue ||
                           intent.PriceMax.HasValue ||
                           intent.TargetPrice.HasValue
                       )
                   );
        }

        private static void MarkAsRefinementExclude(ParsedIntent intent)
        {
            intent.IntentType = "refine";
            intent.IsFollowUp = true;
            intent.IsDirectCompare = false;
            intent.FollowUpType ??= "exclude";
        }

        private static void MarkAsFreshRecommendation(ParsedIntent intent)
        {
            intent.IntentType = "recommend";
            intent.IsFollowUp = false;
            intent.FollowUpType = null;
            intent.IsOpenRecommendation = true;
        }

        private static void ClearCompareContext(CustomerPreferenceProfile? profile)
        {
            if (profile == null)
                return;

            profile.HasActiveCompareContext = false;
            profile.LastComparedProducts?.Clear();
            profile.LastComparisonFeature = null;
        }
        private static bool IsProductCategoryQuestion(string message, ParsedIntent? intent)
        {
            var text = NormalizeText(message);

            bool asksCategory =
                text.Contains("co phai") &&
                (
                    text.Contains("xe ga") ||
                    text.Contains("tay ga") ||
                    text.Contains("xe so") ||
                    text.Contains("con tay")
                );

            bool hasProduct =
                intent?.MentionedProducts != null &&
                intent.MentionedProducts.Any(x => !string.IsNullOrWhiteSpace(x));

            bool hasReference =
                text.Contains("xe nay") ||
                text.Contains("mau nay") ||
                text.Contains("con nay") ||
                text.Contains("xe do") ||
                text.Contains("mau do");

            return asksCategory && (hasProduct || hasReference);
        }
        private static bool LooksLikeBareExclusionMessage(ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            bool hasExclusion =
                intent.ExcludedBrands.Any() ||
                intent.ExcludedProducts.Any() ||
                intent.ExcludedCategories.Any();

            if (!hasExclusion)
                return false;

            bool hasNewRecommendationSignal =
                string.Equals(intent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) ||
                intent.IsOpenRecommendation ||
                intent.HasFreshConsultationSignal ||
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue;

            bool hasNamedCompareTargets =
                intent.MentionedProducts != null &&
                intent.MentionedProducts.Count >= 2;

            return !hasNewRecommendationSignal && !hasNamedCompareTargets;
        }
        private static bool LooksLikeCategoryRefinement(string message, ParsedIntent intent)
        {
            var text = NormalizeText(message);

            bool hasCategory =
                !string.IsNullOrWhiteSpace(intent.Category) ||
                text.Contains("xe ga") ||
                text.Contains("tay ga") ||
                text.Contains("xe so") ||
                text.Contains("con tay");

            bool hasNamedProducts =
                intent.MentionedProducts != null &&
                intent.MentionedProducts.Any(x => !string.IsNullOrWhiteSpace(x));

            return hasCategory && !hasNamedProducts;
        }
        private static bool LooksLikePolicyQuestion(string message, ParsedIntent? intent)
        {
            var text = NormalizeText(message);

            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Không cướp flow đơn hàng
            if (ShouldForceOrderLookup(intent))
                return false;

            bool hasPolicyKeyword =
                text.Contains("tra gop") ||
                text.Contains("gop") ||
                text.Contains("bao hanh") ||
                text.Contains("bao duong") ||
                text.Contains("doi tra") ||
                text.Contains("doi xe") ||
                text.Contains("giao hang") ||
                text.Contains("van chuyen") ||
                text.Contains("dat coc") ||
                text.Contains("coc") ||
                text.Contains("giay to") ||
                text.Contains("dang ky xe") ||
                text.Contains("bien so") ||
                text.Contains("mua online") ||
                text.Contains("thanh toan") ||
                text.Contains("chuyen khoan") ||
                text.Contains("lai thu") ||
                text.Contains("thu cu doi moi") ||
                text.Contains("ho so") ||
text.Contains("thu tuc") ||
text.Contains("can gi") ||
text.Contains("di mua xe") ||
text.Contains("khi di mua xe") ||
text.Contains("khi mua xe") ||
                text.Contains("cuu ho") ||
                text.Contains("khuyen mai");

            bool asksInfo =
                text.Contains("nhu nao") ||
                text.Contains("the nao") ||
                text.Contains("ra sao") ||
                text.Contains("bao lau") ||
                text.Contains("may thang") ||
                text.Contains("bao nhieu") ||
                text.Contains("can gi") ||
                text.Contains("thu tuc") ||
                text.Contains("chinh sach") ||
                text.Contains("co khong") ||
                text.Contains("duoc khong");

            bool purchaseDocumentQuestion =
    (text.Contains("giay to") || text.Contains("ho so") || text.Contains("thu tuc") || text.Contains("can gi")) &&
    (text.Contains("mua xe") || text.Contains("di mua xe") || text.Contains("khi mua xe") || text.Contains("lay xe"));

            return purchaseDocumentQuestion || (hasPolicyKeyword && asksInfo);
        }
        private static bool LooksLikePickOneRequest(string message)
        {
            var text = NormalizeText(message);

            return
     text.Contains("chon 1") ||
     text.Contains("chon mot") ||
     text.Contains("chon cho") ||
     text.Contains("chon giup") ||
     text.Contains("chon tot nhat") ||
     text.Contains("chon 1 con") ||
     text.Contains("chon mot con") ||
     text.Contains("chon 1 xe") ||
     text.Contains("chon mot xe") ||
     text.Contains("lay 1") ||
     text.Contains("lay mot") ||
     text.Contains("lay con nao") ||
     text.Contains("chot 1") ||
     text.Contains("chot mot") ||
     text.Contains("chot con") ||
     text.Contains("chot xe") ||
     text.Contains("xe tot nhat trong so") ||
     text.Contains("mau tot nhat trong so") ||
     text.Contains("con tot nhat trong so");
        }
        private static bool ShouldRouteToRagPolicy(ParsedIntent intent, string message)
        {
            if (intent == null)
                return false;

            if (ShouldForceOrderLookup(intent))
                return false;

            bool policyIntent =
                string.Equals(intent.IntentType, "policy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intent.IntentType, "faq", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intent.IntentType, "rag_policy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intent.RouteFlow, ChatFlowType.RagPolicy, StringComparison.OrdinalIgnoreCase);

            if (policyIntent)
                return true;

            // fallback cũ, giữ để bắt các câu parser chưa hiểu
            return LooksLikePolicyQuestion(message, intent);
        }
        private static bool LooksLikeBudgetExpansion(string text)
        {
            text = NormalizeText(text);

            bool hasExpandSignal =
                text.Contains("cao hon") ||
                text.Contains("dat hon") ||
                text.Contains("them chut") ||
                text.Contains("them mot chut") ||
                text.Contains("hon mot chut") ||
                text.Contains("noi ngan sach") ||
                text.Contains("noi them") ||
                text.Contains("tang ngan sach") ||
                text.Contains("len chut") ||
                text.Contains("len mot chut");

            bool hasBudgetWord =
                text.Contains("gia") ||
                text.Contains("ngan sach") ||
                text.Contains("tien") ||
                text.Contains("trieu");

            bool softBudgetFollowUp =
                text.Contains("cung duoc") ||
                text.Contains("cung dc") ||
                text.Contains("cung duoc");

            return hasExpandSignal && (hasBudgetWord || softBudgetFollowUp);
        }
        private static void ResolveOrdinalProductsIfNeeded(
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string message)
        {
            if (intent == null || profile == null)
                return;

            var source =
                profile.LastRecommendedProducts?.Count > 0 ? profile.LastRecommendedProducts :
                profile.CurrentRecommendedProducts?.Count > 0 ? profile.CurrentRecommendedProducts :
                profile.BaseRecommendedProducts?.Count > 0 ? profile.BaseRecommendedProducts :
                new List<string>();

            if (source.Count == 0)
                return;

            var text = NormalizeText(message);
            var resolved = new List<string>();

            void AddIfValid(int index)
            {
                if (index >= 0 && index < source.Count)
                    resolved.Add(source[index]);
            }
            int ToIndex(string token)
            {
                token = NormalizeText(token);
                return token switch
                {
                    "1" or "nhat" => 0,
                    "2" or "hai" => 1,
                    "3" or "ba" => 2,
                    "4" or "tu" => 3,
                    "5" or "nam" => 4,
                    "cuoi" => source.Count - 1, 
                    _ => -1
                };
            }
            if (text.Contains("2 xe dau") ||
    text.Contains("hai xe dau") ||
    text.Contains("2 mau dau") ||
    text.Contains("hai mau dau"))
            {
                AddIfValid(0);
                AddIfValid(1);
            }
            var matches = Regex.Matches(
    text,
    @"\b(?:xe|mau|con)?\s*(?:thu\s*)?(1|2|3|4|5|nhat|hai|ba|tu|nam|cuoi)\b", 
    RegexOptions.IgnoreCase);

            foreach (Match m in matches)
            {
                var index = ToIndex(m.Groups[1].Value);
                AddIfValid(index);
            }

            if (text.Contains("xe dau tien") || text.Contains("mau dau tien") || text.Contains("con dau tien"))
                AddIfValid(0);
            if (text.Contains("xe cuoi") || text.Contains("mau cuoi") || text.Contains("con cuoi")) // Thêm check text thủ công
                AddIfValid(source.Count - 1);

            foreach (var p in resolved.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!intent.MentionedProducts.Contains(p, StringComparer.OrdinalIgnoreCase))
                    intent.MentionedProducts.Add(p);
            }
        }
        private static bool LooksLikeCompareFeatureWithinRecommendation(string message, ParsedIntent intent)
        {
            var text = NormalizeText(message);

            bool asksWhich =
                text.Contains("xe nao") ||
                text.Contains("mau nao") ||
                text.Contains("con nao") ||
                text.Contains("cai nao");

            bool hasFeature =
                text.Contains("tiet kiem xang") ||
                text.Contains("it hao xang") ||
                text.Contains("ben hon") ||
                text.Contains("bền hơn") ||
                text.Contains("de di hon") ||
                text.Contains("dễ đi hơn") ||
                text.Contains("re hon") ||
                text.Contains("rẻ hơn") ||
                text.Contains("tot hon") ||
                text.Contains("tốt hơn") ||
                !string.IsNullOrWhiteSpace(intent.ComparisonFeature);

            if (text.Contains("tiet kiem xang") || text.Contains("it hao xang"))
                intent.ComparisonFeature ??= "fuel_saving";

            if (text.Contains("ben hon") || text.Contains("bền hơn"))
                intent.ComparisonFeature ??= "durability";

            return asksWhich && hasFeature;
        }
        private static bool ExcludedBrandNotInCurrentRecommendation(
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            if (intent?.ExcludedBrands == null || intent.ExcludedBrands.Count == 0 || profile == null)
                return false;

            var currentProducts = new List<string>();

            if (profile.CurrentRecommendedProducts != null)
                currentProducts.AddRange(profile.CurrentRecommendedProducts);

            if (currentProducts.Count == 0 && profile.LastRecommendedProducts != null)
                currentProducts.AddRange(profile.LastRecommendedProducts);

            if (currentProducts.Count == 0 && profile.BaseRecommendedProducts != null)
                currentProducts.AddRange(profile.BaseRecommendedProducts);

            if (currentProducts.Count == 0)
                return false;

            foreach (var excludedBrand in intent.ExcludedBrands)
            {
                if (currentProducts.Any(p =>
                    !string.IsNullOrWhiteSpace(p) &&
                    p.Contains(excludedBrand, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }

            return true;
        }
        private static bool MessageAsksGlobalScope(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("cua shop") ||
                   text.Contains("toan shop") ||
                   text.Contains("toan bo shop") ||
                   text.Contains("tat ca shop") ||
                   text.Contains("cua hang") ||
                   text.Contains("toan cua hang") ||
                   text.Contains("tat ca xe") ||
                   text.Contains("toan bo xe");
        }
        private static bool LooksLikeCheapestInRecommendationList(string text)
        {
            text = NormalizeText(text);

            if (MessageAsksGlobalScope(text))
                return false;

            bool hasCurrentListSignal =
                text.Contains("trong danh sach") ||
                text.Contains("trong nhom") ||
                text.Contains("vua goi y") ||
                text.Contains("ben tren") ||
                text.Contains("may mau vua goi y") ||
                text.Contains("cac mau vua goi y") ||
                text.Contains("trong cac mau");

            bool asksCheapest =
                text.Contains("xe nao re hon") ||
                text.Contains("mau nao re hon") ||
                text.Contains("con nao re hon") ||
                text.Contains("cai nao re hon") ||
                text.Contains("xe nao re nhat") ||
                text.Contains("mau nao re nhat") ||
                text.Contains("re nhat") ||
                text == "re hon" ||
                text == "xe re hon";

            return hasCurrentListSignal && asksCheapest;
        }
        private static bool LooksLikeValueForMoneyQuestion(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("ngon bo re") ||
                   text.Contains("ngon re") ||
                   text.Contains("dang tien") ||
                   text.Contains("dang mua") ||
                   text.Contains("mau nao dang mua") ||
                   text.Contains("hop ly nhat") ||
                   text.Contains("tot trong tam gia") ||
                   text.Contains("xe nao ngon") ||
                   text.Contains("xe nao on");
        }
        private static bool LooksLikeBareCompareCurrentListRequest(string message)
        {
            var text = NormalizeText(message);

            return text == "so sanh" ||
                   text == "so sanh di" ||
                   text == "so sanh tiep" ||
                   text == "compare";
        }
    }
    }
