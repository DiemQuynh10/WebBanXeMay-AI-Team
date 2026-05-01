using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using static Chatbot.API.Models.Intent.ParsedIntent;

namespace Chatbot.API.Helpers
{
    public static class RecommendationConstraintExtractor
    {
        private static readonly string[] KnownBrands =
        {
            "Honda", "Yamaha", "Suzuki", "SYM", "Piaggio"
        };

        public static void Apply(string normalizedText, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(normalizedText))
                return;

            ExtractExcludedBrands(normalizedText, intent);
            ExtractChangeProductSignal(normalizedText, intent);
            ExtractFreshRecommendationSignal(normalizedText, intent);
        }
        private static void ExtractExcludedBrands(string text, ParsedIntent intent)
        {
            foreach (var brand in KnownBrands)
            {
                var b = Normalize(brand);

                var patterns = new[]
                {
            // mạnh nhất
            $@"\b(khong\s+{b})\b",

            // các dạng đầy đủ
            $@"\b(mien khong la|mien khong|tru|ngoai tru|khong lay|khong chon|khong muon|khong thich|khong phai|khong la|ne|bo|loai)\s+{b}\b",

            // dạng đảo
            $@"\b{b}\b.*\b(khong thich|khong muon|khong lay|khong chon|thi khong|thi bo|loai ra)\b"
        };

                if (patterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase)))
                {
                    intent.ExcludedBrands.Add(brand);

                    if (string.Equals(intent.Brand, brand, StringComparison.OrdinalIgnoreCase))
                        intent.Brand = null;
                }
            }
        }
        private static void ExtractChangeProductSignal(string text, ParsedIntent intent)
        {
            var isChangeProduct =
                Regex.IsMatch(text, @"\b(doi mau khac|doi con khac|doi xe khac)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\b(mau khac|con khac|xe khac)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\b(khac xem|khac di|xem mau khac|xem con khac|goi y mau khac|goi y con khac)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(text, @"\b(con mau nao khac|con con nao khac|con lua chon nao khac|lua chon khac)\b", RegexOptions.IgnoreCase);

            if (!isChangeProduct)
                return;

            intent.IntentType = "refine";
            intent.IsFollowUp = true;
            intent.FollowUpType = "change_product";

            intent.Action = ConversationAction.ChangeProduct;
            intent.KeepConstraints = true;
            intent.ExcludePreviousProducts = true;
            intent.ExcludePreviousBrands = false;

            intent.IsOpenRecommendation = false;
            intent.HasFreshConsultationSignal = false;
            intent.HasDeterministicProductIntent = true;
            intent.RouteFlow = ChatFlowType.Refinement;
        }
        private static void ExtractFreshRecommendationSignal(string text, ParsedIntent intent)
        {
            if (intent.Action == ConversationAction.ChangeProduct ||
       string.Equals(intent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var hasConsultWord =
                Regex.IsMatch(text, @"\b(tu van|goi y|nen mua|muon mua|can mua|tim xe|chon xe)\b", RegexOptions.IgnoreCase);

            var hasUserProfile =
                Regex.IsMatch(text, @"\b(sinh vien|hoc sinh|di hoc|di lam|di pho|cho nu|cho nam|nu|nam)\b", RegexOptions.IgnoreCase);

            var hasVehicleConstraint =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                intent.ExcludedBrands.Any() ||
                intent.ExcludedCategories.Any() ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue;

            if ((hasConsultWord || hasUserProfile) && hasVehicleConstraint)
            {
                intent.IntentType = "recommend";
                intent.IsOpenRecommendation = true;
                intent.IsFollowUp = false;
                intent.FollowUpType = null;
                intent.HasFreshConsultationSignal = true;
                intent.HasDeterministicProductIntent = false;
                intent.RouteFlow = ChatFlowType.Recommendation;
            }
        }

        private static string Normalize(string text)
        {
            return text.ToLowerInvariant()
                .Replace("á", "a").Replace("à", "a").Replace("ả", "a").Replace("ã", "a").Replace("ạ", "a")
                .Replace("ă", "a").Replace("ắ", "a").Replace("ằ", "a").Replace("ẳ", "a").Replace("ẵ", "a").Replace("ặ", "a")
                .Replace("â", "a").Replace("ấ", "a").Replace("ầ", "a").Replace("ẩ", "a").Replace("ẫ", "a").Replace("ậ", "a")
                .Replace("é", "e").Replace("è", "e").Replace("ẻ", "e").Replace("ẽ", "e").Replace("ẹ", "e")
                .Replace("ê", "e").Replace("ế", "e").Replace("ề", "e").Replace("ể", "e").Replace("ễ", "e").Replace("ệ", "e")
                .Replace("í", "i").Replace("ì", "i").Replace("ỉ", "i").Replace("ĩ", "i").Replace("ị", "i")
                .Replace("ó", "o").Replace("ò", "o").Replace("ỏ", "o").Replace("õ", "o").Replace("ọ", "o")
                .Replace("ô", "o").Replace("ố", "o").Replace("ồ", "o").Replace("ổ", "o").Replace("ỗ", "o").Replace("ộ", "o")
                .Replace("ơ", "o").Replace("ớ", "o").Replace("ờ", "o").Replace("ở", "o").Replace("ỡ", "o").Replace("ợ", "o")
                .Replace("ú", "u").Replace("ù", "u").Replace("ủ", "u").Replace("ũ", "u").Replace("ụ", "u")
                .Replace("ư", "u").Replace("ứ", "u").Replace("ừ", "u").Replace("ử", "u").Replace("ữ", "u").Replace("ự", "u")
                .Replace("ý", "y").Replace("ỳ", "y").Replace("ỷ", "y").Replace("ỹ", "y").Replace("ỵ", "y")
                .Replace("đ", "d");
        }
    }
}