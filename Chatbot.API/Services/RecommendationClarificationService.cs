using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class RecommendationClarificationService : IRecommendationClarificationService
    {
        public bool HasEnoughSignalsForDirectRecommendation(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null)
        {
            var text = (message ?? string.Empty).ToLowerInvariant();

            bool hasBudget =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None ||
                profile?.PriceMin.HasValue == true ||
                profile?.PriceMax.HasValue == true ||
                profile?.TargetPrice.HasValue == true ||
                LooksLikeBudgetFragment(text) ||
                text.Contains("triệu") ||
                text.Contains("triêu") ||
                text.Contains("trieu") ||
                text.Contains("tầm") ||
                text.Contains("khoảng") ||
                text.Contains("quanh");

            bool hasTargetOrUseCase =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(profile?.Target) ||
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                profile?.ForWork == true ||
                profile?.ForSchool == true ||
                profile?.ForCity == true ||
                profile?.ForTour == true ||
                HasExplicitMaleSignal(text) ||
                HasExplicitFemaleSignal(text) ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("đi học") ||
                text.Contains("di hoc") ||
                text.Contains("đi phố") ||
                text.Contains("di pho") ||
                text.Contains("sinh viên");

            bool hasHardConstraint =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredBrand) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredCategory);

            bool hasNeedHint =
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.NeedsLowSeat ||
                profile?.WantsFuelSaving == true ||
                profile?.WantsLargeStorage == true ||
                profile?.WantsEasyControl == true ||
                profile?.NeedsLowSeat == true ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ đi") ||
                text.Contains("de di") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan");

            if (hasBudget && hasTargetOrUseCase)
                return true;

            if (hasBudget && hasHardConstraint)
                return true;

            if (hasBudget && hasNeedHint)
                return true;

            bool looksGeneralRecommendationAsk =
                text.Contains("tư vấn") ||
                text.Contains("tu van") ||
                text.Contains("xe tầm") ||
                text.Contains("xe tam") ||
                text.Contains("xe khoảng") ||
                text.Contains("xe khoang") ||
                text.Contains("gợi ý") ||
                text.Contains("goi y") ||
                text.Contains("mua xe") ||
                Regex.IsMatch(
                    text,
                    @"^\s*xe\s+\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                    RegexOptions.IgnoreCase);

            if (hasBudget && looksGeneralRecommendationAsk)
                return true;

            return false;
        }

        public bool NeedsClarificationForConsultation(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();
            var isConsultation = IsConsultationIntent(message);

            if (!isConsultation)
                return false;

            if (HasEnoughSignalsForDirectRecommendation(message, parsedIntent, profile))
                return false;

            bool hasStrongDirectConsultationSignal =
                (parsedIntent.PriceMin.HasValue ||
                 parsedIntent.PriceMax.HasValue ||
                 parsedIntent.TargetPrice.HasValue ||
                 parsedIntent.FilterType != PriceFilterType.None) &&
                (
                    !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                    parsedIntent.ForWork ||
                    parsedIntent.ForSchool ||
                    parsedIntent.ForCity ||
                    parsedIntent.ForTour ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                    parsedIntent.WantsFuelSaving ||
                    parsedIntent.WantsLargeStorage ||
                    parsedIntent.WantsEasyControl ||
                    parsedIntent.NeedsLowSeat
                );

            if (hasStrongDirectConsultationSignal)
                return false;

            if (parsedIntent.HeightCm.HasValue || profile?.HeightCm.HasValue == true)
                return false;

            if (parsedIntent.NeedsLowSeat || profile?.NeedsLowSeat == true)
                return false;

            bool hasBudget =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                profile?.PriceMin.HasValue == true ||
                profile?.PriceMax.HasValue == true ||
                profile?.TargetPrice.HasValue == true ||
                LooksLikeBudgetFragment(text) ||
                text.Contains("triệu") ||
                text.Contains("triêu") ||
                text.Contains("trieu") ||
                text.Contains("tầm") ||
                text.Contains("khoảng") ||
                text.Contains("quanh");

            bool hasCategory =
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredCategory);

            bool hasBrand =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredBrand);

            bool hasTarget =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(profile?.Target) ||
                text.Contains("đi học") ||
                text.Contains("di hoc") ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("sinh viên") ||
                HasExplicitMaleSignal(text) ||
                HasExplicitFemaleSignal(text);

            bool hasNeedHint =
                text.Contains("tiết kiệm xăng") ||
                text.Contains("cốp rộng") ||
                text.Contains("nhẹ") ||
                text.Contains("dễ đi") ||
                text.Contains("cá tính") ||
                text.Contains("thể thao") ||
                text.Contains("thanh lịch") ||
                text.Contains("đi phố") ||
                text.Contains("di pho") ||
                profile?.WantsEasyControl == true ||
                profile?.WantsFuelSaving == true ||
                profile?.WantsLargeStorage == true ||
                profile?.NeedsLowSeat == true ||
                profile?.HeightCm.HasValue == true;

            if (hasBudget && hasTarget)
                return false;

            bool explicitBudgetAndGender =
                (text.Contains("45 triệu") || text.Contains("40 triệu") || text.Contains("30 triệu") ||
                 text.Contains("khoảng") || text.Contains("tầm") || text.Contains("quanh")) &&
                (HasExplicitMaleSignal(text) || HasExplicitFemaleSignal(text));

            if (explicitBudgetAndGender)
                return false;

            if (hasBudget && (hasNeedHint || hasBrand || hasCategory))
                return false;

            if (hasBudget && (hasTarget || hasCategory || hasBrand || hasNeedHint))
                return false;

            if (hasTarget && hasNeedHint)
                return false;

            if (hasCategory && hasNeedHint)
                return false;

            int knownSignals = 0;
            if (hasBudget) knownSignals++;
            if (hasCategory) knownSignals++;
            if (hasBrand) knownSignals++;
            if (hasTarget) knownSignals++;
            if (hasNeedHint) knownSignals++;

            if (hasNeedHint && (text.Contains("đi phố") || text.Contains("di pho") || text.Contains("cá tính") || text.Contains("ca tinh")))
                return false;

            bool allowSoftRecommendationWithoutBudget = hasTarget || hasNeedHint;

            if (allowSoftRecommendationWithoutBudget)
                return false;

            return knownSignals <= 1;
        }

        public string BuildClarificationQuestion(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null)
        {
            var text = message.ToLowerInvariant();

            bool hasGender =
                HasExplicitFemaleSignal(text) ||
                HasExplicitMaleSignal(text) ||
                HasExplicitFemaleSignal(parsedIntent.Target) ||
                HasExplicitMaleSignal(parsedIntent.Target) ||
                HasExplicitFemaleSignal(profile?.Target) ||
                HasExplicitMaleSignal(profile?.Target);

            bool hasBudget =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                profile?.PriceMin.HasValue == true ||
                profile?.PriceMax.HasValue == true ||
                profile?.TargetPrice.HasValue == true ||
                LooksLikeBudgetFragment(text) ||
                text.Contains("triệu") ||
                text.Contains("triêu") ||
                text.Contains("trieu") ||
                text.Contains("tầm") ||
                text.Contains("khoảng") ||
                text.Contains("quanh");

            bool hasCategory =
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredCategory);

            if (hasGender && !hasBudget)
            {
                return "Mình có thể gợi ý sơ bộ theo nhu cầu này. Bạn muốn mình ưu tiên tầm giá nào để lọc sát hơn?";
            }

            if (hasBudget && hasGender)
            {
                return "Mình đã có ngân sách và đối tượng rồi, mình sẽ gợi ý sơ bộ trước. Nếu muốn lọc sâu hơn nữa thì bạn có thể nói thêm loại xe hoặc nhu cầu như đi làm, đi học, cốp rộng.";
            }

            if (hasBudget && !hasCategory)
            {
                return "Mình có thể gợi ý sơ bộ trước theo ngân sách này. Nếu muốn lọc sát hơn nữa thì bạn nói thêm loại xe như xe ga, xe số hoặc côn tay nhé.";
            }

            if (!hasBudget && !hasCategory)
            {
                return "Bạn cho mình thêm 1 ý là ngân sách hoặc loại xe bạn thích, mình sẽ gợi ý sát hơn nhé.";
            }

            return "Bạn nói thêm giúp mình 1 chi tiết quan trọng nhất như ngân sách, loại xe hoặc nhu cầu sử dụng để mình lọc chính xác hơn nhé.";
        }

        private static bool HasExplicitFemaleSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = text.Trim().ToLowerInvariant();

            return
                Regex.IsMatch(value, @"(^|\s)(nữ|nu)(\s|$)", RegexOptions.IgnoreCase) ||
                value.Contains("cho nữ") ||
                value.Contains("cho nu") ||
                value.Contains("xe nữ") ||
                value.Contains("xe nu") ||
                value.Contains("hợp nữ") ||
                value.Contains("hop nu") ||
                value.Contains("nữ tính") ||
                value.Contains("nu tinh") ||
                value.Contains("phù hợp cho nữ") ||
                value.Contains("phu hop cho nu");
        }

        private static bool HasExplicitMaleSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = text.Trim().ToLowerInvariant();

            return
                Regex.IsMatch(value, @"(^|\s)nam(\s|$)", RegexOptions.IgnoreCase) ||
                value.Contains("cho nam") ||
                value.Contains("xe nam") ||
                value.Contains("hợp nam") ||
                value.Contains("hop nam") ||
                value.Contains("phù hợp cho nam") ||
                value.Contains("phu hop cho nam") ||
                value.Contains("nam tính") ||
                value.Contains("manly");
        }

        private static bool LooksLikeBudgetFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(
                       text,
                       @"\b(tầm|khoảng|quanh)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"^\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"\b(từ|tu)\s*\d+([.,]\d+)?\s*(đến|den)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"^\d+([.,]\d+)?\s*[-~]\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"\b(dưới|duoi|trên|tren|tối đa|toi da|không quá|khong qua|ít nhất|it nhat|trở lên|tro len)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase);
        }

        public bool IsConsultationIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            return text.Contains("tư vấn")
                || text.Contains("phù hợp")
                || text.Contains("nên mua")
                || text.Contains("gợi ý")
                || text.Contains("xe nào")
                || text.Contains("mua xe nào")
                || text.Contains("đi học nên mua")
                || text.Contains("đi làm nên mua")
                || text.Contains("xe nào rẻ")
                || text.Contains("tu van")
                || text.Contains("phu hop")
                || text.Contains("di lam")
                || text.Contains("di hoc")
                || text.Contains("tiet kiem xang")
                || text.Contains("cop rong")
                || text.Contains("de chong chan")
                || text.Contains("cho nữ")
                || text.Contains("cho nam")
                || text.Contains("sinh viên")
                || text.Contains("đi học")
                || text.Contains("đi làm")
                || text.Contains("đi phố")
                || text.Contains("tiết kiệm xăng")
                || text.Contains("cốp rộng")
                || text.Contains("nhẹ")
                || text.Contains("dễ đi")
                || text.Contains("cá tính")
                || text.Contains("thể thao")
                || text.Contains("thanh lịch")
                || text.Contains("xe ga")
                || text.Contains("xe số")
                || text.Contains("côn tay")
                || text.Contains("thanh lich")
                || text.Contains("xe dưới")
                || text.Contains("xe duoi")
                || text.Contains("xe trên")
                || text.Contains("xe tren")
                || text.Contains("đổi ý")
                || text.Contains("doi y")
                || text.Contains("h t muốn")
                || text.Contains("gio t muon")
                || text.Contains("giờ t muốn")
                || text.Contains("nu tinh")
                || text.Contains("mem mai");
        }
    }


}