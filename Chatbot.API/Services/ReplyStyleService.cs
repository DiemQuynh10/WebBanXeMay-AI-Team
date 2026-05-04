using System.Text;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ReplyStyleService : IReplyStyleService
    {
        private static readonly Random _random = new();

        private static string Pick(params string[] options)
        {
            if (options == null || options.Length == 0)
                return string.Empty;

            return options[_random.Next(options.Length)];
        }

        public string BuildRecommendationReply(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent,
            Func<ProductSummaryDto, string> reasonFactory)
        {
            if (products == null || products.Count == 0)
            {
                return "Mình chưa lọc ra được mẫu nào thật sự phù hợp từ dữ liệu hiện tại. Bạn thử nói thêm một tiêu chí như loại xe, hãng hoặc mức giá sát hơn nhé.";
            }

            var sb = new StringBuilder();
            sb.AppendLine(BuildRecommendationIntro(intent, products.Count));
            sb.AppendLine();

            foreach (var item in products)
            {
                var reason = SafeReason(reasonFactory, item, "mẫu này khá cân bằng trong nhóm");

                var line = Pick(
     $"- {item.Ten} ({item.Gia:N0} VNĐ): {reason}",
     $"- {item.Ten} ({item.Gia:N0} VNĐ) — {reason}",
     $"- {item.Ten} ({item.Gia:N0} VNĐ) khá hợp nếu bạn ưu tiên {reason}",
     $"- {item.Ten} ({item.Gia:N0} VNĐ) đáng xem vì {reason}",
     $"- {item.Ten} ({item.Gia:N0} VNĐ) là phương án nên cân nhắc: {reason}"
 );

                sb.AppendLine(line);
            }

            sb.AppendLine();
            sb.AppendLine(BuildRecommendationHint(intent));

            return sb.ToString().Trim();
        }

        public string BuildRecommendationNoMatchReply(
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string? effectiveCategory)
        {
            var brand = FirstNonEmpty(intent.Brand, profile?.PreferredBrand);
            var category = FirstNonEmpty(intent.Category, effectiveCategory);

            bool hasBudget = HasBudget(intent);

            if (!string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(category) && hasBudget)
            {
                return Pick(
                    $"Hiện mình chưa thấy mẫu {category} của {brand} nào khớp thật sát mức giá bạn đang nhắm tới.",
                    $"Trong dữ liệu hiện tại, mình chưa thấy mẫu {category} của {brand} nào thật sự khớp với mức giá này.",
                    $"Nếu giữ cả tiêu chí {category}, hãng {brand} và mức giá hiện tại thì mình chưa lọc ra được mẫu nào thật sự sát."
                ) + " Bạn có thể nới nhẹ ngân sách hoặc bỏ bớt một tiêu chí để mình lọc tiếp.";
            }

            if (!string.IsNullOrWhiteSpace(brand) && hasBudget)
            {
                return Pick(
                    $"Hiện mình chưa thấy mẫu {brand} nào khớp thật sát mức giá bạn đang nhắm tới.",
                    $"Trong dữ liệu hiện tại, mình chưa thấy mẫu {brand} nào thật sự khớp với mức giá này.",
                    $"Nếu vẫn giữ hãng {brand} và mức giá hiện tại thì mình chưa lọc ra được mẫu nào thật sự sát."
                ) + " Bạn có thể nới nhẹ ngân sách hoặc đổi thêm loại xe để mình lọc tiếp.";
            }

            if (!string.IsNullOrWhiteSpace(category) && hasBudget)
            {
                return Pick(
                    $"Hiện mình chưa thấy mẫu {category} nào khớp thật sát mức giá bạn đang nhắm tới.",
                    $"Trong dữ liệu hiện tại, mình chưa thấy mẫu {category} nào thật sự khớp với mức giá này.",
                    $"Nếu vẫn giữ loại {category} và mức giá hiện tại thì mình chưa lọc ra được mẫu nào thật sự sát."
                ) + " Bạn có thể nới nhẹ ngân sách hoặc đổi sang hãng khác để mình lọc tiếp.";
            }

            if (!string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(category))
            {
                return Pick(
                    $"Hiện mình chưa thấy mẫu {category} của {brand} nào thật sự phù hợp trong dữ liệu hiện tại.",
                    $"Trong dữ liệu hiện tại, mình chưa lọc ra được mẫu {category} của {brand} nào thật sự sát.",
                    $"Nếu giữ cả hãng {brand} và loại {category} thì hiện mình chưa thấy mẫu nào thật sự phù hợp."
                ) + " Bạn có thể nói thêm mức giá hoặc đổi bớt một tiêu chí để mình lọc lại.";
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                return Pick(
                    $"Hiện mình chưa thấy mẫu nào của {brand} thật sự sát với các tiêu chí đang có.",
                    $"Trong dữ liệu hiện tại, mình chưa lọc ra được mẫu {brand} nào thật sự phù hợp với nhu cầu này.",
                    $"Nếu vẫn giữ hướng {brand} thì hiện mình chưa thấy mẫu nào thật sự ổn."
                ) + " Bạn có thể nới giá hoặc nói thêm loại xe để mình lọc lại.";
            }

            return Pick(
                "Mình chưa lọc ra được mẫu nào thật sự phù hợp từ dữ liệu hiện tại.",
                "Hiện mình chưa thấy mẫu nào thật sự sát với nhu cầu này trong dữ liệu hiện tại.",
                "Với các tiêu chí hiện tại, mình chưa lọc ra được mẫu nào thật sự phù hợp."
            ) + " Bạn thử nói thêm một tiêu chí như loại xe, hãng hoặc mức giá sát hơn nhé.";
        }

        public string BuildRefinementReply(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent,
            string normalizedMessage,
            Func<ProductSummaryDto, string> reasonFactory)
        {
            if (products == null || products.Count == 0)
            {
                return "Trong nhóm đang xét, mình chưa thấy mẫu nào nổi bật hơn theo tiêu chí mới.";
            }

            var top = products[0];
            var backups = products.Skip(1).Take(2).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(BuildRefinementIntro(intent, normalizedMessage, products.Count));
            sb.AppendLine();


            if (products.Count == 1)
            {
                sb.AppendLine($"Hiện mình nghiêng về {top.Ten} ({top.Gia:N0} VNĐ), vì {SafeReason(reasonFactory, top, "mẫu này hợp nhất với tiêu chí bạn vừa siết lại")}.");
                sb.AppendLine();
                sb.AppendLine(BuildRefinementHint(intent, normalizedMessage));

                return sb.ToString().Trim();
            }

            var text = Normalize(normalizedMessage);
            var isDecideBest = IsDecideBestRefinement(intent, text);
            var isAlternative = IsAlternativeRefinement(intent, text);

            if (isDecideBest)
            {
                var topReason = SafeReason(reasonFactory, top, "mẫu này cân bằng nhất trong nhóm hiện tại");

                sb.AppendLine($"Nếu chọn một mẫu ổn nhất trong nhóm hiện tại thì mình nghiêng về {top.Ten} ({top.Gia:N0} VNĐ).");
                sb.AppendLine($"Lý do là {topReason}.");

                var backup = products.Skip(1).FirstOrDefault();
                if (backup != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Nếu muốn có phương án dự phòng thì bạn có thể cân nhắc thêm {backup.Ten} ({backup.Gia:N0} VNĐ).");
                }

                sb.AppendLine();
                sb.AppendLine("Nếu muốn, mình có thể so sánh nhanh mẫu này với một mẫu khác cho bạn.");

                return sb.ToString().Trim();
            }

            var mainReason = SafeReason(
    reasonFactory,
    top,
    "mẫu này hợp nhất với tiêu chí bạn vừa nói");

            var topLead = isAlternative
    ? Pick(
        $"Nếu đổi sang hướng khác thì {top.Ten} ({top.Gia:N0} VNĐ) khá đáng xem, vì {mainReason}.",
        $"Trong mấy phương án khác này, {top.Ten} ({top.Gia:N0} VNĐ) là mẫu mình thấy ổn nhất, vì {mainReason}.",
        $"{top.Ten} ({top.Gia:N0} VNĐ) là lựa chọn khác khá hợp, vì {mainReason}."
    )
    : Pick(
        $"Trong nhóm này, mình sẽ để {top.Ten} ({top.Gia:N0} VNĐ) lên trước - {mainReason}.",
$"Nếu chọn nhanh, {top.Ten} ({top.Gia:N0} VNĐ) là phương án dễ chốt hơn - {mainReason}.",
$"Mình ưu tiên {top.Ten} ({top.Gia:N0} VNĐ) trước vì {mainReason}."
    );

            sb.AppendLine(topLead);
            if (backups.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(isAlternative
     ? Pick(
         "Ngoài ra, bạn có thể xem thêm các lựa chọn khác này:",
         "Một vài phương án khác cũng đáng tham khảo là:",
         "Bạn cũng có thể cân nhắc thêm các mẫu khác sau:")
     : Pick(
         "Bạn vẫn có thể cân nhắc thêm các phương án sau:",
         "Ngoài ra bạn có thể xem thêm các mẫu này:",
         "Nếu muốn tham khảo thêm thì còn các phương án này:")
 );

                foreach (var item in backups)
                {
                    sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): {SafeReason(reasonFactory, item, "là phương án phụ nhưng vẫn khá đáng cân nhắc trong nhóm này")}");
                }
            }

            sb.AppendLine();
            sb.AppendLine(BuildRefinementHint(intent, normalizedMessage));

            return sb.ToString().Trim();
        }

        public string BuildRefinementNoMatchReply(ParsedIntent intent)
        {
            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                !string.IsNullOrWhiteSpace(intent.Category) &&
                HasBudget(intent))
            {
                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return Pick(
                        $"Hiện mình chưa thấy mẫu {intent.Category} của {intent.Brand} nào còn nằm dưới {intent.PriceMax.Value:N0} VNĐ.",
                        $"Trong tầm dưới {intent.PriceMax.Value:N0} VNĐ, hiện mình chưa lọc ra được mẫu {intent.Category} nào của {intent.Brand}.",
                        $"Nếu vẫn giữ {intent.Brand}, {intent.Category} và mức giá này thì hiện chưa còn mẫu nào phù hợp."
                    );
                }

                if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
                {
                    return Pick(
                        $"Hiện mình chưa thấy mẫu {intent.Category} của {intent.Brand} nào nằm trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ.",
                        $"Trong khoảng giá này, hiện mình chưa lọc ra được mẫu {intent.Category} nào của {intent.Brand}.",
                        $"Nếu vẫn giữ {intent.Brand}, {intent.Category} và khoảng giá hiện tại thì chưa còn mẫu nào phù hợp."
                    );
                }

                return Pick(
                    $"Hiện mình chưa thấy mẫu {intent.Category} của {intent.Brand} nào thật sự phù hợp với mức giá bạn đang nhắm tới.",
                    $"Với các tiêu chí hiện tại, mình chưa lọc ra được mẫu {intent.Category} nào của {intent.Brand}.",
                    $"Nếu vẫn giữ {intent.Brand}, {intent.Category} và mức giá này thì hiện chưa còn mẫu nào phù hợp."
                );
            }

            if (intent.ExcludedBrands.Any())
            {
                var brands = string.Join(", ", intent.ExcludedBrands);

                return Pick(
                    $"Trước đó bạn có nói không muốn {brands}, nên trong nhóm hiện tại mình không còn mẫu nào phù hợp nữa.",
                    $"Vì bạn đang loại {brands}, nên hiện tại danh sách không còn mẫu nào phù hợp.",
                    $"Do bạn đã loại {brands} khỏi lựa chọn, nên hiện mình chưa còn mẫu nào trong nhóm này."
                )
                + " Nếu bạn muốn xem lại hãng này, mình có thể gợi ý lại cho bạn nhé.";
            }

            if (intent.ExcludedCategories.Any())
            {
                return Pick(
                    $"Trong nhóm đang xét, sau khi bỏ {string.Join(", ", intent.ExcludedCategories)} thì hiện chưa còn mẫu nào phù hợp.",
                    $"Sau khi loại nhóm {string.Join(", ", intent.ExcludedCategories)} thì hiện chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu bỏ nhóm {string.Join(", ", intent.ExcludedCategories)} thì hiện không còn mẫu nào phù hợp nữa."
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                return Pick(
                    $"Trong nhóm đang xét thì hiện không còn mẫu {intent.Brand} nào thật sự phù hợp nữa.",
                    $"Sau khi lọc sang {intent.Brand} thì hiện chưa còn mẫu nào thật sự phù hợp trong nhóm này.",
                    $"Nếu giữ nhóm hiện tại và chỉ lấy {intent.Brand} thì mình chưa thấy mẫu nào còn phù hợp."
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                return Pick(
                    $"Trong nhóm đang xét thì hiện không còn mẫu {intent.Category} nào thật sự phù hợp nữa.",
                    $"Sau khi lọc theo {intent.Category} thì hiện chưa còn mẫu nào thật sự phù hợp trong nhóm này.",
                    $"Nếu giữ nhóm hiện tại và chỉ lấy {intent.Category} thì mình chưa thấy mẫu nào còn phù hợp."
                );
            }

            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                return Pick(
                    $"Trong nhóm đang xét, hiện chưa có mẫu nào thật sự nằm dưới {intent.PriceMax.Value:N0} VNĐ.",
                    $"Sau khi siết xuống dưới {intent.PriceMax.Value:N0} VNĐ thì hiện chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu giữ nhóm hiện tại và lọc dưới {intent.PriceMax.Value:N0} VNĐ thì mình chưa thấy mẫu nào còn phù hợp."
                );
            }

            if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                return Pick(
                    $"Trong nhóm đang xét, hiện chưa có mẫu nào thật sự nằm từ {intent.PriceMin.Value:N0} VNĐ trở lên.",
                    $"Sau khi lọc từ {intent.PriceMin.Value:N0} VNĐ trở lên thì hiện chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu giữ nhóm hiện tại và lọc từ {intent.PriceMin.Value:N0} VNĐ trở lên thì mình chưa thấy mẫu nào còn phù hợp."
                );
            }

            if (intent.FilterType == PriceFilterType.Range && intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return Pick(
                    $"Trong nhóm đang xét, hiện chưa có mẫu nào thật sự nằm trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ.",
                    $"Sau khi siết vào khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ thì hiện chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu giữ nhóm hiện tại và lọc trong khoảng giá này thì mình chưa thấy mẫu nào còn phù hợp."
                );
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                return Pick(
                    $"Trong nhóm đang xét, hiện chưa có mẫu nào đủ sát mức khoảng {intent.TargetPrice.Value:N0} VNĐ.",
                    $"Sau khi thu hẹp về mức khoảng {intent.TargetPrice.Value:N0} VNĐ thì hiện chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu giữ nhóm hiện tại và bám theo mức khoảng {intent.TargetPrice.Value:N0} VNĐ thì mình chưa thấy mẫu nào còn phù hợp."
                );
            }

            return Pick(
                "Trong nhóm đang xét, sau khi lọc theo tiêu chí mới thì hiện chưa còn mẫu nào thật sự phù hợp.",
                "Sau khi thêm tiêu chí mới thì hiện nhóm này chưa còn mẫu nào thật sự phù hợp.",
                "Nếu giữ nhóm hiện tại và áp thêm tiêu chí này thì mình chưa thấy mẫu nào còn phù hợp."
            );
        }

        public string BuildRefinementBrandRelaxedReply(
            IReadOnlyList<ProductSummaryDto> products,
            ParsedIntent intent)
        {
            var brand = intent.Brand ?? "hãng bạn đang hỏi";

            var sb = new StringBuilder();
            sb.AppendLine($"Nếu vẫn giữ nhu cầu trước đó thì trong tầm giá hiện tại mình chưa thấy mẫu {brand} nào thật sự sát.");
            sb.AppendLine();
            sb.AppendLine($"Nếu nới nhẹ hơn một chút thì mình thấy các mẫu {brand} này đáng cân nhắc:");
            sb.AppendLine();

            foreach (var item in products)
            {
                sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): đúng hãng {brand}, còn {item.SoLuong} chiếc");
            }

            sb.AppendLine();
            sb.AppendLine("Bạn có thể lọc tiếp thêm theo loại xe, cốp rộng, dễ chống chân hoặc siết lại mức giá.");

            return sb.ToString().Trim();
        }

        public string BuildSearchReply(
            List<ProductSummaryDto> items,
            ParsedIntent intent,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice,
            Func<ProductSummaryDto, string> reasonFactory)
        {
            var shown = items.Take(5).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(BuildSearchIntro(shown.Count, brand, category, minPrice, maxPrice));
            sb.AppendLine();

            foreach (var item in shown)
            {
                sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): {SafeReason(reasonFactory, item, "là mẫu khá sát với bộ lọc hiện tại")}");
            }

            if (items.Count > shown.Count)
            {
                sb.AppendLine();
                sb.AppendLine($"Hiện mình thấy khoảng {items.Count} mẫu phù hợp trong dữ liệu, nên mình lấy trước vài mẫu dễ tham khảo nhất.");
            }

            sb.AppendLine();
            sb.AppendLine(BuildSearchHint(brand, category));

            return sb.ToString().Trim();
        }

        public string BuildSearchEmptyReply(
            ParsedIntent intent,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice,
            List<ProductSummaryDto> nearMatches)
        {
            var filterText = BuildFilterText(brand, category, minPrice, maxPrice);

            if (nearMatches == null || nearMatches.Count == 0)
            {
                return Pick(
                    $"Hiện mình chưa thấy mẫu nào khớp sát với {filterText} trong dữ liệu hiện tại.",
                    $"Mình chưa lọc ra được mẫu nào thật sự khớp với {filterText} trong dữ liệu hiện tại.",
                    $"Nếu giữ đúng {filterText} thì hiện mình chưa thấy mẫu nào thật sự phù hợp."
                ) + " Bạn thử nới nhẹ mức giá hoặc bớt một tiêu chí, mình lọc lại ngay.";
            }

            var sb = new StringBuilder();
            sb.AppendLine(Pick(
                $"Hiện chưa có mẫu nào khớp hoàn toàn với {filterText}.",
                $"Mình chưa thấy mẫu nào khớp thật sát với {filterText}.",
                $"Nếu giữ đúng {filterText} thì hiện chưa có mẫu nào khớp hoàn toàn."
            ));

            sb.AppendLine(Pick(
                "Nhưng nếu tham khảo các phương án gần nhất thì bạn có thể xem nhanh những mẫu này:",
                "Nếu nới điều kiện một chút để tham khảo thì đây là vài mẫu gần nhất:",
                "Nếu mở nhẹ điều kiện để xem thêm thì bạn có thể tham khảo các mẫu này:"
            ));
            sb.AppendLine();

            foreach (var item in nearMatches.Take(4))
            {
                sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): còn {item.SoLuong} chiếc");
            }

            sb.AppendLine();
            sb.AppendLine(Pick(
                "Nếu muốn, bạn nói thêm một tiêu chí ngắn như hãng, loại xe hoặc mức giá sát hơn, mình lọc tiếp cho gọn.",
                "Bạn có thể nói thêm hãng, loại xe hoặc siết lại mức giá để mình lọc sát hơn.",
                "Bạn cứ nói thêm một tiêu chí nhỏ nữa, mình sẽ lọc lại sát hơn cho bạn."
            ));

            return sb.ToString().Trim();
        }

        private static string BuildRecommendationIntro(ParsedIntent intent, int count)
        {
            bool hasBudget = HasBudget(intent);
            bool hasBrandOrCategory = !string.IsNullOrWhiteSpace(intent.Brand) || !string.IsNullOrWhiteSpace(intent.Category);
            bool hasUseCase = intent.ForWork || intent.ForSchool || intent.ForCity || intent.ForTour;
            bool hasFeaturePreference = intent.WantsFuelSaving || intent.WantsLargeStorage || intent.WantsEasyControl || intent.NeedsLowSeat;

            if (count == 1)
            {
                if (hasBudget && hasBrandOrCategory)
                {
                    return Pick(
                        "Mình thấy có 1 mẫu khá sát với tiêu chí bạn đang hỏi:",
                        "Theo các tiêu chí hiện tại thì mình đang nghiêng về mẫu này:",
                        "Nếu bám theo đúng tiêu chí này thì hiện có 1 mẫu nổi bật hơn cả:"
                    );
                }

                if (hasBudget)
                {
                    return Pick(
                        "Trong tầm giá này, mình thấy có 1 mẫu đáng chú ý:",
                        "Với ngân sách này thì mình nghiêng về mẫu sau:",
                        "Nếu bám theo mức giá này thì hiện có 1 mẫu nổi bật hơn:"
                    );
                }

                if (hasUseCase || hasFeaturePreference)
                {
                    return Pick(
                        "Nếu bám theo những gì bạn đang ưu tiên thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Theo nhu cầu bạn vừa nói thì mình đang nghiêng về mẫu này:",
                        "Nếu lấy đúng tiêu chí sử dụng này làm chính thì hiện có 1 mẫu đáng xem nhất:"
                    );
                }

                return Pick(
                    "Mình thấy có 1 mẫu khá hợp với nhu cầu bạn đang nói tới:",
                    "Theo những gì bạn đang ưu tiên thì mình nghiêng về mẫu này:",
                    "Hiện mình thấy có 1 mẫu hợp hơn cả cho nhu cầu này:"
                );
            }

            if (hasBudget && hasBrandOrCategory)
            {
                return Pick(
                    $"Mình thấy có {count} mẫu khá gần với tiêu chí bạn đang muốn:",
                    $"Theo những tiêu chí bạn vừa đưa ra thì mình thấy {count} mẫu này khá đáng xem:",
                    $"Nếu bám theo đúng tiêu chí hiện tại thì mình thấy {count} mẫu này khá ổn:"
                );
            }

            if (hasBudget)
            {
                return Pick(
                    $"Tầm giá này mình lọc được {count} mẫu khá ổn:",
                    $"Với ngân sách này, bạn có thể xem trước {count} mẫu sau:",
                    $"Nếu bám theo mức tiền này thì có {count} mẫu dễ cân nhắc:"
                );
            }

            if (hasUseCase || hasFeaturePreference)
            {
                return Pick(
                    $"Nếu bám theo nhu cầu bạn vừa nói thì mình thấy {count} mẫu này khá đáng chú ý:",
                    $"Theo những gì bạn đang ưu tiên khi dùng xe, mình nghiêng về {count} mẫu sau:",
                    $"Nếu lấy các tiêu chí sử dụng này làm chính thì đây là các mẫu đáng xem trước:"
                );
            }

            return Pick(
                $"Mình thấy {count} mẫu khá hợp với nhu cầu bạn đang nói tới:",
                $"Theo những gì bạn đang ưu tiên, mình thấy {count} mẫu này đáng tham khảo trước:",
                $"Mình đang nghiêng hơn về {count} mẫu sau cho nhu cầu này:"
            );
        }

        private static string BuildRecommendationHint(ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(intent.Brand) && string.IsNullOrWhiteSpace(intent.Category))
            {
                return Pick(
                    "Bạn có thể lọc tiếp theo hãng, loại xe hoặc tiêu chí như cốp rộng, dễ chống chân, tiết kiệm xăng.",
                    "Nếu muốn, mình có thể lọc tiếp theo hãng bạn thích, kiểu xe hoặc mức giá sát hơn.",
                    "Bạn cứ nói thêm một tiêu chí nhỏ như Honda, xe ga hay cốp rộng, mình lọc tiếp cho gọn."
                );
            }

            if (intent.ForWork || intent.ForSchool || intent.ForCity || intent.ForTour)
            {
                return Pick(
                    "Bạn có thể lọc tiếp thêm theo giá, kiểu dáng hoặc các tiêu chí như cốp rộng, tiết kiệm xăng, dễ chống chân.",
                    "Nếu muốn, mình có thể siết tiếp theo nhu cầu đi lại hoặc mức giá sát hơn.",
                    "Bạn nói thêm một tiêu chí nhỏ nữa là mình có thể lọc tiếp sát hơn."
                );
            }

            return Pick(
                "Bạn có thể lọc tiếp thêm theo mức giá, nhu cầu đi lại hoặc các tiêu chí như cốp rộng, dễ chống chân, tiết kiệm xăng.",
                "Nếu muốn, mình có thể siết thêm theo giá, loại xe hoặc tiêu chí sử dụng để danh sách gọn hơn.",
                "Bạn nói thêm một tiêu chí nhỏ nữa là mình có thể lọc tiếp sát hơn."
            );
        }

        private static string BuildRefinementIntro(ParsedIntent intent, string message, int count)
        {
            var text = Normalize(message);
            if (IsDecideBestRefinement(intent, text))
            {
                return Pick(
                    "Mình chốt lại một mẫu đáng chọn nhất trong nhóm hiện tại cho bạn:",
                    "Nếu cần chọn nhanh một mẫu ổn nhất thì mình nghiêng về phương án này:",
                    "Trong nhóm đang xét, mình sẽ chọn ra phương án nổi bật nhất cho bạn:"
                );
            }
            if (IsAlternativeRefinement(intent, text))
            {
                return count <= 1
                    ? Pick(
                        "Mình đổi sang một phương án khác để bạn tham khảo:",
                        "Nếu muốn xem mẫu khác thì hiện mình nghiêng về mẫu này:",
                        "Mình thử chuyển sang lựa chọn khác phù hợp hơn cho bạn:")
                    : Pick(
                        "Mình đổi sang vài phương án khác để bạn tham khảo:",
                        "Nếu muốn xem mẫu khác thì các lựa chọn dưới đây đáng cân nhắc hơn:",
                        "Mình thử mở rộng sang một vài mẫu khác phù hợp hơn cho bạn:");
            }

            var excludedText = BuildExcludedText(intent);
            if (IsCheaperRefinement(text))
            {
                return count <= 1
                    ? Pick(
                        "Mình vẫn giữ nhu cầu trước đó, nhưng lọc xuống nhóm giá dễ chịu hơn:",
                        "Nếu muốn tiết kiệm hơn nhóm vừa xem, hiện mình nghiêng về mẫu này:",
                        "Mình lọc lại theo hướng rẻ hơn nhưng vẫn giữ tiêu chí chính của bạn:")
                    : Pick(
                        "Mình vẫn giữ nhu cầu trước đó, nhưng lọc xuống nhóm giá dễ chịu hơn:",
                        "Nếu muốn tiết kiệm hơn nhóm vừa xem, các mẫu này hợp hơn:",
                        "Mình lọc lại theo hướng rẻ hơn nhưng vẫn giữ tiêu chí chính của bạn:");
            }

            if (!string.IsNullOrWhiteSpace(excludedText))
            {
                return count <= 1
                    ? Pick(
                        $"Sau khi bỏ {excludedText} thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Sau khi loại {excludedText} khỏi nhóm hiện tại thì hiện có 1 mẫu nổi bật hơn cả:",
                        $"Sau khi bỏ {excludedText} thì hiện mẫu này là phương án đáng xem nhất:")
                    : Pick(
                        $"Sau khi bỏ {excludedText} thì mình thấy các mẫu này đáng cân nhắc hơn:",
                       $"Sau khi loại {excludedText} khỏi nhóm hiện tại thì mình nghiêng về các mẫu này:",
                        $"Sau khi bỏ {excludedText} thì các mẫu này nổi bật hơn:");
            }

            if (count <= 1)
            {
                if (HasLargeStorageSignal(intent, text))
                {
                    return Pick(
                        "Nếu ưu tiên cốp rộng trong nhóm đang xét thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy tiêu chí cốp rộng làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu bạn ưu tiên mang đồ tiện hơn thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (HasEasyControlSignal(intent, text))
                {
                    return Pick(
                        "Nếu ưu tiên dễ chống chân hơn thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy tiêu chí dễ làm quen làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu cần dễ chống chân hơn thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (HasFuelSavingSignal(intent, text))
                {
                    return Pick(
                        "Nếu ưu tiên tiết kiệm xăng hơn thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy tiêu chí tiết kiệm xăng làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu muốn tối ưu chi phí đi lại hơn thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (intent.ForWork || text.Contains("di lam") || text.Contains("đi làm"))
                {
                    return Pick(
                        "Nếu ưu tiên đi làm hằng ngày hơn thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy nhu cầu đi làm làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu cần một mẫu hợp đi làm hằng ngày hơn thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (intent.ForSchool || text.Contains("di hoc") || text.Contains("đi học") || text.Contains("sinh vien") || text.Contains("sinh viên"))
                {
                    return Pick(
                        "Nếu ưu tiên đi học hằng ngày hơn thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy nhu cầu đi học làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu cần một mẫu hợp đi học hơn thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (!string.IsNullOrWhiteSpace(intent.Brand))
                {
                    return Pick(
                        $"Nếu tiếp tục giữ hướng {intent.Brand} thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Trong nhóm {intent.Brand} đang xét, hiện có 1 mẫu nổi bật hơn cả:",
                        $"Nếu bám theo hãng {intent.Brand} thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (!string.IsNullOrWhiteSpace(intent.Category))
                {
                    return Pick(
                        $"Nếu tiếp tục bám theo {intent.Category} thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Trong nhóm {intent.Category} đang xét, hiện có 1 mẫu nổi bật hơn cả:",
                        $"Nếu bám theo {intent.Category} thì hiện mẫu này là phương án đáng xem nhất:");
                }

                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return Pick(
                        $"Nếu siết xuống dưới {intent.PriceMax.Value:N0} VNĐ thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Trong mức dưới {intent.PriceMax.Value:N0} VNĐ, hiện có 1 mẫu nổi bật hơn cả:",
                        $"Nếu bám theo mức giá này thì hiện mẫu này là phương án đáng xem nhất:");
                }
            }

            if (HasLargeStorageSignal(intent, text))
            {
                return Pick(
                    "Nếu ưu tiên cốp rộng hơn trong nhóm đang xét, mình thấy các mẫu này đáng cân nhắc hơn:",
                    "Nếu lấy tiêu chí cốp rộng làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu bạn ưu tiên chở đồ tiện hơn thì các mẫu này nổi bật hơn:");
            }

            if (HasEasyControlSignal(intent, text))
            {
                return Pick(
                    "Nếu ưu tiên dễ chống chân hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:",
                    "Nếu ưu tiên yên thấp và dễ làm quen hơn thì mình nghiêng về các mẫu này:",
                    "Nếu cần dễ chống chân hơn thì các mẫu này đáng xem hơn:");
            }

            if (HasFuelSavingSignal(intent, text))
            {
                return Pick(
                    "Nếu ưu tiên tiết kiệm xăng hơn trong nhóm đang xét, mình thấy các mẫu này đáng cân nhắc hơn:",
                    "Nếu lấy tiêu chí tiết kiệm xăng làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu muốn tối ưu chi phí đi lại hơn thì các mẫu này nổi bật hơn:");
            }

            if (intent.ForWork || text.Contains("đi làm") || text.Contains("di lam"))
            {
                return Pick(
                    "Nếu ưu tiên đi làm hằng ngày hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:",
                    "Nếu lấy nhu cầu đi làm làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu cần một mẫu hợp đi làm hằng ngày hơn thì các mẫu này nổi bật hơn:");
            }

            if (intent.ForSchool || text.Contains("đi học") || text.Contains("di hoc") || text.Contains("sinh viên") || text.Contains("sinh vien"))
            {
                return Pick(
                    "Nếu ưu tiên đi học hằng ngày hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:",
                    "Nếu lấy nhu cầu đi học làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu cần một mẫu dễ hợp với nhu cầu đi học hơn thì các mẫu này đáng xem hơn:");
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                bool looksFreshAdvice =
                    text.Contains("tu van") ||
                    text.Contains("tư vấn") ||
                    text.Contains("goi y") ||
                    text.Contains("gợi ý") ||
                    text.Contains("nen mua") ||
                    text.Contains("nên mua");

                if (looksFreshAdvice)
                {
                    return Pick(
                        $"Nếu bạn ưu tiên hãng {intent.Brand}, mình thấy các mẫu này khá đáng xem:",
                        $"Với hướng {intent.Brand} bạn vừa nói, mình nghiêng về các mẫu này:",
                        $"Nếu xét riêng hãng {intent.Brand}, đây là vài mẫu đáng cân nhắc:"
                    );
                }

                return Pick(
                    $"Nếu bạn ưu tiên hãng {intent.Brand}, mình thấy các mẫu này khá đáng xem:",
                    $"Trong nhóm {intent.Brand}, mình nghiêng về các mẫu này:",
                    $"Nếu xét riêng hãng {intent.Brand}, các mẫu này đáng xem hơn:"
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                bool looksFreshAdvice =
                    text.Contains("tu van") ||
                    text.Contains("tư vấn") ||
                    text.Contains("goi y") ||
                    text.Contains("gợi ý") ||
                    text.Contains("nen mua") ||
                    text.Contains("nên mua") ||
                    text.Contains("chon xe") ||
                    text.Contains("chọn xe");

                if (looksFreshAdvice)
                {
                    return Pick(
                        $"Nếu bạn ưu tiên {intent.Category}, mình thấy các mẫu này khá ổn:",
                        $"Với nhu cầu {intent.Category} bạn vừa nói, mình nghiêng về các mẫu này:",
                        $"Nếu bạn đang tìm {intent.Category}, mấy mẫu dưới đây đáng cân nhắc:"
                    );
                }

                return Pick(
                    $"Nếu bạn ưu tiên {intent.Category}, mình thấy các mẫu này khá ổn:",
                    $"Trong nhóm {intent.Category}, mình thấy các mẫu này đáng cân nhắc hơn:",
                    $"Với nhóm {intent.Category}, mình thấy các mẫu này đáng cân nhắc:"
                );
            }
            if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
            {
                return Pick(
                    $"Nếu thu hẹp theo mức giá dưới {intent.PriceMax.Value:N0} VNĐ, hiện mình thấy các mẫu này hợp hơn:",
                    $"Nếu siết lại xuống dưới {intent.PriceMax.Value:N0} VNĐ thì mình nghiêng về các mẫu này:",
                    $"Nếu bám theo mức dưới {intent.PriceMax.Value:N0} VNĐ thì các mẫu này đáng xem hơn:");
            }

            if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return Pick(
                    $"Nếu thu hẹp theo khoảng giá từ {intent.PriceMin.Value:N0} đến {intent.PriceMax.Value:N0} VNĐ, hiện mình thấy các mẫu này hợp hơn:",
                    $"Nếu siết lại trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ thì mình nghiêng về các mẫu này:",
                    $"Trong khoảng giá bạn vừa thu hẹp lại, mình thấy các mẫu này nổi bật hơn:");
            }

            return Pick(
                "Nếu lọc tiếp từ nhóm trước thì hiện mình thấy các mẫu này phù hợp hơn:",
                "Nếu thu hẹp thêm từ nhóm đang xét thì mình nghiêng về các mẫu này:",
                "Sau khi thêm tiêu chí mới, mình thấy các mẫu này nổi bật hơn:");
        }

        private static string BuildSearchIntro(
            int count,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice)
        {
            var filterText = BuildFilterText(brand, category, minPrice, maxPrice);
            if (string.Equals(filterText, "tiêu chí hiện tại", StringComparison.OrdinalIgnoreCase))
            {
                return count == 1
                    ? "Mình tìm thấy 1 mẫu xe hiện có trong cửa hàng:"
                    : $"Mình tìm thấy {count} mẫu xe hiện có trong cửa hàng, bạn có thể xem nhanh:";
            }
            if (count == 1)
            {
                return Pick(
                    $"Mình lọc theo {filterText} và thấy có 1 mẫu khá phù hợp:",
                    $"Theo bộ lọc {filterText}, hiện có 1 mẫu bạn có thể tham khảo:",
                    $"Mình vừa rà theo {filterText} và thấy 1 mẫu nổi bật hơn cả:");
            }

            return Pick(
                $"Mình lọc nhanh theo {filterText} và thấy {count} mẫu khá đáng xem:",
                $"Theo bộ lọc {filterText}, hiện có {count} mẫu bạn có thể tham khảo:",
                $"Mình vừa rà theo {filterText} và thấy {count} mẫu nổi bật:");
        }

        private static string BuildSearchHint(string? brand, string? category)
        {
            if (string.IsNullOrWhiteSpace(brand) && string.IsNullOrWhiteSpace(category))
            {
                return "Bạn muốn mình lọc tiếp theo hãng, loại xe hoặc tầm giá nào không?";
            }
            if (string.IsNullOrWhiteSpace(brand))
            {
                return Pick(
                    "Bạn có hãng nào đang thích không?",
                    "Bạn muốn lọc tiếp theo Honda, Yamaha hay hãng khác?",
                    "Bạn muốn mình siết tiếp theo hãng hay tầm giá?"
                );
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                return Pick(
                    $"Trong {brand}, bạn muốn xem xe ga, xe số hay côn tay?",
                    $"Bạn thích kiểu xe nào của {brand}: xe ga, xe số hay côn tay?",
                    $"Mình lọc tiếp trong {brand} theo loại xe cho gọn nhé?"
                );
            }

            return Pick(
                "Bạn muốn ưu tiên bền, tiết kiệm xăng hay dễ đi hơn?",
                "Bạn cần xe đi làm, đi học hay đi phố là chính?",
                "Bạn muốn mình lọc tiếp theo giá hay độ tiện dụng?"
            );
        }
        private static string BuildFilterText(
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(category))
                parts.Add(category);

            if (!string.IsNullOrWhiteSpace(brand))
                parts.Add($"hãng {brand}");

            if (minPrice.HasValue && maxPrice.HasValue)
                parts.Add($"giá từ {minPrice.Value:N0} đến {maxPrice.Value:N0} VNĐ");
            else if (maxPrice.HasValue)
                parts.Add($"giá dưới {maxPrice.Value:N0} VNĐ");
            else if (minPrice.HasValue)
                parts.Add($"giá từ {minPrice.Value:N0} VNĐ trở lên");

            return parts.Count == 0 ? "tiêu chí hiện tại" : string.Join(", ", parts);
        }

        private static bool HasBudget(ParsedIntent intent)
        {
            return intent.TargetPrice.HasValue || intent.PriceMin.HasValue || intent.PriceMax.HasValue;
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private static string SafeReason(Func<ProductSummaryDto, string> reasonFactory, ProductSummaryDto item, string fallback)
        {
            try
            {
                var reason = reasonFactory(item)?.Trim();
                return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
            }
            catch
            {
                return fallback;
            }
        }

        private static string BuildRefinementHint(ParsedIntent intent, string normalizedMessage)
        {
            var text = Normalize(normalizedMessage);
            if (IsDecideBestRefinement(intent, text))
            {
                return "Bạn có thể nói tên một mẫu khác nếu muốn mình so sánh nhanh trước khi quyết định.";
            }
            if (IsAlternativeRefinement(intent, text))
            {
                return Pick(
                    "Bạn có thể nói thêm hãng, tầm giá hoặc kiểu xe để mình đổi hướng lọc sát hơn.",
                    "Nếu muốn khác rõ hơn nữa, bạn có thể nói thêm hãng, loại xe hoặc mức giá mong muốn.",
                    "Bạn cứ nói thêm một tiêu chí mới như Honda, xe ga hay dưới 40 triệu, mình sẽ lọc lại cho sát hơn."
                );
            }
            if (HasLargeStorageSignal(intent, text) || HasFuelSavingSignal(intent, text) || HasEasyControlSignal(intent, text))
            {
                return Pick(
                    "Bạn có thể lọc tiếp thêm theo hãng, mức giá hoặc nhu cầu đi làm, đi học để nhóm này gọn hơn.",
                    "Nếu muốn, mình có thể siết tiếp theo giá, hãng hoặc một tiêu chí sử dụng khác.",
                    "Bạn cứ nói thêm một tiêu chí nữa như hãng, giá hoặc loại xe, mình lọc tiếp cho gọn."
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Category) &&
    string.IsNullOrWhiteSpace(intent.Brand))
            {
                return Pick(
                    "Bạn đang nghiêng về hãng nào không, hay mình chọn giúp theo tiêu chí bền nhất?",
                    "Bạn muốn ưu tiên hãng nào, hay để mình lọc theo mẫu bền và dễ dùng nhất?",
                    "Bạn thích Honda, Yamaha hay chỉ cần mẫu nào bền và hợp lý nhất?"
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                string.IsNullOrWhiteSpace(intent.Category))
            {
                return Pick(
                    $"Bạn muốn xem xe ga, xe số hay côn tay trong hãng {intent.Brand}?",
                    $"Bạn đang nghiêng về kiểu xe nào của {intent.Brand}: xe ga, xe số hay côn tay?",
                    $"Mình có thể lọc tiếp trong {intent.Brand} theo loại xe hoặc mức giá bạn muốn."
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                !string.IsNullOrWhiteSpace(intent.Category))
            {
                return Pick(
                    "Bạn muốn mình giữ đúng hướng này hay nới thêm lựa chọn khác cũng được?",
                    "Bạn muốn ưu tiên giá dễ chịu hơn hay chọn mẫu bền và đáng dùng hơn?",
                    "Bạn muốn mình lọc tiếp theo giá, độ bền hay tiện ích sử dụng hằng ngày?"
                );
            }
            if (intent.PriceMax.HasValue || intent.TargetPrice.HasValue || intent.PriceMin.HasValue)
            {
                return Pick(
                    "Bạn muốn mình giữ đúng tầm giá này hay ưu tiên bền hơn một chút cũng được?",
                    "Bạn muốn bám sát ngân sách này hay có thể nới nhẹ nếu mẫu đó đáng tiền hơn?",
                    "Bạn muốn mình lọc tiếp theo hãng trong tầm giá này không?"
                );
            }
            return Pick(
                "Bạn có thể lọc tiếp thêm một chút nữa như đổi hãng, siết giá hoặc thêm tiêu chí sử dụng.",
                "Nếu muốn, mình có thể lọc tiếp theo hãng, loại xe hoặc mức giá sát hơn.",
                "Bạn cứ nói thêm một tiêu chí nhỏ nữa, mình sẽ lọc tiếp cho gọn hơn."
            );
        }

        public string BuildClusteredRecommendationReply(
      IReadOnlyList<ProductSummaryDto> ranked,
      ParsedIntent intent,
      CustomerPreferenceProfile profile,
      ProductSummaryDto? anchor,
      List<string> bucketNarratives,
      string normalizedMessage,
      Func<ProductSummaryDto, List<string>> getReasons)
        {
            var lead = BuildRecommendationLead(intent, profile, anchor, normalizedMessage);
            var contextSummary = BuildContextSummary(intent, profile, normalizedMessage);

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(lead))
            {
                sb.AppendLine(lead);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(contextSummary))
            {
                sb.AppendLine(contextSummary);
                sb.AppendLine();
            }

            if (bucketNarratives != null && bucketNarratives.Count > 0)
            {
                foreach (var line in bucketNarratives)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine(line);
                }

                sb.AppendLine();
            }

            int index = 0;

            foreach (var item in ranked)
            {
                var reasons = getReasons(item) ?? new List<string>();
                var reasonText = BuildReasonText(reasons);
                var priceText = $"{item.Gia:N0} VNĐ";
                var itemName = item.Ten ?? "Mẫu xe";

                var line = index switch
                {
                    0 => Pick(
                        $"- {itemName} ({priceText}) là mẫu mình sẽ xem trước: {reasonText}.",
                        $"- {itemName} ({priceText}) nổi bật nhất trong nhóm này vì {reasonText}.",
                        $"- Với {itemName} ({priceText}), điểm đáng chú ý là {reasonText}."
                    ),
                    1 => Pick(
    $"- {itemName} ({priceText}) cũng đáng cân nhắc vì {reasonText}.",
    $"- Nếu muốn thêm phương án khác, {itemName} ({priceText}) khá ổn: {reasonText}.",
    $"- {itemName} ({priceText}) là lựa chọn phụ khá hợp, nhất là vì {reasonText}."
),
                    _ => Pick(
                        $"- {itemName} ({priceText}) phù hợp nếu bạn muốn thêm một lựa chọn {reasonText}.",
                        $"- Còn {itemName} ({priceText}) thì hợp để tham khảo thêm vì {reasonText}.",
                        $"- {itemName} ({priceText}) cũng có thể xem qua, đặc biệt nếu bạn ưu tiên {reasonText}."
                    )
                };

                sb.AppendLine(line);
                index++;
            }

            sb.AppendLine();
            sb.Append(BuildRecommendationFollowUp(intent, profile));

            return sb.ToString().Trim();
        }
        private static string BuildReasonText(List<string> reasons)
        {
            if (reasons == null || reasons.Count == 0)
            {
                var fallbacks = new[]
 {
    "dễ đi, hợp chạy trong phố hằng ngày",
    "gọn nhẹ, không cần làm quen nhiều khi sử dụng",
    "chi phí sử dụng dễ chịu, hợp đi lại thường xuyên",
    "phù hợp nếu bạn cần một mẫu xe đơn giản, dễ dùng",
    "xoay trở linh hoạt trong đô thị, không bị cồng kềnh",
    "mức giá dễ tiếp cận, phù hợp để dùng hằng ngày"
};

                return fallbacks[new Random().Next(fallbacks.Length)];
            }

            var cleaned = reasons
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().TrimEnd('.'))
                .Where(x =>
                    !x.Contains("đúng nhóm", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("phù hợp với nhóm", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("không phù hợp vì bạn đang ưu tiên", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("hiện còn", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("còn ", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("chiếc", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("tồn kho", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("không có xung đột", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("chưa khóa", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("chưa có loại xe cụ thể", StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals("Đúng hãng Honda", StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals("Đúng hãng Yamaha", StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals("Đúng hãng Piaggio", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("lựa chọn lý tưởng", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("lựa chọn tuyệt vời", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("mang lại", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("thiết kế", StringComparison.OrdinalIgnoreCase) &&
                    !x.Contains("ưu tiên xe số", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("ưu tiên xe ga", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("ưu tiên côn tay", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("muốn chú trọng vào xe số", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("chú trọng vào xe số", StringComparison.OrdinalIgnoreCase) &&
!x.Contains("bộ lọc", StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals("Đúng hãng SYM", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            if (cleaned.Count == 0)
            {
                var fallbacks = new[]
 {
    "gọn nhẹ, dễ điều khiển khi đi trong phố",
    "chi phí sử dụng dễ chịu, hợp đi lại hằng ngày",
    "dễ làm quen, phù hợp nếu bạn cần xe đơn giản để dùng lâu dài",
    "mức giá dễ tiếp cận, không tạo áp lực ngân sách",
    "phù hợp đi làm hoặc đi học vì cách dùng khá đơn giản",
    "dễ kiểm soát, hợp với nhu cầu di chuyển thường xuyên"
};

                return fallbacks[new Random().Next(fallbacks.Length)];
            }

            if (cleaned.Count == 1)
                return cleaned[0];

            return Pick(
                $"{cleaned[0]}, đồng thời {cleaned[1]}",
                $"{cleaned[0]}, ngoài ra {cleaned[1]}",
                $"{cleaned[0]}, thêm vào đó {cleaned[1]}",
                $"{cleaned[0]} và {cleaned[1]}",
                $"{cleaned[0]}, đi kèm với {cleaned[1]}"
            );
        }
        private static string BuildRecommendationFollowUp(ParsedIntent intent, CustomerPreferenceProfile profile)
        {
            var desiredCategory = NormalizeCategory(intent.Category ?? profile.PreferredCategory);
            if (intent.PriceMax.HasValue || intent.TargetPrice.HasValue || profile.PriceMax.HasValue || profile.TargetPrice.HasValue)
            {
                return Pick(
                    "Bạn muốn mình lọc tiếp theo hướng rẻ hơn, hay giữ tầm này để chọn mẫu đáng dùng hơn?",
                    "Bạn muốn ưu tiên tiết kiệm chi phí, hay chọn xe dễ đi và ổn định lâu dài hơn?",
                    "Mình có thể lọc tiếp theo hãng, độ dễ đi hoặc tiết kiệm xăng nếu bạn muốn."
                );
            }
            if (desiredCategory == "xe ga")
            {
                return Pick(
                    "Bạn thích xe nhẹ dễ đi hay kiểu đầm chắc hơn?",
                    "Bạn cần cốp rộng hay chỉ cần xe đi êm, dễ dùng là được?",
                    "Bạn có hãng nào đang thích không, hay để mình chọn mẫu hợp nhất?"
                );
            }

            if (desiredCategory == "xe so")
            {
                return Pick(
                    "Bạn muốn mình chọn luôn mẫu bền nhất hay có hãng bạn thích?",
                    "Bạn ưu tiên Honda hay chỉ cần mẫu nào bền, dễ nuôi là được?",
                    "Bạn thích xe số gọn nhẹ hay chắc chắn một chút?"
                );
            }

            if (desiredCategory == "con tay")
            {
                return Pick(
                    "Bạn thích kiểu thể thao mạnh hơn hay dễ chạy hằng ngày hơn?",
                    "Bạn muốn xe bốc hơn hay ưu tiên dễ điều khiển?",
                    "Bạn có hãng nào đang nhắm không?"
                );
            }

            if (intent.ForWork || profile.ForWork)
            {
                return Pick(
                    "Bạn đi làm hằng ngày nhiều hơn trong phố hay đường xa?",
                    "Bạn muốn ưu tiên tiết kiệm xăng hay xe chạy đầm hơn?",
                    "Bạn có muốn mình lọc tiếp theo mức giá dễ mua hơn không?"
                );
            }

            return Pick(
                "Bạn đang nghiêng về hãng nào không?",
                "Bạn muốn mình chọn giúp luôn hay lọc kỹ thêm theo tiêu chí nào?",
                "Bạn thích xe dễ đi, tiết kiệm hay kiểu dáng đẹp hơn?"
            );
        }
        private static string DisplayCategory(string? category)
        {
            var normalized = NormalizeCategory(category);

            return normalized switch
            {
                "xe so" => "xe số",
                "xe ga" => "xe ga",
                "con tay" => "xe côn tay",
                _ => category ?? string.Empty
            };
        }
        private static string NormalizeCategory(string? category)
        {
            var text = NormalizeText(category);

            if (text.Contains("ga")) return "xe ga";
            if (text.Contains("so")) return "xe so";
            if (text.Contains("con")) return "con tay";

            return text;
        }

        private static string BuildRecommendationLead(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    ProductSummaryDto? anchor,
    string normalizedMessage)
        {
            if (anchor == null)
                return "Mình lọc nhanh theo nhu cầu bạn vừa nói thì có vài mẫu khá đáng cân nhắc.";

            var anchorName = anchor.Ten ?? "mẫu này";
            var desiredCategoryRaw = intent.Category ?? profile.PreferredCategory;
            var desiredCategory = NormalizeCategory(desiredCategoryRaw);
            var displayCategory = DisplayCategory(desiredCategoryRaw);
            bool forWork = intent.ForWork || profile.ForWork;
            bool forSchool = intent.ForSchool || profile.ForSchool;

            bool hasPriceAnchor =
                intent.TargetPrice.HasValue ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                profile.TargetPrice.HasValue ||
                profile.PriceMin.HasValue ||
                profile.PriceMax.HasValue;

            bool hasBrandAnchor =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(profile.PreferredBrand);

            bool hasConcretePreference =
                !string.IsNullOrWhiteSpace(desiredCategory) ||
                hasPriceAnchor ||
                hasBrandAnchor ||
                forWork ||
                forSchool ||
                intent.WantsFuelSaving ||
                profile.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                profile.WantsLargeStorage ||
                intent.NeedsLowSeat ||
                profile.NeedsLowSeat ||
                intent.WantsEasyControl ||
                profile.WantsEasyControl;

            var message = NormalizeText(normalizedMessage);
            var brand = intent.Brand ?? profile.PreferredBrand;

            if (profile.ExcludedBrands != null && profile.ExcludedBrands.Any())
            {
                var excluded = string.Join(", ", profile.ExcludedBrands);

                return Pick(
                    $"Mình đã loại {excluded} ra và chọn các mẫu khác phù hợp hơn cho bạn.",
                    $"Do bạn không muốn {excluded}, mình sẽ gợi ý các mẫu khác để bạn tham khảo.",
                    $"Mình bỏ {excluded} khỏi lựa chọn và lọc lại các mẫu phù hợp hơn."
                );
            }
            if (!string.IsNullOrWhiteSpace(brand) &&
                profile.ExcludedBrands.Any(x => string.Equals(x, brand, StringComparison.OrdinalIgnoreCase)))
            {
                return $"Mình hiểu là bạn muốn xem lại {brand}, nên mình sẽ bỏ điều kiện loại {brand} trước đó và gợi ý lại các mẫu phù hợp.";
            }
            bool isVeryOpenAsk =
                ContainsAny(message,
                    "tư vấn xe", "tu van xe",
                    "xe cho nam", "xe cho nữ", "xe cho nu",
                    "xe nam", "xe nữ", "xe nu") &&
                !hasPriceAnchor &&
                string.IsNullOrWhiteSpace(desiredCategory) &&
                !forWork &&
                !forSchool;

            if (ContainsAny(message, "ben", "bền") && !string.IsNullOrWhiteSpace(desiredCategory))
            {
                return Pick(
     $"Nếu ưu tiên {displayCategory} bền thì {anchorName} là mẫu hợp nhất.",
     $"Nếu cần {displayCategory} bền thì {anchorName} là lựa chọn dễ dùng nhất.",
     $"Nếu bạn cần {displayCategory} bền thì {anchorName} là mẫu đáng chọn nhất."
 );
            }

            if (ContainsAny(message, "tiet kiem xang", "tiết kiệm xăng"))
            {
                return $"Nếu ưu tiên tiết kiệm xăng, mình đang nghiêng hơn về {anchorName}.";
            }

            if (ContainsAny(message, "cop rong", "cốp rộng"))
            {
                return $"Nếu ưu tiên cốp rộng, mình đang nghiêng hơn về {anchorName}.";
            }

            if (!string.IsNullOrWhiteSpace(desiredCategory) && forWork)
            {
                return $"Nếu bạn ưu tiên {displayCategory} để đi làm, mình đang nghiêng hơn về {anchorName}.";
            }

            if (!string.IsNullOrWhiteSpace(desiredCategory))
            {
                bool isFemaleFollowUp =
                    ContainsAny(message,
                        "cho nu",
                        "cho nữ",
                        "xe nu",
                        "xe nữ",
                        "hop nu",
                        "hợp nữ",
                        "nu tinh",
                        "nữ tính");

                if (isFemaleFollowUp && desiredCategory == "xe so")
                {
                    return $"Nếu vẫn ưu tiên xe số nhưng muốn hợp với nữ hơn, mình đang nghiêng về {anchorName}.";
                }

                if (isFemaleFollowUp && desiredCategory == "xe ga")
                {
                    return $"Nếu ưu tiên xe ga và muốn hợp với nữ hơn, mình đang nghiêng về {anchorName}.";
                }

                if (ContainsAny(message, "tu van", "tư vấn", "goi y", "gợi ý", "nen mua", "nên mua"))
                {
                    return Pick(
                        $"Nếu chọn nhanh trong nhóm {displayCategory}, mình sẽ xem {anchorName} trước.",
                        $"Với nhu cầu {displayCategory}, {anchorName} là mẫu mình thấy dễ bắt đầu cân nhắc nhất.",
                        $"Trong nhóm {displayCategory}, mình sẽ ưu tiên {anchorName} vì khá hợp với hướng bạn đang tìm."
                    );
                }


                return Pick(
     $"Nếu bám theo nhóm {displayCategory}, mình sẽ ưu tiên {anchorName} trước.",
     $"Trong nhóm {displayCategory}, {anchorName} là mẫu nổi bật hơn để cân nhắc.",
     $"Với hướng {displayCategory}, mình thấy {anchorName} là lựa chọn dễ xem trước."
 );
            }

            if (forWork)
            {
                return $"Với nhu cầu đi làm hằng ngày, mình đang nghiêng hơn về {anchorName}.";
            }

            if (forSchool)
            {
                return $"Với nhu cầu đi học, mình đang nghiêng hơn về {anchorName}.";
            }

            bool hasGenderContext =
     intent.PrefersFemaleStyle ||
     profile.PrefersFemaleStyle ||
     intent.PrefersMaleStyle ||
     profile.PrefersMaleStyle;

            if (hasPriceAnchor && hasGenderContext)
            {
                return $"Nếu chọn nhanh trong tầm này, mình nghiêng về {anchorName} trước - vì mẫu này vừa sát ngân sách, vừa hợp với nhu cầu bạn đang nói.";
            }
            if (hasPriceAnchor)
            {
                return Pick(
                    $"Nếu chọn nhanh trong tầm này, mình sẽ xem {anchorName} trước vì giá khá sát ngân sách.",
                    $"Với mức tiền này, {anchorName} là mẫu dễ cân nhắc nhất để bắt đầu so sánh.",
                    $"Trong khoảng giá này, {anchorName} là phương án nổi bật hơn vì không lệch quá xa ngân sách."
                );
            }

            return Pick(
    "Mình lọc được vài mẫu khá sát nhu cầu của bạn:",
    "Mình chọn ra một vài mẫu đáng cân nhắc để bạn xem trước:",
    "Dựa trên nhu cầu bạn nói, mình gợi ý trước vài mẫu phù hợp:"
);
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
        private static bool HasLargeStorageSignal(ParsedIntent intent, string text)
        {
            return intent.WantsLargeStorage || text.Contains("cốp rộng") || text.Contains("cop rong");
        }

        private static bool HasFuelSavingSignal(ParsedIntent intent, string text)
        {
            return intent.WantsFuelSaving || text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang");
        }

        private static bool HasEasyControlSignal(ParsedIntent intent, string text)
        {
            return intent.WantsEasyControl || intent.NeedsLowSeat ||
                   text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
                   text.Contains("yên thấp") || text.Contains("yen thap");
        }
        private static bool IsCheaperRefinement(string text)
        {
            return text.Contains("re hon") ||
                   text.Contains("rẻ hơn") ||
                   text.Contains("mềm hơn") ||
                   text.Contains("mem hon") ||
                   text.Contains("tiết kiệm hơn") ||
                   text.Contains("tiet kiem hon") ||
                   text.Contains("giá thấp hơn") ||
                   text.Contains("gia thap hon");
        }
        private static bool IsAlternativeRefinement(ParsedIntent intent, string text)
        {
            return string.Equals(intent.ComparisonFeature, "alternative", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("mau khac")
                   || text.Contains("xe khac")
                   || text.Contains("khac di")
                   || text.Contains("loai khac")
                   || text.Contains("con khac");
        }
        private static bool IsDecideBestRefinement(ParsedIntent intent, string text)
        {
            return string.Equals(intent.ComparisonFeature, "decide_best", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("xe nao on")
                   || text.Contains("mau nao on")
                   || text.Contains("con nao on")
                   || text.Contains("nen chon")
                   || text.Contains("chon xe nao")
                   || text.Contains("chon mau nao");
        }
        private static string BuildExcludedText(ParsedIntent intent, CustomerPreferenceProfile? profile = null)
        {
            var parts = new List<string>();

            var brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in intent.ExcludedBrands)
                brands.Add(b);

            if (profile?.ExcludedBrands != null)
            {
                foreach (var b in profile.ExcludedBrands)
                    brands.Add(b);
            }

            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in intent.ExcludedCategories)
                categories.Add(DisplayCategory(c));

            if (profile?.ExcludedCategories != null)
            {
                foreach (var c in profile.ExcludedCategories)
                    categories.Add(DisplayCategory(c));
            }

            if (brands.Any())
                parts.AddRange(brands);

            if (categories.Any())
                parts.AddRange(categories);

            return parts.Count == 0 ? string.Empty : string.Join(", ", parts);
        }
        private static string BuildContextSummary(
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage)
        {
            var parts = new List<string>();
            var text = NormalizeText(normalizedMessage);

            bool female =
                intent.PrefersFemaleStyle ||
                profile.PrefersFemaleStyle ||
                ContainsAny(text, "cho nu", "cho nữ", "xe nu", "xe nữ", "hop nu", "hợp nữ");

            bool male =
                intent.PrefersMaleStyle ||
                profile.PrefersMaleStyle ||
                ContainsAny(text, "cho nam", "xe nam", "hop nam", "hợp nam");

            if (female)
                parts.Add("xe hợp với nữ");
            else if (male)
                parts.Add("xe hợp với nam");
            var brand = intent.Brand ?? profile.PreferredBrand;
            if (!string.IsNullOrWhiteSpace(brand))
                parts.Add($"hãng {brand}");

            var category = DisplayCategory(intent.Category ?? profile.PreferredCategory);
            if (!string.IsNullOrWhiteSpace(category))
                parts.Add(category);

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                parts.Add($"khoảng {intent.TargetPrice.Value:N0} VNĐ");
            }
            else if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                parts.Add($"dưới {intent.PriceMax.Value:N0} VNĐ");
            }
            else if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                parts.Add($"từ {intent.PriceMin.Value:N0} đến {intent.PriceMax.Value:N0} VNĐ");
            }
            else if (intent.TargetPrice.HasValue)
            {
                parts.Add($"khoảng {intent.TargetPrice.Value:N0} VNĐ");
            }

            if (intent.WantsFuelSaving || profile.WantsFuelSaving)
                parts.Add("ưu tiên tiết kiệm xăng");

            if (intent.WantsLargeStorage || profile.WantsLargeStorage)
                parts.Add("ưu tiên cốp rộng");

            if (intent.WantsEasyControl || intent.NeedsLowSeat || profile.WantsEasyControl || profile.NeedsLowSeat)
                parts.Add("ưu tiên dễ đi");

            if (parts.Count == 0)
                return string.Empty;

            return Pick(
    $"Mình sẽ bám theo hướng {string.Join(", ", parts)} để chọn các mẫu sát nhu cầu hơn.",
    $"Mình sẽ dựa trên các tiêu chí {string.Join(", ", parts)} để gợi ý cho hợp hơn.",
    $"Mình vẫn giữ các tiêu chí chính: {string.Join(", ", parts)} để lọc danh sách cho gọn."
);
        }
        private static string Normalize(string? input)
        {
            return (input ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
