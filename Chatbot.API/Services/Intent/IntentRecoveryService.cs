using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services.Intent
{
    public class IntentRecoveryService : IIntentRecoveryService
    {
        public void Recover(
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            if (intent == null || string.IsNullOrWhiteSpace(normalizedMessage))
                return;

            RecoverExplicitRecommendationIntent(normalizedMessage, intent);
            RecoverOrdinalCompareIntent(normalizedMessage, intent, profile);
            RecoverCompareCurrentRecommendationIntent(normalizedMessage, intent, profile);
        }

        private static void RecoverExplicitRecommendationIntent(string message, ParsedIntent intent)
        {
            var text = NormalizeText(message);

            bool hasRecommendSignal =
                text.Contains("tu van") ||
                text.Contains("goi y") ||
                text.Contains("nen mua") ||
                text.Contains("chon xe") ||
                text.StartsWith("xe ");

            bool hasVehicleSignal =
                text.Contains("xe ga") ||
                text.Contains("xe so") ||
                text.Contains("xe con") ||
                text.Contains("xe tay ga");

            bool hasBrandSignal =
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio");

            bool hasTargetSignal =
                text.Contains("cho nu") ||
                text.Contains("cho nam") ||
                text.Contains("sinh vien") ||
                text.Contains("hoc sinh");

            bool hasPriceSignal =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                text.Contains("trieu") ||
                text.Contains("cu") ||
                text.Contains("khoang") ||
                text.Contains("tam");
            bool hasProductSignal =
    intent.MentionedProducts != null &&
    intent.MentionedProducts.Count > 0;
            if (!hasRecommendSignal)
                return;

            if (!(hasVehicleSignal || hasBrandSignal || hasTargetSignal || hasPriceSignal || hasProductSignal))
                return;
            if (hasRecommendSignal &&
    hasProductSignal &&
    intent.MentionedProducts.Count == 1 &&
    !hasPriceSignal &&
    !hasTargetSignal &&
    !hasVehicleSignal)
            {
                intent.IntentType = "product_lookup";
                intent.RouteFlow = ChatFlowType.ProductLookup;
                intent.IsDirectProductLookup = true;
                intent.LookupTargetType = "product";
                intent.LookupField = "detail";

                intent.IsOpenRecommendation = false;
                intent.IsFollowUp = false;
                intent.FollowUpType = null;

                intent.IsOutOfScope = false;
                intent.IsNoise = false;
                intent.IsAck = false;

                intent.HasFreshConsultationSignal = false;
                intent.HasNarrowRefinementSignal = false;
                intent.HasDeterministicProductIntent = true;

                return;
            }
            intent.IntentType = "recommend";
            intent.RouteFlow = ChatFlowType.Recommendation;
            intent.IsOpenRecommendation = true;
            intent.IsFollowUp = false;
            intent.FollowUpType = null;

            intent.IsOutOfScope = false;
            intent.IsNoise = false;
            intent.IsAck = false;

            intent.IsDirectCompare = false;
            intent.IsDirectProductLookup = false;
            intent.LookupField = null;

            intent.HasFreshConsultationSignal = true;
            intent.HasNarrowRefinementSignal = false;
            if (hasProductSignal)
            {
                intent.HasDeterministicProductIntent = true;
            }
            if (string.IsNullOrWhiteSpace(intent.Category))
            {
                if (text.Contains("xe ga") || text.Contains("xe tay ga"))
                    intent.Category = "xe ga";
                else if (text.Contains("xe so"))
                    intent.Category = "xe số";
                else if (text.Contains("xe con"))
                    intent.Category = "côn tay";
            }

            if (string.IsNullOrWhiteSpace(intent.Brand))
            {
                if (text.Contains("honda")) intent.Brand = "Honda";
                else if (text.Contains("yamaha")) intent.Brand = "Yamaha";
                else if (text.Contains("suzuki")) intent.Brand = "Suzuki";
                else if (text.Contains("sym")) intent.Brand = "SYM";
                else if (text.Contains("piaggio")) intent.Brand = "Piaggio";
            }

            if (text.Contains("cho nu"))
            {
                intent.Target = "nữ";
                intent.PrefersFemaleStyle = true;
                intent.WantsEasyControl = true;
                intent.NeedsLowSeat = true;
            }

            if (text.Contains("sinh vien") || text.Contains("hoc sinh"))
            {
                intent.Target = "sinh viên";
                intent.ForSchool = true;
                intent.WantsFuelSaving = true;
            }
        }

        private static void RecoverOrdinalCompareIntent(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            var text = NormalizeText(message);

            bool asksFirstTwo =
                text.Contains("so sanh 2 xe dau") ||
                text.Contains("so sanh hai xe dau") ||
                text.Contains("so sanh 2 mau dau") ||
                text.Contains("so sanh hai mau dau");

            if (!asksFirstTwo)
                return;

            bool hasRecommendationList =
                profile?.BaseRecommendedProducts?.Count >= 2 ||
                profile?.LastRecommendedProducts?.Count >= 2 ||
                profile?.CurrentRecommendedProducts?.Count >= 2 ||
                profile?.LastMentionedProducts?.Count >= 2;

            if (!hasRecommendationList)
                return;

            intent.IntentType = "compare";
            intent.RouteFlow = ChatFlowType.Compare;
            intent.IsDirectCompare = true;
            intent.IsFollowUp = true;
            intent.FollowUpType = "compare";
            intent.HasDeterministicProductIntent = true;

            intent.IsOutOfScope = false;
            intent.IsNoise = false;
            intent.IsAck = false;
        }

        private static void RecoverCompareCurrentRecommendationIntent(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile)
        {
            var text = NormalizeText(message);

            bool isBareCompare =
                text == "so sanh" ||
                text == "so sanh di" ||
                text == "so sanh tiep";

            if (!isBareCompare)
                return;

            bool hasRecommendationList =
                profile?.CurrentRecommendedProducts?.Count >= 2 ||
                profile?.LastRecommendedProducts?.Count >= 2 ||
                profile?.BaseRecommendedProducts?.Count >= 2;

            if (!hasRecommendationList)
                return;

            intent.IntentType = "compare";
            intent.RouteFlow = ChatFlowType.Compare;
            intent.IsDirectCompare = true;
            intent.IsFollowUp = true;
            intent.FollowUpType = "compare";
            intent.IsOpenRecommendation = false;

            intent.ExcludedBrands.Clear();
            intent.ExcludedProducts.Clear();
            intent.ExcludedCategories.Clear();

            intent.IsOutOfScope = false;
            intent.IsNoise = false;
            intent.IsAck = false;
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
    }
}