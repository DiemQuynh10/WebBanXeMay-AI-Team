using Chatbot.API.Models.Intent;
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

            if (intent.IntentType == "followup")
            {
                if (profile?.HasActiveCompareContext == true &&
                    profile.LastComparedProducts.Count >= 2)
                {
                    result.FlowType = ChatFlowType.Compare;
                    result.ShouldUseDeterministicFlow = true;
                    result.ShouldUseAiFallback = false;
                    result.Reason = "Compare follow-up detected";
                    return result;
                }

                bool asksAlternativeChoice =
        (intent.RawMessage ?? string.Empty).Contains("loại khác", StringComparison.OrdinalIgnoreCase) ||
        (intent.RawMessage ?? string.Empty).Contains("loai khac", StringComparison.OrdinalIgnoreCase) ||
        (intent.RawMessage ?? string.Empty).Contains("xe khác", StringComparison.OrdinalIgnoreCase) ||
        (intent.RawMessage ?? string.Empty).Contains("xe khac", StringComparison.OrdinalIgnoreCase) ||
        (intent.RawMessage ?? string.Empty).Contains("mẫu khác", StringComparison.OrdinalIgnoreCase) ||
        (intent.RawMessage ?? string.Empty).Contains("mau khac", StringComparison.OrdinalIgnoreCase);

                bool hasHardRefinementSignals =
                    intent.PriceMin.HasValue ||
                    intent.PriceMax.HasValue ||
                    intent.TargetPrice.HasValue ||
                    !string.IsNullOrWhiteSpace(intent.Brand) ||
                    !string.IsNullOrWhiteSpace(intent.Category) ||
                    intent.ExcludedBrands.Any() ||
                    intent.ExcludedCategories.Any() ||
                    intent.WantsLargeStorage ||
                    intent.WantsFuelSaving ||
                    intent.NeedsLowSeat ||
                    intent.WantsEasyControl ||
                    asksAlternativeChoice;

                if (profile?.HasActiveRecommendationContext == true &&
                    profile.LastRecommendedProducts.Count > 0 &&
                    hasHardRefinementSignals)
                {
                    result.FlowType = ChatFlowType.Refinement;
                    result.ShouldUseDeterministicFlow = true;
                    result.ShouldUseAiFallback = false;
                    result.Reason = "Recommendation follow-up with hard refinement signals";
                    return result;
                }

                if (profile?.HasActiveRecommendationContext == true &&
                    profile.LastRecommendedProducts.Count > 0)
                {
                    result.FlowType = ChatFlowType.RecommendationFollowUp;
                    result.ShouldUseDeterministicFlow = true;
                    result.ShouldUseAiFallback = false;
                    result.Reason = "Recommendation follow-up detected";
                    return result;
                }
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
                result.Reason = "Refinement detected";
                return result;
            }

            if (intent.IntentType == "refine" &&
                profile?.HasActiveCompareContext == true &&
                profile.LastComparedProducts.Count >= 2 &&
                !string.IsNullOrWhiteSpace(intent.ComparisonFeature))
            {
                result.FlowType = ChatFlowType.Compare;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Compare context overrides refine";
                return result;
            }

            if (intent.IsBrandSwitch)
            {
                result.FlowType = ChatFlowType.BrandSwitch;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Brand switch detected";
                return result;
            }

            if (intent.IntentType == "refine" &&
                profile?.HasActiveRecommendationContext == true)
            {
                result.FlowType = ChatFlowType.Refinement;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Refinement detected";
                return result;
            }
            if (profile?.HasActiveRecommendationContext == true &&
    profile.LastRecommendedProducts.Count > 0 &&
    (intent.PriceMin.HasValue ||
     intent.PriceMax.HasValue ||
     intent.TargetPrice.HasValue ||
     !string.IsNullOrWhiteSpace(intent.Brand) ||
     !string.IsNullOrWhiteSpace(intent.Category) ||
     intent.WantsLargeStorage ||
     intent.WantsFuelSaving ||
     intent.NeedsLowSeat ||
     intent.WantsEasyControl))
            {
                result.FlowType = ChatFlowType.Refinement;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Recommendation context + filter fragment detected";
                return result;
            }
            bool hasRefinementSignals =
    intent.PriceMin.HasValue ||
    intent.PriceMax.HasValue ||
    intent.TargetPrice.HasValue ||
    !string.IsNullOrWhiteSpace(intent.Brand) ||
    !string.IsNullOrWhiteSpace(intent.Category) ||
    !string.IsNullOrWhiteSpace(intent.ComparisonFeature) ||
    intent.WantsLargeStorage ||
    intent.WantsFuelSaving ||
    intent.NeedsLowSeat ||
    intent.WantsEasyControl ||
    intent.ExcludedBrands.Any() ||
    intent.ExcludedCategories.Any();

            if (profile?.HasActiveRecommendationContext == true &&
                profile.LastRecommendedProducts.Count > 0 &&
                hasRefinementSignals &&
                intent.IntentType != "recommend" &&
                !intent.IsDirectProductLookup &&
                !intent.IsProductSearch)
            {
                result.FlowType = ChatFlowType.Refinement;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Recommendation context + refinement signals detected";
                return result;
            }

            bool isPriceOnlySearch =
     intent.PriceMin.HasValue ||
     intent.PriceMax.HasValue ||
     intent.TargetPrice.HasValue;

            if (intent.IsProductSearch || isPriceOnlySearch)
            {
                result.FlowType = ChatFlowType.ProductSearch;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.Reason = intent.IsProductSearch
                    ? "Product search detected"
                    : "Price-only product search detected";
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