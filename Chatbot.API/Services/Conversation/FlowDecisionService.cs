using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
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
            bool hasLookupContext = HasLookupContext(conversationProfile);

            if (ShouldForceOrderLookup(effectiveIntent))
            {
                routing.FlowType = ChatFlowType.OrderLookup;
                routing.ShouldUseDeterministicFlow = true;
                routing.ShouldUseAiFallback = false;
                routing.ShouldUseRag = false;
                routing.Reason = "Forced by explicit order lookup intent";
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
            if (ShouldForceSearchFromIntent(effectiveIntent))
            {
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
                text.Contains("lua chon khac");

            bool asksCheaper =
                text.Contains("re hon") ||
                text.Contains("mem hon") ||
                text.Contains("gia thap hon") ||
                text.Contains("thap hon") ||
                text.Contains("it tien hon");

            return asksOtherOption && asksCheaper;
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
    }
}
