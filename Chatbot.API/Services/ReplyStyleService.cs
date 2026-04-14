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
                var reason = reasonFactory(item);
                sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): {reason}");
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
            var brand = intent.Brand ?? profile.PreferredBrand;
            var category = !string.IsNullOrWhiteSpace(intent.Category)
                ? intent.Category
                : effectiveCategory;

            bool hasBudget =
                intent.TargetPrice.HasValue ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue;

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
                sb.AppendLine($"Hiện mình nghiêng về {top.Ten} ({top.Gia:N0} VNĐ), vì {reasonFactory(top)}.");
                sb.AppendLine();
                sb.AppendLine("Bạn có thể lọc tiếp thêm một chút nữa như đổi hãng, siết giá hoặc thêm tiêu chí sử dụng.");

                return sb.ToString().Trim();
            }

            sb.AppendLine($"Nếu chọn trong nhóm này thì mình nghiêng hơn về {top.Ten} ({top.Gia:N0} VNĐ), vì {reasonFactory(top)}.");

            if (backups.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Pick(
                    "Bạn vẫn có thể cân nhắc thêm các phương án sau:",
                    "Ngoài ra bạn có thể xem thêm các mẫu này:",
                    "Nếu muốn tham khảo thêm thì còn các phương án này:"
                ));

                foreach (var item in backups)
                {
                    sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): {reasonFactory(item)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Bạn có thể lọc tiếp thêm một chút nữa như đổi hãng, siết giá hoặc thêm tiêu chí sử dụng.");

            return sb.ToString().Trim();
        }

        public string BuildRefinementNoMatchReply(ParsedIntent intent)
        {
            // 1. Ưu tiên brand + category + budget trước
            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                !string.IsNullOrWhiteSpace(intent.Category) &&
                (intent.PriceMax.HasValue || intent.PriceMin.HasValue || intent.TargetPrice.HasValue))
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

            // 2. Exclude brand/category
            if (intent.ExcludedBrands.Any())
            {
                return Pick(
                    $"Trong nhóm đang xét, sau khi bỏ {string.Join(", ", intent.ExcludedBrands)} thì hiện chưa còn mẫu nào phù hợp.",
                    $"Sau khi loại {string.Join(", ", intent.ExcludedBrands)} khỏi nhóm hiện tại thì chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu bỏ {string.Join(", ", intent.ExcludedBrands)} thì hiện nhóm này không còn mẫu nào phù hợp nữa."
                );
            }

            if (intent.ExcludedCategories.Any())
            {
                return Pick(
                    $"Trong nhóm đang xét, sau khi bỏ {string.Join(", ", intent.ExcludedCategories)} thì hiện chưa còn mẫu nào phù hợp.",
                    $"Sau khi loại nhóm {string.Join(", ", intent.ExcludedCategories)} thì hiện chưa còn mẫu nào thật sự phù hợp.",
                    $"Nếu bỏ nhóm {string.Join(", ", intent.ExcludedCategories)} thì hiện không còn mẫu nào phù hợp nữa."
                );
            }

            // 3. Brand / category riêng lẻ
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

            // 4. Giá
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

            if (intent.FilterType == PriceFilterType.Range &&
                intent.PriceMin.HasValue &&
                intent.PriceMax.HasValue)
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
                "Sau khi lọc tiếp theo tiêu chí mới thì hiện nhóm này chưa còn mẫu nào thật sự phù hợp.",
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
                sb.AppendLine($"- {item.Ten} ({item.Gia:N0} VNĐ): {reasonFactory(item)}");
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
            bool hasBudget =
                intent.TargetPrice.HasValue ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue;

            bool hasBrandOrCategory =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category);

            // Trường hợp chỉ có 1 mẫu
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

                return Pick(
                    "Mình thấy có 1 mẫu khá hợp với nhu cầu bạn đang nói tới:",
                    "Theo những gì bạn đang ưu tiên thì mình nghiêng về mẫu này:",
                    "Hiện mình thấy có 1 mẫu hợp hơn cả cho nhu cầu này:"
                );
            }

            // Trường hợp nhiều mẫu
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
                    $"Trong tầm bạn đang cân nhắc, mình thấy {count} mẫu khá đáng chú ý:",
                    $"Với mức giá này, mình nghiêng về {count} mẫu sau:",
                    $"Nếu bám theo ngân sách này thì đây là vài mẫu đáng xem trước:"
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

            return Pick(
                "Bạn có thể lọc tiếp thêm theo mức giá, nhu cầu đi lại hoặc các tiêu chí như cốp rộng, dễ chống chân, tiết kiệm xăng.",
                "Nếu muốn, mình có thể siết thêm theo giá, loại xe hoặc tiêu chí sử dụng để danh sách gọn hơn.",
                "Bạn nói thêm một tiêu chí nhỏ nữa là mình có thể lọc tiếp sát hơn."
            );
        }

        private static string BuildRefinementIntro(ParsedIntent intent, string message, int count)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();
            if (intent.ExcludedBrands.Any())
            {
                var excluded = string.Join(", ", intent.ExcludedBrands);

                if (count <= 1)
                {
                    return Pick(
                        $"Sau khi bỏ {excluded} thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Nếu loại {excluded} khỏi nhóm đang xét thì hiện có 1 mẫu nổi bật hơn cả:",
                        $"Sau khi bỏ {excluded} thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }

                return Pick(
                    $"Sau khi bỏ {excluded} thì mình thấy các mẫu này đáng cân nhắc hơn:",
                    $"Nếu loại {excluded} khỏi nhóm hiện tại thì mình nghiêng về các mẫu này:",
                    $"Sau khi bỏ {excluded} thì các mẫu này nổi bật hơn:"
                );
            }
            if (count <= 1)
            {
                if (text.Contains("cốp rộng") || text.Contains("cop rong"))
                {
                    return Pick(
                        "Nếu ưu tiên cốp rộng trong nhóm đang xét thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy tiêu chí cốp rộng làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu bạn ưu tiên mang đồ tiện hơn thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }

                if (text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
                    text.Contains("yên thấp") || text.Contains("yen thap"))
                {
                    return Pick(
                        "Nếu ưu tiên dễ chống chân hơn thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy tiêu chí dễ làm quen làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu cần dễ chống chân hơn thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }

                if (text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang"))
                {
                    return Pick(
                        "Nếu ưu tiên tiết kiệm xăng hơn thì hiện mình nghiêng nhất về mẫu sau:",
                        "Nếu lấy tiêu chí tiết kiệm xăng làm chính thì hiện có 1 mẫu nổi bật hơn cả:",
                        "Nếu muốn tối ưu chi phí đi lại hơn thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }

                if (!string.IsNullOrWhiteSpace(intent.Brand))
                {
                    return Pick(
                        $"Nếu tiếp tục giữ hướng {intent.Brand} thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Trong nhóm {intent.Brand} đang xét, hiện có 1 mẫu nổi bật hơn cả:",
                        $"Nếu bám theo hãng {intent.Brand} thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }

                if (!string.IsNullOrWhiteSpace(intent.Category))
                {
                    return Pick(
                        $"Nếu tiếp tục lọc theo {intent.Category} thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Trong nhóm {intent.Category} đang xét, hiện có 1 mẫu nổi bật hơn cả:",
                        $"Nếu bám theo {intent.Category} thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }

                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return Pick(
                        $"Nếu siết xuống dưới {intent.PriceMax.Value:N0} VNĐ thì hiện mình nghiêng nhất về mẫu sau:",
                        $"Trong mức dưới {intent.PriceMax.Value:N0} VNĐ, hiện có 1 mẫu nổi bật hơn cả:",
                        $"Nếu bám theo mức giá này thì hiện mẫu này là phương án đáng xem nhất:"
                    );
                }
            }

            // 1. Ưu tiên refine mềm trước
            if (text.Contains("cốp rộng") || text.Contains("cop rong"))
            {
                return Pick(
                    "Nếu ưu tiên cốp rộng hơn trong nhóm đang xét, mình thấy các mẫu này đáng cân nhắc hơn:",
                    "Nếu lấy tiêu chí cốp rộng làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu bạn ưu tiên chở đồ tiện hơn thì các mẫu này nổi bật hơn:"
                );
            }

            if (text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
                text.Contains("yên thấp") || text.Contains("yen thap"))
            {
                return Pick(
                    "Nếu ưu tiên dễ chống chân hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:",
                    "Nếu ưu tiên yên thấp và dễ làm quen hơn thì mình nghiêng về các mẫu này:",
                    "Nếu cần dễ chống chân hơn thì các mẫu này đáng xem hơn:"
                );
            }

            if (text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang"))
            {
                return Pick(
                    "Nếu ưu tiên tiết kiệm xăng hơn trong nhóm đang xét, mình thấy các mẫu này đáng cân nhắc hơn:",
                    "Nếu lấy tiêu chí tiết kiệm xăng làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu muốn tối ưu chi phí đi lại hơn thì các mẫu này nổi bật hơn:"
                );
            }

            if (text.Contains("đi làm") || text.Contains("di lam"))
            {
                return Pick(
                    "Nếu ưu tiên đi làm hằng ngày hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:",
                    "Nếu lấy nhu cầu đi làm làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu cần một mẫu hợp đi làm hằng ngày hơn thì các mẫu này nổi bật hơn:"
                );
            }

            if (text.Contains("đi học") || text.Contains("di hoc") ||
                text.Contains("sinh viên") || text.Contains("sinh vien"))
            {
                return Pick(
                    "Nếu ưu tiên đi học hằng ngày hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:",
                    "Nếu lấy nhu cầu đi học làm chính thì mình nghiêng về các mẫu này:",
                    "Nếu cần một mẫu dễ hợp với nhu cầu đi học hơn thì các mẫu này đáng xem hơn:"
                );
            }

            // 2. Sau đó mới fallback sang brand/category
            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                return Pick(
                    $"Nếu vẫn giữ nhu cầu trước đó và ưu tiên thêm {intent.Brand}, mình thấy các mẫu này khá đáng chú ý:",
                    $"Nếu vẫn giữ hướng cũ nhưng chuyển sang {intent.Brand}, mình thấy các mẫu này hợp hơn:",
                    $"Nếu giữ nhu cầu trước đó và lọc sang {intent.Brand}, mình nghiêng về các mẫu này:"
                );
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                return Pick(
                    $"Nếu giữ nhu cầu trước đó và lọc thêm theo {intent.Category}, mình thấy các mẫu này phù hợp hơn:",
                    $"Nếu thu hẹp sang nhóm {intent.Category}, mình thấy các mẫu này đáng cân nhắc hơn:",
                    $"Nếu tiếp tục lọc theo {intent.Category}, mình thấy các mẫu này nổi bật hơn:"
                );
            }

            if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
            {
                return Pick(
                    $"Nếu lọc hẹp hơn theo mức giá dưới {intent.PriceMax.Value:N0} VNĐ, hiện mình thấy các mẫu này hợp hơn:",
                    $"Nếu siết lại xuống dưới {intent.PriceMax.Value:N0} VNĐ thì mình nghiêng về các mẫu này:",
                    $"Nếu bám theo mức dưới {intent.PriceMax.Value:N0} VNĐ thì các mẫu này đáng xem hơn:"
                );
            }

            if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return Pick(
                    $"Nếu lọc hẹp hơn theo khoảng giá từ {intent.PriceMin.Value:N0} đến {intent.PriceMax.Value:N0} VNĐ, hiện mình thấy các mẫu này hợp hơn:",
                    $"Nếu siết lại trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ thì mình nghiêng về các mẫu này:",
                    $"Trong khoảng giá bạn vừa thu hẹp lại, mình thấy các mẫu này nổi bật hơn:"
                );
            }

            return Pick(
                "Nếu lọc tiếp từ nhóm trước thì hiện mình thấy các mẫu này phù hợp hơn:",
                "Nếu thu hẹp thêm từ nhóm đang xét thì mình nghiêng về các mẫu này:",
                "Sau khi lọc tiếp theo tiêu chí mới, mình thấy các mẫu này nổi bật hơn:"
            );
        }

        private static string BuildSearchIntro(
     int count,
     string? brand,
     string? category,
     decimal? minPrice,
     decimal? maxPrice)
        {
            var filterText = BuildFilterText(brand, category, minPrice, maxPrice);

            if (count == 1)
            {
                return Pick(
                    $"Mình lọc theo {filterText} và thấy có 1 mẫu khá phù hợp:",
                    $"Theo bộ lọc {filterText}, hiện có 1 mẫu bạn có thể tham khảo:",
                    $"Mình vừa rà theo {filterText} và thấy 1 mẫu nổi bật hơn cả:"
                );
            }

            return Pick(
                $"Mình lọc nhanh theo {filterText} và thấy {count} mẫu khá đáng xem:",
                $"Theo bộ lọc {filterText}, hiện có {count} mẫu bạn có thể tham khảo:",
                $"Mình vừa rà theo {filterText} và thấy {count} mẫu nổi bật:"
            );
        }
        private static string BuildSearchHint(string? brand, string? category)
        {
            if (string.IsNullOrWhiteSpace(brand))
            {
                return "Bạn có thể lọc tiếp theo hãng, loại xe hoặc mức giá sát hơn.";
            }

            if (string.IsNullOrWhiteSpace(category))
            {
                return $"Nếu muốn, mình có thể lọc tiếp riêng trong hãng {brand} theo xe ga, xe số hoặc côn tay.";
            }

            return "Bạn có thể nói thêm một tiêu chí nhỏ như cốp rộng, tiết kiệm xăng hoặc dễ chống chân để mình lọc tiếp.";
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
    }
}