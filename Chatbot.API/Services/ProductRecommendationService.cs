using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ProductRecommendationService : IProductRecommendationService
    {
        public List<ProductSummaryDto> RankProducts(
            IEnumerable<ProductSummaryDto> products,
            ParsedIntent intent,
            string normalizedMessage,
            int take = 5)
        {
            var message = (normalizedMessage ?? string.Empty).ToLowerInvariant();
            var profile = BuildProfile(intent, message);

            var ranked = products
                .Select(p => new
                {
                    Product = p,
                    Score = CalculateScore(p, profile, message)
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Product.SoLuong)
                .ThenBy(x => x.Product.Gia)
                .Take(take)
                .Select(x => x.Product)
                .ToList();

            return ranked;
        }

        private static RecommendationProfile BuildProfile(ParsedIntent intent, string message)
        {
            return new RecommendationProfile
            {
                IsStudent = (intent.Target ?? string.Empty).Contains("sinh viên", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("sinh viên"),
                IsFemale = (intent.Target ?? string.Empty).Contains("nữ", StringComparison.OrdinalIgnoreCase)
                           || message.Contains("nữ"),
                IsMale = (intent.Target ?? string.Empty).Contains("nam", StringComparison.OrdinalIgnoreCase)
                         || message.Contains("nam"),
                ForSchool = message.Contains("đi học") || message.Contains("đi học"),
                ForWork = message.Contains("đi làm"),
                PreferredCategory = intent.Category
            };
        }

        private static int CalculateScore(ProductSummaryDto product, RecommendationProfile profile, string message)
        {
            var score = 0;

            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            var brand = (product.ThuongHieu ?? string.Empty).ToLowerInvariant();
            var category = (product.Loai ?? string.Empty).ToLowerInvariant();
            var price = product.Gia;
            var stock = product.SoLuong;

            if (!string.IsNullOrWhiteSpace(profile.PreferredCategory))
            {
                var preferred = profile.PreferredCategory.ToLowerInvariant();
                if (category.Contains(preferred.Replace("xe ", "")) || category.Contains(preferred))
                {
                    score += 40;
                }
            }

            if (stock > 0) score += 10;
            if (stock >= 5) score += 5;

            if (profile.IsStudent)
            {
                if (price <= 35_000_000m) score += 20;
                else if (price <= 45_000_000m) score += 10;

                if (name.Contains("vision")) score += 32;
                if (name.Contains("janus")) score += 28;
                if (name.Contains("freego")) score += 24;
                if (name.Contains("lead")) score += 20;
                if (name.Contains("latte")) score += 18;
                if (name.Contains("grande")) score += 16;

                if (brand.Contains("honda")) score += 10;
                if (brand.Contains("yamaha")) score += 8;

                if (name.Contains("elite 50")) score -= 24;
                if (name.Contains("50")) score -= 10;
            }

            if (profile.IsFemale)
            {
                if (name.Contains("vision")) score += 24;
                if (name.Contains("janus")) score += 20;
                if (name.Contains("lead")) score += 18;
                if (name.Contains("latte")) score += 16;
                if (name.Contains("grande")) score += 14;
                if (name.Contains("attila")) score += 10;

                if (name.Contains("shark mini")) score += 4;
                if (name.Contains("impulse")) score -= 12;
            }

            if (profile.ForSchool)
            {
                if (price <= 40_000_000m) score += 12;
                if (category.Contains("ga")) score += 10;
            }

            if (message.Contains("tiết kiệm xăng"))
            {
                if (name.Contains("vision")) score += 8;
                if (name.Contains("wave")) score += 8;
                if (name.Contains("janus")) score += 7;
                if (name.Contains("sirius")) score += 7;
            }

            if (message.Contains("cốp rộng"))
            {
                if (name.Contains("lead")) score += 12;
                if (name.Contains("vision")) score += 6;
            }

            return score;
        }
    }
}