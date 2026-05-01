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
           
            if (profile?.HasPendingOrderLookup == true)
            {
                result.FlowType = ChatFlowType.OrderLookup;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Pending order lookup is waiting for missing information";
                return result;
            }

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
            if (LooksLikeProductReferenceLookup(text) &&
    (
        !string.IsNullOrWhiteSpace(profile?.LastLookupProductName) ||
        !string.IsNullOrWhiteSpace(profile?.LastResolvedProductName) ||
        profile?.LastMentionedProducts?.Count > 0
    ))
            {
                result.FlowType = ChatFlowType.ProductLookup;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseAiFallback = false;
                result.ShouldUseRag = false;
                result.Reason = "Product reference lookup follow-up";
                return result;
            }
            bool hasActiveRecommendationContext = HasRecommendationContext(profile);
            bool isRefineFollowUp =
    hasActiveRecommendationContext &&
   RecommendationConversationRules.LooksLikeRefineWithinCurrentRecommendation(
    text,
    intent,
    profile);
            if (intent.HasFreshConsultationSignal || intent.IsOpenRecommendation)
            {
                result.FlowType = ChatFlowType.Recommendation;
                result.ShouldUseDeterministicFlow = true;
                result.ShouldUseRag = true;
                result.ShouldUseAiFallback = false;
                result.Reason = "Fresh recommendation overrides old recommendation context";
                return result;
            }
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
        private static bool LooksLikeProductReferenceLookup(string text)
        {
            text = NormalizeText(text);

            bool hasReference =
                text.Contains("xe nay") ||
                text.Contains("mau nay") ||
                text.Contains("con nay") ||
                text.Contains("xe do") ||
                text.Contains("mau do") ||
                text.Contains("con do") ||
                text.Contains("xe kia") ||
                text.Contains("mau kia") ||
                text.Contains("con kia");

            bool asksLookup =
                text.Contains("co phai") ||
                text.Contains("loai xe") ||
                text.Contains("dong xe") ||
                text.Contains("xe gi") ||
                text.Contains("xe ga") ||
                text.Contains("xe so") ||
                text.Contains("con tay") ||
                text.Contains("gia") ||
                text.Contains("con hang") ||
                text.Contains("ton kho") ||
                text.Contains("bao nhieu");

            return hasReference && asksLookup;
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
        private static bool HasRecommendationContext(CustomerPreferenceProfile? profile)
        {
            return profile?.HasActiveRecommendationContext == true &&
                   profile.LastRecommendedProducts.Count > 0;
        }
    }
}