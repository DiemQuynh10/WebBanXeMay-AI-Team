using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class RecommendationClarificationService : IRecommendationClarificationService
    {
        private static readonly string[] ConsultationPhrases =
        {
            "tư vấn", "tu van", "gợi ý", "goi y", "phù hợp", "phu hop",
            "nên mua", "mua xe nào", "xe nào", "mẫu nào", "con nào",
            "đi làm", "di lam", "đi học", "di hoc", "đi phố", "di pho",
            "tiết kiệm xăng", "tiet kiem xang", "cốp rộng", "cop rong",
            "dễ đi", "de di", "dễ chống chân", "de chong chan",
            "xe ga", "xe số", "côn tay", "con tay", "sinh viên",
            "thể thao", "the thao", "thanh lịch", "thanh lich",
            "nam tính", "nu tinh", "nữ tính", "mềm mại", "mem mai"
        };

        public bool HasEnoughSignalsForDirectRecommendation(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            var text = Normalize(message);
            var signals = BuildSignals(text, parsedIntent, profile);

            if (!signals.IsConsultationIntent)
                return false;

            if (signals.HasBudget)
                return true;

            if (signals.HasTargetOrUseCase ||
                signals.HasCategory ||
                signals.HasBrand ||
                signals.HasNeedHint ||
                signals.HasLowSeatOrHeightInfo)
            {
                return true;
            }

            if ((signals.HasAlternative || signals.HasDecisionFollowUp) &&
                profile?.HasActiveRecommendationContext == true)
            {
                return true;
            }

            return false;
        }

        public bool NeedsClarificationForConsultation(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = Normalize(message);
            var signals = BuildSignals(text, parsedIntent, profile);

            if (!signals.IsConsultationIntent)
                return false;

            if (HasEnoughSignalsForDirectRecommendation(message, parsedIntent, profile))
                return false;
            return IsVeryVagueConsultation(text);
        }
        private static bool IsVeryVagueConsultation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = Normalize(text);

            return text == "tu van" ||
                   text == "tu van xe" ||
                   text == "goi y" ||
                   text == "goi y xe" ||
                   text == "nen mua xe nao" ||
                   text == "mua xe nao" ||
                   text == "chon xe nao" ||
                   text == "xe nao on" ||
                   text == "mau nao on";
        }
        public string BuildClarificationQuestion(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null)
        {
            var text = Normalize(message);
            var signals = BuildSignals(text, parsedIntent, profile);
            if (parsedIntent.ExcludedBrands?.Any() == true &&
    !signals.HasBudget &&
    !signals.HasCategory &&
    !signals.HasTargetOrUseCase &&
    !signals.HasNeedHint)
            {
                var excludedBrands = string.Join(", ", parsedIntent.ExcludedBrands);

                return $"Mình hiểu là bạn không muốn chọn {excludedBrands}. Bạn muốn mình tư vấn theo tầm giá nào, hoặc ưu tiên xe ga/xe số/côn tay?";
            }

            if (signals.HasGender && !signals.HasBudget)
            {
                return "Mình có thể gợi ý sơ bộ theo nhóm nhu cầu này. Bạn muốn mình ưu tiên tầm giá khoảng bao nhiêu để lọc sát hơn?";
            }

            if (signals.HasBudget && !signals.HasCategory && !signals.HasBrand && !signals.HasNeedHint && !signals.HasTargetOrUseCase)
            {
                return "Mình có ngân sách rồi. Bạn muốn mình ưu tiên kiểu nào hơn: xe ga, xe số, côn tay, hay nhu cầu như đi làm, đi học, cốp rộng?";
            }

            if (!signals.HasBudget && (signals.HasCategory || signals.HasBrand || signals.HasNeedHint || signals.HasTargetOrUseCase))
            {
                return "Mình hiểu hướng bạn đang muốn rồi. Bạn cho mình thêm mốc ngân sách để mình lọc sát và gợi ý chuẩn hơn nhé.";
            }

            if (signals.HasBudget && signals.HasTargetOrUseCase)
            {
                return "Mình đã có khung chính rồi, mình có thể gợi ý sơ bộ luôn. Nếu muốn lọc sát hơn nữa thì bạn nói thêm hãng, loại xe hoặc ưu tiên như tiết kiệm xăng, cốp rộng, dễ chống chân nhé.";
            }

            if (signals.HasBudget && (signals.HasBrand || signals.HasCategory || signals.HasNeedHint))
            {
                return "Mình đã có vài tín hiệu khá rõ rồi. Nếu muốn mình lọc sát hơn nữa thì bạn nói thêm đối tượng hoặc nhu cầu như đi làm, đi học, nam, nữ, thấp người nhé.";
            }

            return "Bạn đang muốn xe ga, xe số hay xe côn tay? Nếu chưa chắc, bạn nói khoảng ngân sách dự định cũng được, mình sẽ gợi ý dễ hơn.";
        }

        public bool IsConsultationIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = Normalize(message);

            if (LooksLikeBudgetFragment(text))
                return true;

            if (Regex.IsMatch(text, @"\b(xe|mẫu|con)\b", RegexOptions.IgnoreCase) &&
                (HasExplicitMaleSignal(text) || HasExplicitFemaleSignal(text)))
                return true;

            return ConsultationPhrases.Any(p => text.Contains(p));
        }

        private static RecommendationSignals BuildSignals(
            string text,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            bool hasBudget =
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None ||
                profile?.PriceMin.HasValue == true ||
                profile?.PriceMax.HasValue == true ||
                profile?.TargetPrice.HasValue == true ||
                LooksLikeBudgetFragment(text);

            bool hasTarget =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(profile?.Target) ||
                HasExplicitMaleSignal(text) ||
                HasExplicitFemaleSignal(text);

            bool hasUseCase =
                parsedIntent.ForWork || parsedIntent.ForSchool || parsedIntent.ForCity || parsedIntent.ForTour ||
                profile?.ForWork == true || profile?.ForSchool == true || profile?.ForCity == true || profile?.ForTour == true ||
                HasUseCaseSignal(text);

            bool hasBrand =
    !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
    !string.IsNullOrWhiteSpace(profile?.PreferredBrand) ||
    HasBrandSwitchSignal(text);

            bool hasCategory =
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredCategory) ||
                HasCategorySignal(text);

            bool hasDecisionFollowUp =
    HasDecisionFollowUpSignal(text) ||
    text.Contains("xe nao") ||
    text.Contains("mau nao") ||
    text.Contains("con nao");

            bool hasNeedHint =
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.NeedsLowSeat ||
                profile?.WantsFuelSaving == true ||
                profile?.WantsLargeStorage == true ||
                profile?.WantsEasyControl == true ||
                profile?.NeedsLowSeat == true ||
                parsedIntent.PrefersMaleStyle ||
                parsedIntent.PrefersFemaleStyle ||
                HasNeedHintSignal(text);
            bool hasAlternative =
    HasAlternativeSignal(text) ||
    string.Equals(parsedIntent.ComparisonFeature, "alternative", StringComparison.OrdinalIgnoreCase);
            bool hasLowSeatOrHeightInfo =
                parsedIntent.HeightCm.HasValue ||
                profile?.HeightCm.HasValue == true ||
                parsedIntent.NeedsLowSeat ||
                profile?.NeedsLowSeat == true;

            bool isConsultationIntent =
    ConsultationPhrases.Any(p => text.Contains(p)) ||
    LooksLikeBudgetFragment(text) ||
    hasNeedHint ||
    hasUseCase ||
    hasCategory ||
    hasTarget ||
    hasAlternative ||
    hasDecisionFollowUp ||
    HasBrandSwitchSignal(text);

            int knownSignalCount = 0;
            if (hasBudget) knownSignalCount++;
            if (hasTarget || hasUseCase) knownSignalCount++;
            if (hasBrand) knownSignalCount++;
            if (hasCategory) knownSignalCount++;
            if (hasNeedHint) knownSignalCount++;
            if (hasAlternative) knownSignalCount++;
            if (hasDecisionFollowUp) knownSignalCount++;
            bool isStrongStandaloneRecommendation =
                hasBudget && (hasTarget || hasUseCase || hasBrand || hasCategory || hasNeedHint);

            bool isShortFollowUpConstraint =
    text.Length <= 40 &&
    !LooksLikeHardReset(text) &&
    (HasNeedHintSignal(text) ||
     HasCategorySignal(text) ||
     HasBrandSwitchSignal(text) ||
     HasAlternativeSignal(text) ||
     HasDecisionFollowUpSignal(text)||
     LooksLikeBudgetFragment(text));

            bool hasOnlySoftRecommendation =
                !hasBudget &&
                ((hasTarget || hasUseCase) && hasNeedHint);

            return new RecommendationSignals
            {
                HasBudget = hasBudget,
                HasGender = hasTarget,
                HasTargetOrUseCase = hasTarget || hasUseCase,
                HasBrand = hasBrand,
                HasCategory = hasCategory,
                HasNeedHint = hasNeedHint,
                HasLowSeatOrHeightInfo = hasLowSeatOrHeightInfo,
                IsConsultationIntent = isConsultationIntent,
                KnownSignalCount = knownSignalCount,
                IsStrongStandaloneRecommendation = isStrongStandaloneRecommendation,
                IsShortFollowUpConstraint = isShortFollowUpConstraint,
                HasOnlySoftRecommendation = hasOnlySoftRecommendation,
                IsVerySparse = knownSignalCount <= 1,
                HasAlternative = hasAlternative,
                HasDecisionFollowUp = hasDecisionFollowUp
            };
        }

        private static bool HasUseCaseSignal(string text)
        {
            return text.Contains("đi làm") || text.Contains("di lam") ||
                   text.Contains("đi học") || text.Contains("di hoc") ||
                   text.Contains("đi phố") || text.Contains("di pho") ||
                   text.Contains("đi tour") || text.Contains("tour") ||
                   text.Contains("sinh viên") || text.Contains("sinh vien");
        }

        private static bool HasCategorySignal(string text)
        {
            return text.Contains("xe ga") ||
                   text.Contains("xe số") || text.Contains("xe so") ||
                   text.Contains("côn tay") || text.Contains("con tay") ||
                   text.Contains("tay ga") ||
                   text.Contains("underbone") ||
                   text.Contains("scooter");
        }

        private static bool HasNeedHintSignal(string text)
        {
            return text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang") ||
                   text.Contains("cốp rộng") || text.Contains("cop rong") ||
                   text.Contains("dễ đi") || text.Contains("de di") ||
                   text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
                   text.Contains("nhẹ") || text.Contains("nhe") || text.Contains("the thao") || text.Contains("thể thao") ||
                   text.Contains("thanh lịch") || text.Contains("thanh lich") ||
                   text.Contains("cá tính") || text.Contains("ca tinh") ||
                   text.Contains("mềm mại") || text.Contains("mem mai") ||
                   text.Contains("nam tính") || text.Contains("nam tinh")|| text.Contains("nữ tính") || text.Contains("nu tinh");
        }

        private static bool HasBrandSwitchSignal(string text)
        {
            return text.Contains("doi sang") ||
                   text.Contains("chuyen sang") ||
                   text.Contains("con honda") ||
                   text.Contains("con yamaha") ||
                   text.Contains("con suzuki") ||
                   text.Contains("con sym") ||
                   text.Contains("con piaggio") ||
                   text.Contains("honda di") ||
                   text.Contains("yamaha di") ||
                   text.Contains("suzuki di") ||
                   text.Contains("sym di") ||
                   text.Contains("piaggio di") ||
                   text is "honda" or "yamaha" or "suzuki" or "sym" or "piaggio";
        }

        private static bool LooksLikeHardReset(string text)
        {
            return text.Contains("doi y") ||
                   text.Contains("gio t muon") ||
                   text.Contains("thoi doi") ||
                   text.Contains("bo qua") ||
                   text.Contains("quen cai truoc");
        }
        private static bool HasDecisionFollowUpSignal(string text)
        {
            return text.Contains("xe nao on") ||
                   text.Contains("mau nao on") ||
                   text.Contains("con nao on") ||
                   text.Contains("nen chon xe nao") ||
                   text.Contains("nen lay xe nao") ||
                   text.Contains("chon con nao") ||
                   text.Contains("chon mau nao");
        }
        private static bool HasExplicitFemaleSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = Normalize(text);

            return Regex.IsMatch(value, @"(^|\s)(nữ|nu)(\s|$)", RegexOptions.IgnoreCase)
                   || value.Contains("cho nữ")
                   || value.Contains("cho nu")
                   || value.Contains("xe nữ")
                   || value.Contains("xe nu")
                   || value.Contains("hợp nữ")
                   || value.Contains("hop nu")
                   || value.Contains("nữ tính")
                   || value.Contains("nu tinh")
                   || value.Contains("phù hợp cho nữ")
                   || value.Contains("phu hop cho nu");
        }
        private static bool HasAlternativeSignal(string text)
        {
            return text.Contains("mau khac") ||
                   text.Contains("xe khac") ||
                   text.Contains("loai khac") ||
                   text.Contains("con khac") ||
                   text.Contains("khac di") ||
                   text.Contains("doi mau khac") ||
                   text.Contains("con mau nao khac");
        }
        private static bool HasExplicitMaleSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = Normalize(text);

            return Regex.IsMatch(value, @"(^|\s)nam(\s|$)", RegexOptions.IgnoreCase)
                   || value.Contains("cho nam")
                   || value.Contains("xe nam")
                   || value.Contains("hợp nam")
                   || value.Contains("hop nam")
                   || value.Contains("phù hợp cho nam")
                   || value.Contains("phu hop cho nam")
                   || value.Contains("nam tính")
                   || value.Contains("manly");
        }

        private static bool LooksLikeBudgetFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"\b(tầm|khoảng|quanh)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase)
                   || Regex.IsMatch(text, @"^\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase)
                   || Regex.IsMatch(text, @"\b(từ|tu)\s*\d+([.,]\d+)?\s*(đến|den)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase)
                   || Regex.IsMatch(text, @"^\d+([.,]\d+)?\s*[-~]\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase)
                   || Regex.IsMatch(text, @"\b(dưới|duoi|trên|tren|tối đa|toi da|không quá|khong qua|ít nhất|it nhat|trở lên|tro len)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase);
        }

        private static string Normalize(string? value)
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

            return Regex.Replace(text, @"\s+", " ");
        }

        private sealed class RecommendationSignals
        {
            public bool HasBudget { get; set; }
            public bool HasGender { get; set; }
            public bool HasTargetOrUseCase { get; set; }
            public bool HasBrand { get; set; }
            public bool HasCategory { get; set; }
            public bool HasNeedHint { get; set; }
            public bool HasLowSeatOrHeightInfo { get; set; }
            public bool IsConsultationIntent { get; set; }
            public int KnownSignalCount { get; set; }
            public bool HasAlternative { get; set; }
            public bool IsStrongStandaloneRecommendation { get; set; }
            public bool IsShortFollowUpConstraint { get; set; }
            public bool HasOnlySoftRecommendation { get; set; }
            public bool IsVerySparse { get; set; }
            public bool HasDecisionFollowUp { get; set; }
        }
    }
}
