using Chatbot.API.Models.Intent;

namespace Chatbot.API.Helpers
{
    public static class FlowIntentHeuristics
    {
        public static bool IsStaticKnowledgeQuestion(string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] knowledgeKeywords =
            {
                "trả góp", "tra gop", "0%", "0 %", "lãi suất", "lai suat", "hồ sơ", "ho so",
                "thủ tục", "thu tuc", "điều kiện", "dieu kien", "duyệt vay", "duyet vay",
                "bảo dưỡng", "bao duong", "bảo hành", "bao hanh", "dịch vụ", "dich vu",
                "quy trình", "quy trinh", "các bước", "cac buoc", "chính sách", "chinh sach",
                "giao hàng", "giao hang", "vận chuyển", "van chuyen", "nội thành", "noi thanh",
                "ngoại thành", "ngoai thanh", "miễn phí", "mien phi", "phong thủy", "phong thuy",
                "mệnh", "menh", "màu xe", "mau xe", "hợp màu", "hop mau", "kì hạn", "ki han", "kỳ hạn", "ky han",
                "giá lăn bánh", "gia lan banh", "bao giấy", "bao giay", "giá niêm yết", "gia niem yet",
                "phí biển số", "phi bien so", "phí cấp biển", "phi cap bien", "lệ phí trước bạ", "le phi truoc ba",
                "đặt cọc", "dat coc", "hoàn cọc", "hoan coc", "đổi trả", "doi tra", "khấu hao", "khau hao",
                "bảo hiểm", "bao hiem", "tnds", "mất cắp", "mat cap", "khuyến mãi", "khuyen mai", "sinh viên", "sinh vien",
                "thu cũ", "thu cu", "đổi mới", "doi moi", "trade-in", "trợ giá", "tro gia",
                "thanh toán", "thanh toan", "quẹt thẻ", "quet the", "mua xe online", "online",
                "lái thử", "lai thu", "test ride", "my honda", "y-connect", "ứng dụng", "ung dung",
                "cứu hộ", "cuu ho", "thủng lốp", "thung lop", "chết máy", "chet may", "hết xăng", "het xang",
                "hao xăng", "hao xang", "còn hàng", "con hang", "giao ngay"
            };

            bool hasKnowledgeKeyword = knowledgeKeywords.Any(keyword =>
                text.Contains(keyword, StringComparison.Ordinal));

            if (!hasKnowledgeKeyword)
                return false;

            bool looksLikeVehicleRecommendationRequest =
                text.Contains("tư vấn xe", StringComparison.Ordinal) ||
                text.Contains("tu van xe", StringComparison.Ordinal) ||
                text.Contains("gợi ý xe", StringComparison.Ordinal) ||
                text.Contains("goi y xe", StringComparison.Ordinal) ||
                text.Contains("nên mua xe", StringComparison.Ordinal) ||
                text.Contains("nen mua xe", StringComparison.Ordinal);

            return !looksLikeVehicleRecommendationRequest;
        }

        public static bool IsHardFilterOnlySearch(ParsedIntent intent, string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool hasHardFilter =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                intent.ExcludedBrands.Count > 0 ||
                intent.ExcludedCategories.Count > 0 ||
                intent.ExcludedProducts.Count > 0;

            bool hasSoftNeed =
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.ForWork ||
                intent.ForSchool ||
                intent.ForCity ||
                intent.ForTour ||
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                intent.RequestedStyles.Count > 0;

            bool hasRecommendationCue =
                text.Contains("tư vấn") ||
                text.Contains("tu van") ||
                text.Contains("gợi ý") ||
                text.Contains("goi y") ||
                text.Contains("phù hợp") ||
                text.Contains("phu hop") ||
                text.Contains("nên mua") ||
                text.Contains("nen mua");

            return hasHardFilter && !hasSoftNeed && !hasRecommendationCue;
        }
    }
}
