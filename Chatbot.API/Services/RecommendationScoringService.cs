using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Recommendation;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class RecommendationScoringService : IRecommendationScoringService
    {
        public List<ScoredRecommendationItem> ScoreProducts(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage,
            int take = 6)
        {
            if (products == null || products.Count == 0)
                return new List<ScoredRecommendationItem>();

            var scored = products
                .Where(p => p != null)
                .Select(p => ScoreSingleProduct(p, intent, profile, normalizedMessage))
                .OrderByDescending(x => x.Breakdown.TotalScore)
                .ThenBy(x => x.Product.Gia)
                .Take(take)
                .ToList();

            return scored;
        }

        private static ScoredRecommendationItem ScoreSingleProduct(
            ProductSummaryDto product,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage)
        {
            var criteria = new List<RecommendationCriterionScore>();

            AddBudgetFit(criteria, product, intent, profile);
            AddBrandFit(criteria, product, intent, profile);
            AddCategoryFit(criteria, product, intent, profile);
            AddTargetStyleFit(criteria, product, intent, profile);
            AddUsageFit(criteria, product, intent, profile, normalizedMessage);
            AddFuelSavingFit(criteria, product, intent, profile);
            AddStorageFit(criteria, product, intent, profile);
            AddLowSeatFit(criteria, product, intent, profile);
            AddStyleFit(criteria, product, intent, profile);
            AddGlobalConstraint(criteria, product, intent, profile);
            AddCompatibilityPenalty(criteria, product, intent, profile);
            var total = criteria.Sum(x => x.WeightedScore);

            return new ScoredRecommendationItem
            {
                Product = product,
                Breakdown = new RecommendationScoreBreakdown
                {
                    ProductId = product.Id,
                    ProductName = product.Ten ?? string.Empty,
                    TotalScore = total,
                    Criteria = criteria
                }
            };
        }

        private static void AddBudgetFit(
            List<RecommendationCriterionScore> criteria,
            ProductSummaryDto product,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            const double weight = 0.24;

            decimal? target =
                intent.TargetPrice ??
                profile.TargetPrice ??
                intent.PriceMax ??
                profile.PriceMax;

            double raw;
            string reason;

            if (!target.HasValue || target.Value <= 0)
            {
                if (product.Gia <= 25_000_000m)
                {
                    raw = 6.2;
                }
                else if (product.Gia <= 35_000_000m)
                {
                    raw = 7.3;
                }
                else if (product.Gia <= 45_000_000m)
                {
                    raw = 7.1;
                }
                else
                {
                    raw = 6.2;
                }

                reason = "Chưa có ngân sách rõ, nên tạm ưu tiên các mẫu có mức giá dễ tiếp cận.";
            }
            else
            {
                var diff = Math.Abs(product.Gia - target.Value);
                if (diff <= 2_000_000m)
                {
                    raw = 10;
                    reason = "Giá rất sát mức đang cân nhắc.";
                }
                else if (diff <= 5_000_000m)
                {
                    raw = 8;
                    reason = "Giá khá gần mức đang cân nhắc.";
                }
                else if (product.Gia < target.Value)
                {
                    raw = 7;
                    reason = "Giá thấp hơn mức đang cân nhắc.";
                }
                else
                {
                    raw = 4;
                    reason = "Giá cao hơn khá nhiều so với mức đang cân nhắc.";
                }
            }

            criteria.Add(BuildCriterion("BudgetFit", weight, raw, reason));
        }

        private static void AddBrandFit(
            List<RecommendationCriterionScore> criteria,
            ProductSummaryDto product,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            const double weight = 0.10;

            var desiredBrand = FirstNonEmpty(intent.Brand, profile.PreferredBrand);
            double raw;
            string reason;

            if (string.IsNullOrWhiteSpace(desiredBrand))
            {
                raw = 5;
                reason = "Chưa khóa theo hãng cụ thể.";
            }
            else if (string.Equals(product.ThuongHieu, desiredBrand, StringComparison.OrdinalIgnoreCase))
            {
                raw = 10;
                reason = $"Đúng hãng {desiredBrand}.";
            }
            else
            {
                raw = 2;
                reason = $"Không đúng hãng {desiredBrand}.";
            }

            criteria.Add(BuildCriterion("BrandFit", weight, raw, reason));
        }

        private static void AddCategoryFit(
            List<RecommendationCriterionScore> criteria,
            ProductSummaryDto product,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            const double weight = 0.22;

            var desiredCategory = NormalizeCategory(FirstNonEmpty(intent.Category, profile.PreferredCategory));
            var actualCategory = NormalizeCategory(product.Loai);

            double raw;
            string reason;

            if (string.IsNullOrWhiteSpace(desiredCategory))
            {
                raw = 5;
                reason = "Chưa khóa theo loại xe cụ thể.";
            }
            else if (string.Equals(desiredCategory, actualCategory, StringComparison.OrdinalIgnoreCase))
            {
                raw = 10;
                reason = $"Đúng nhóm {actualCategory}.";
            }
            else
            {
                raw = -25;
                reason = $"Không đúng nhóm {desiredCategory}.";
            }

            criteria.Add(BuildCriterion("CategoryFit", weight, raw, reason));
        }
        private static void AddTargetStyleFit(
    List<RecommendationCriterionScore> criteria,
    ProductSummaryDto product,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            const double weight = 0.14;
            var stylePreference = ResolveStylePreference(intent, profile);

            double raw = 5;
            string reason = "Mức độ phù hợp với đối tượng sử dụng ở mức trung tính.";

            if (stylePreference == "female")
            {
                bool isScooter = LooksLikeScooter(product);
                bool soft = LooksLikeSoftStyle(product);
                bool sporty = LooksLikeSporty(product);
                bool lowSeat = LooksLikeLowSeat(product);
                bool largeStorage = LooksLikeLargeStorage(product);

                if (soft && isScooter)
                {
                    raw = 9.2;
                    reason = "Dáng xe gọn, mềm và khá hợp nhu cầu nữ.";
                }
                else if (isScooter && (lowSeat || largeStorage))
                {
                    raw = 9.0;
                    reason = "Thuộc nhóm xe ga và khá hợp nếu ưu tiên dễ dùng hằng ngày.";
                }
                else if (isScooter)
                {
                    raw = 8.0;
                    reason = "Thuộc nhóm xe ga, mức phù hợp khá với nhu cầu nữ phổ thông.";
                }
                else if (sporty)
                {
                    raw = 2.5;
                    reason = "Kiểu dáng thiên thể thao hơn, không thật sự hợp nhu cầu nữ phổ thông.";
                }
                else if (LooksLikeUnderbone(product))
                {
                    raw = 2.8;
                    reason = "Xe số thiên về thực dụng, nhưng chưa thật sự hợp nếu đang ưu tiên nữ tính, dễ đi và tiện dùng hằng ngày.";
                }
            }
            else if (stylePreference == "male")
            {
                if (LooksLikeSporty(product) || ContainsAny(product.Ten, "Air Blade", "Winner"))
                {
                    raw = 8.5;
                    reason = "Kiểu dáng khá hợp gu mạnh và hiện đại hơn.";
                }
                else if (ContainsAny(product.Ten, "Future"))
                {
                    raw = 7.2;
                    reason = "Thiết kế thực dụng, hợp nếu bạn ưu tiên sự bền bỉ và ổn định.";
                }
                else if (LooksLikeUnderbone(product))
                {
                    raw = 5.2;
                    reason = "Nhóm xe số phù hợp nếu bạn ưu tiên sự thực dụng.";
                }
                else if (LooksLikeSoftStyle(product))
                {
                    raw = 4.5;
                    reason = "Kiểu dáng mềm hơn, sẽ hợp nếu bạn không quá đặt nặng gu thể thao.";
                }
                else if (LooksLikeScooter(product))
                {
                    raw = 6.8;
                    reason = "Thuộc nhóm xe ga, hợp nếu bạn ưu tiên sự tiện dụng và đi phố thoải mái.";
                }
            }

            criteria.Add(BuildCriterion("TargetStyleFit", weight, raw, reason));
        }
        private static void AddUsageFit(
    List<RecommendationCriterionScore> criteria,
    ProductSummaryDto product,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string normalizedMessage)
        {
            const double weight = 0.20;

            var message = NormalizeText(normalizedMessage);
            var stylePreference = ResolveStylePreference(intent, profile);
            var desiredCategory = NormalizeCategory(FirstNonEmpty(intent.Category, profile.PreferredCategory));
            bool explicitlyScooter = desiredCategory == "xe ga";
            bool explicitlyUnderbone = desiredCategory == "xe so";

            bool forWork = intent.ForWork || profile.ForWork || message.Contains("di lam");
            bool forSchool = intent.ForSchool || profile.ForSchool || message.Contains("di hoc");

            double raw = 5;
            string reason = "Phù hợp ở mức cơ bản với nhu cầu hiện tại.";

            if (forWork)
            {
                if (explicitlyScooter && LooksLikeScooter(product))
                {
                    raw = Math.Max(raw, 9.0);
                    reason = "Đúng nhóm xe ga và khá hợp đi làm hằng ngày.";
                }
                else if (explicitlyUnderbone && LooksLikeUnderbone(product))
                {
                    raw = Math.Max(raw, 8.5);
                    reason = "Đúng nhóm xe số và khá hợp đi làm hằng ngày.";
                }
                if (LooksLikeScooter(product))
                {
                    raw = 8.5;
                    reason = "Khá hợp đi làm hằng ngày.";
                }

                if (ContainsAny(product.Ten, "Vision", "Freego", "Lead", "Latte"))
                {
                    raw = 9.2;
                    reason = "Khá hợp đi làm hằng ngày.";
                }
                else if (ContainsAny(product.Ten, "Air Blade"))
                {
                    raw = 8.4;
                    reason = "Khá hợp đi làm hằng ngày, nhất là nếu ưu tiên cảm giác xe đầm và mạnh hơn.";
                }
                else if (ContainsAny(product.Ten, "Future"))
                {
                    raw = 8.0;
                    reason = "Khá hợp đi làm theo hướng thực dụng và bền bỉ.";
                }
                else if (ContainsAny(product.Ten, "Wave", "Sirius"))
                {
                    raw = 7.0;
                    reason = "Đi làm cơ bản ổn nhưng trải nghiệm ở mức phổ thông.";
                }

                if (stylePreference == "female")
                {
                    if (LooksLikeSoftStyle(product))
                    {
                        raw = Math.Max(raw, 9.2);
                        reason = "Khá hợp đi làm hằng ngày và dễ sử dụng.";
                    }
                    else if (LooksLikeScooter(product))
                    {
                        raw = Math.Max(raw, 8.4);
                        reason = "Khá hợp đi làm hằng ngày, dễ dùng và phù hợp đi phố.";
                    }

                    if (LooksLikeUnderbone(product))
                    {
                        raw = Math.Min(raw, 6.0);
                    }

                    if (LooksLikeSporty(product))
                    {
                        raw = Math.Min(raw, 4.5);
                        reason = "Đi làm vẫn ổn nhưng kiểu xe chưa thật sự hợp nhu cầu nữ phổ thông.";
                    }
                }
                else if (stylePreference == "male")
                {
                    if (ContainsAny(product.Ten, "Air Blade", "Future", "Winner"))
                    {
                        raw = Math.Max(raw, 9.0);
                        reason = "Khá hợp đi làm và hợp gu nam hơn.";
                    }

                    if (LooksLikeSoftStyle(product))
                    {
                        raw = Math.Min(raw, 6.0);
                    }
                }
            }
            else if (forSchool)
            {
                if (ContainsAny(product.Ten, "Vision", "Wave", "Sirius", "Janus"))
                {
                    raw = 9.0;
                    reason = "Hợp đi học và dễ dùng lâu dài.";
                }
                else if (LooksLikeScooter(product))
                {
                    raw = 7.5;
                    reason = "Khá hợp đi học và di chuyển hằng ngày.";
                }

                if (stylePreference == "female" && LooksLikeScooter(product))
                {
                    raw = Math.Max(raw, 8.5);
                    reason = "Khá hợp đi học, dễ dùng và dáng xe gọn.";
                }
            }

            criteria.Add(BuildCriterion("UsageFit", weight, raw, reason));
        }

        private static void AddFuelSavingFit(
     List<RecommendationCriterionScore> criteria,
     ProductSummaryDto product,
     ParsedIntent intent,
     CustomerPreferenceProfile profile)
        {
            const double weight = 0.12;

            bool preferFuelSaving = intent.WantsFuelSaving || profile.WantsFuelSaving;
            double raw = 5;
            string reason = "Tiêu chí tiết kiệm xăng chưa phải ưu tiên chính.";

            if (preferFuelSaving)
            {
                if (LooksLikeFuelSaving(product))
                {
                    raw = 9;
                    reason = "Khá hợp nếu ưu tiên tiết kiệm xăng.";
                }
                else if (product.CC.HasValue && product.CC.Value <= 125)
                {
                    raw = 7;
                    reason = "Dung tích vừa phải, mức phù hợp khá với tiêu chí tiết kiệm xăng.";
                }
                else
                {
                    raw = 6;
                    reason = "Mức phù hợp trung bình với tiêu chí tiết kiệm xăng.";
                }
            }

            criteria.Add(BuildCriterion("FuelSavingFit", weight, raw, reason));
        }
        private static void AddStorageFit(
    List<RecommendationCriterionScore> criteria,
    ProductSummaryDto product,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            const double weight = 0.10;

            bool preferStorage = intent.WantsLargeStorage || profile.WantsLargeStorage;
            double raw = 5;
            string reason = "Tiêu chí cốp rộng chưa phải ưu tiên chính.";

            if (preferStorage)
            {
                if (LooksLikeLargeStorage(product))
                {
                    raw = 9;
                    reason = "Khá hợp nếu ưu tiên cốp rộng.";
                }
                else if (LooksLikeScooter(product))
                {
                    raw = 6.5;
                    reason = "Thuộc nhóm xe ga, mức phù hợp trung bình với tiêu chí cốp rộng.";
                }
                else
                {
                    raw = 4.5;
                    reason = "Không nổi bật về tiêu chí cốp rộng.";
                }
            }

            criteria.Add(BuildCriterion("StorageFit", weight, raw, reason));
        }
        private static void AddLowSeatFit(
     List<RecommendationCriterionScore> criteria,
     ProductSummaryDto product,
     ParsedIntent intent,
     CustomerPreferenceProfile profile)
        {
            const double weight = 0.12;

            bool preferLowSeat =
                intent.NeedsLowSeat || profile.NeedsLowSeat ||
                intent.WantsEasyControl || profile.WantsEasyControl;

            double raw = 5;
            string reason = "Tiêu chí dễ chống chân chưa phải ưu tiên chính.";

            if (preferLowSeat)
            {
                if (LooksLikeLowSeat(product))
                {
                    raw = 9;
                    reason = "Khá hợp nếu ưu tiên dễ chống chân.";
                }
                else if (LooksLikeScooter(product))
                {
                    raw = 6.5;
                    reason = "Khá dễ làm quen nhưng không thật sự nổi bật về chống chân.";
                }
                else
                {
                    raw = 4.5;
                    reason = "Không nổi bật về tiêu chí dễ chống chân.";
                }
            }

            criteria.Add(BuildCriterion("LowSeatFit", weight, raw, reason));
        }

        private static void AddStyleFit(
     List<RecommendationCriterionScore> criteria,
     ProductSummaryDto product,
     ParsedIntent intent,
     CustomerPreferenceProfile profile)
        {
            const double weight = 0.12;

            double raw = 5;
            string reason = "Kiểu dáng ở mức trung tính.";

            var stylePreference = ResolveStylePreference(intent, profile);
            if (stylePreference == "female")
            {
                if (LooksLikeUnderbone(product))
                {
                    raw = 2.8;
                    reason = "Xe số chưa phải lựa chọn nổi bật nếu đang ưu tiên dáng gọn, dễ đi và tiện dùng hằng ngày.";
                }
                else
                if (LooksLikeSoftStyle(product))
                {
                    raw = 9.6;
                    reason = "Kiểu dáng khá hợp gu mềm, gọn và dễ đi.";
                }
                else if (LooksLikeScooter(product))
                {
                    raw = 8.2;
                    reason = "Thuộc nhóm xe ga, kiểu dáng khá dễ dùng và đi phố thoải mái.";
                }
                else if (LooksLikeSporty(product))
                {
                    raw = 3.2;
                    reason = "Kiểu dáng thiên thể thao hơn, sẽ hợp nếu bạn thích phong cách cá tính.";
                }
            }
            else if (stylePreference == "male")
            {
                if (ContainsAny(product.Ten, "Air Blade"))
                {
                    raw = Math.Max(raw, 9.0);
                    reason = "Khá hợp đi làm nếu bạn thích xe ga đầm và cảm giác lái chắc hơn.";
                }
                else if (ContainsAny(product.Ten, "Future", "Winner"))
                {
                    raw = Math.Max(raw, 8.0);
                    reason = "Khá hợp đi làm nếu bạn ưu tiên hướng thực dụng hoặc cảm giác xe chắc chắn.";
                }

                if (LooksLikeSoftStyle(product))
                {
                    raw = Math.Min(raw, 6.5);
                }
            }

            criteria.Add(BuildCriterion("StyleFit", weight, raw, reason));
        }
        private static RecommendationCriterionScore BuildCriterion(
            string name,
            double weight,
            double rawScore,
            string reason)
        {
            return new RecommendationCriterionScore
            {
                Criterion = name,
                Weight = weight,
                RawScore = rawScore,
                WeightedScore = rawScore * weight,
                Reason = reason
            };
        }
        private static void AddCompatibilityPenalty(
    List<RecommendationCriterionScore> criteria,
    ProductSummaryDto product,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            const double weight = 0.12;

            var stylePreference = ResolveStylePreference(intent, profile);
            var desiredCategory = NormalizeCategory(FirstNonEmpty(intent.Category, profile.PreferredCategory));

            double raw = 5;
            string reason = "Không có xung đột lớn với nhu cầu hiện tại.";

            bool userExplicitlyWantsUnderbone = desiredCategory == "xe so";

            if (stylePreference == "female" &&
                LooksLikeUnderbone(product) &&
                !userExplicitlyWantsUnderbone)
            {
                raw = 1.5;
                reason = "Không ưu tiên xe số khi nhu cầu đang thiên về nữ, dễ đi và tiện dùng.";
            }
            else if (stylePreference == "female" &&
                     LooksLikeSporty(product) &&
                     !ContainsAny(NormalizeText(string.Join(" ", intent.RequestedStyles)), "the thao", "ca tinh"))
            {
                raw = 2.0;
                reason = "Không ưu tiên xe quá thể thao nếu chưa nói thích phong cách cá tính.";
            }

            criteria.Add(BuildCriterion("CompatibilityPenalty", weight, raw, reason));
        }
        private static string? FirstNonEmpty(params string?[] values)
        {
            if (values == null || values.Length == 0)
                return null;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static string NormalizeCategory(string? category)
        {
            var text = NormalizeText(category);

            if (text.Contains("ga")) return "xe ga";
            if (text.Contains("so")) return "xe so";
            if (text.Contains("con")) return "con tay";

            return text;
        }
        private static bool ContainsAny(string? text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
                return false;

            return keywords.Any(k =>
                !string.IsNullOrWhiteSpace(k) &&
                text.Contains(k, StringComparison.OrdinalIgnoreCase));
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
        private static HashSet<string> GetTagSet(ProductSummaryDto product)
        {
            var raw = product?.Tags ?? string.Empty;

            return raw
                .Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => NormalizeText(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasTag(ProductSummaryDto product, params string[] candidates)
        {
            if (product == null || candidates == null || candidates.Length == 0)
                return false;

            var tags = GetTagSet(product);

            foreach (var candidate in candidates)
            {
                var normalized = NormalizeText(candidate);
                if (!string.IsNullOrWhiteSpace(normalized) && tags.Contains(normalized))
                    return true;
            }

            return false;
        }

        private static bool IsFemaleTarget(string? target)
        {
            var text = NormalizeText(target);
            return text.Contains("nữ") || text.Contains("nu");
        }

        private static bool IsMaleTarget(string? target)
        {
            var text = NormalizeText(target);
            return text.Contains("nam");
        }
        private static string ResolveStylePreference(ParsedIntent intent, CustomerPreferenceProfile profile)
        {
            // Ưu tiên intent hiện tại hơn profile cũ
            if (IsFemaleTarget(intent.Target) || intent.PrefersFemaleStyle)
                return "female";

            if (IsMaleTarget(intent.Target) || intent.PrefersMaleStyle)
                return "male";

            if (IsFemaleTarget(profile.Target) || profile.PrefersFemaleStyle)
                return "female";

            if (IsMaleTarget(profile.Target) || profile.PrefersMaleStyle)
                return "male";

            return "neutral";
        }

        private static bool LooksLikeScooter(ProductSummaryDto product)
        {
            var category = NormalizeCategory(product.Loai);
            if (category == "xe ga")
                return true;

            return HasTag(product, "xe ga", "tay ga", "scooter");
        }

        private static bool LooksLikeUnderbone(ProductSummaryDto product)
        {
            var category = NormalizeCategory(product.Loai);
            if (category == "xe so")
                return true;

            return HasTag(product, "xe so", "xe số", "underbone");
        }

        private static bool LooksLikeLargeStorage(ProductSummaryDto product)
        {
            return HasTag(product,
                "cop rong", "cốp rộng",
                "co rong", "cốp lớn",
                "large storage", "large trunk")
                || ContainsAny(product.Ten ?? string.Empty, "Lead", "Freego", "Air Blade", "Latte");
        }

        private static bool LooksLikeLowSeat(ProductSummaryDto product)
        {
            return HasTag(product,
                "yen thap", "yên thấp",
                "de chong chan", "dễ chống chân",
                "de dieu khien", "dễ điều khiển")
                || ContainsAny(product.Ten ?? string.Empty, "Vision", "Janus", "Latte", "Zip");
        }

        private static bool LooksLikeFuelSaving(ProductSummaryDto product)
        {
            return HasTag(product,
                "tiet kiem xang", "tiết kiệm xăng",
                "economical", "fuel saving")
                || ContainsAny(product.Ten ?? string.Empty, "Vision", "Wave", "Future", "Sirius");
        }

        private static bool LooksLikeSoftStyle(ProductSummaryDto product)
        {
            return HasTag(product,
                "nu tinh", "nữ tính",
                "mem", "mềm",
                "thanh lich", "thanh lịch",
                "gon", "gọn")
                || ContainsAny(product.Ten ?? string.Empty, "Vision", "Latte", "Grande", "Janus", "Zip", "Lead");
        }

        private static bool LooksLikeSporty(ProductSummaryDto product)
        {
            return HasTag(product,
                "the thao", "thể thao",
                "manh me", "mạnh mẽ",
                "ca tinh", "cá tính",
                "sporty")
                || ContainsAny(product.Ten ?? string.Empty, "Air Blade", "Winner", "Exciter", "Raider", "Sonic");
        }

        public static List<string> GetTopReasons(RecommendationScoreBreakdown breakdown, int top = 2)
        {
            if (breakdown == null || breakdown.Criteria == null)
                return new List<string>();

            var genericReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Chưa có nhu cầu sử dụng quá rõ.",
    "Phù hợp ở mức cơ bản với nhu cầu hiện tại.",
    "Chưa khóa theo hãng cụ thể.",
    "Chưa khóa theo loại xe cụ thể.",
    "Kiểu dáng ở mức trung tính.",
    "Tiêu chí tiết kiệm xăng chưa phải ưu tiên chính.",
    "Tiêu chí cốp rộng chưa phải ưu tiên chính.",
    "Tiêu chí dễ chống chân chưa phải ưu tiên chính.",
    "Đúng nhóm xe ga.",
    "Đúng nhóm xe số.",
    "Không đúng nhóm xe ga.",
    "Không đúng nhóm xe số.",
    "Không đúng nhóm con tay."
};

            var selected = breakdown.Criteria
     .Where(x => !string.IsNullOrWhiteSpace(x.Reason))
     .OrderByDescending(x => x.WeightedScore)
     .Where(x => !genericReasons.Contains(x.Reason))
     .Select(x => x.Reason!.Trim().TrimEnd('.'))
     .Distinct()
     .Take(top)
     .ToList();

            if (selected.Count == 0)
            {
                selected = breakdown.Criteria
                    .Where(x => !string.IsNullOrWhiteSpace(x.Reason))
                    .OrderByDescending(x => x.WeightedScore)
                    .Take(top)
                    .Select(x => x.Reason!.Trim().TrimEnd('.'))
                    .ToList();
            }

            return selected;
        }
        private static void AddGlobalConstraint(
    List<RecommendationCriterionScore> criteria,
    ProductSummaryDto product,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            const double weight = 0.25;

            var stylePreference = ResolveStylePreference(intent, profile);
            var desiredCategory = NormalizeCategory(FirstNonEmpty(intent.Category, profile.PreferredCategory));

            bool wantsUnderbone = desiredCategory == "xe so";

            double raw = 5;
            string reason = "Không có xung đột lớn với nhu cầu.";

            // ❗ NỮ + KHÔNG nói xe số → loại xe số
            if (stylePreference == "female" &&
                LooksLikeUnderbone(product) &&
                !wantsUnderbone)
            {
                raw = 0;
                reason = "Không phù hợp vì đang ưu tiên xe cho nữ, dễ đi và tiện dùng.";
            }

            // ❗ NỮ + KHÔNG nói thể thao → phạt xe thể thao mạnh
            else if (stylePreference == "female" &&
                     LooksLikeSporty(product))
            {
                raw = 2;
                reason = "Kiểu xe thiên thể thao, không phù hợp nhu cầu nữ phổ thông.";
            }

            criteria.Add(BuildCriterion("GlobalConstraint", weight, raw, reason));
        }
    }
}