using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace Chatbot.API.Services
{
    public class ProductNameResolverService : IProductNameResolverService
    {
        private const string CacheKey = "product_name_resolver_all_products";

        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ProductNameResolverService> _logger;

        public ProductNameResolverService(
            IWebBanXeMayToolClient toolClient,
            IMemoryCache cache,
            ILogger<ProductNameResolverService> logger)
        {
            _toolClient = toolClient;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<string>> ResolveMentionedProductNamesAsync(string text, int take = 5)
        {
            var normalizedText = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
                return new List<string>();

            if (LooksLikeInventoryListSearch(normalizedText))
                return new List<string>();

            var products = await GetAllProductsAsync();
            if (products.Count == 0)
                return new List<string>();

            var scored = products
                .Select(p => new
                {
                    Product = p,
                    Score = ScoreProduct(normalizedText, p)
                })
                .Where(x => x.Score >= 80)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Product.Gia)
                .ToList();

            if (scored.Count == 0)
                return new List<string>();

            var bestScore = scored[0].Score;

            return scored
                .Where(x => x.Score >= bestScore - 10)
                .Take(take)
                .Select(x => x.Product.Ten)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        private async Task<List<ProductSummaryDto>> GetAllProductsAsync()
        {
            if (_cache.TryGetValue(CacheKey, out List<ProductSummaryDto>? cached) && cached != null)
                return cached;

            var result = await _toolClient.GetProductsByFiltersAsync(
                brand: null,
                minPrice: null,
                maxPrice: null,
                category: null,
                take: 200);

            var products = result?.Items?
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList() ?? new List<ProductSummaryDto>();

            _cache.Set(CacheKey, products, TimeSpan.FromMinutes(10));

            return products;
        }

        private static int ScoreProduct(string text, ProductSummaryDto product)
        {
            var name = Normalize(product.Ten);
            var brand = Normalize(product.ThuongHieu);

            if (string.IsNullOrWhiteSpace(name))
                return 0;

            if (ContainsWholePhrase(text, name))
                return 100;

            var nameWithoutBrand = name;
            if (!string.IsNullOrWhiteSpace(brand))
                nameWithoutBrand = nameWithoutBrand.Replace(brand, "").Trim();

            if (!string.IsNullOrWhiteSpace(nameWithoutBrand) &&
                ContainsWholePhrase(text, nameWithoutBrand))
                return 90;

            var nameTokens = name
     .Split(' ', StringSplitOptions.RemoveEmptyEntries)
     .Where(IsSafeProductToken)
     .Distinct()
     .ToList();

            var textTokens = text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 2)
                .Distinct()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchedTokens = nameTokens.Count(t => textTokens.Contains(t));

            if (matchedTokens >= 2)
                return 60 + matchedTokens;

            if (matchedTokens == 1 && !string.IsNullOrWhiteSpace(brand) && textTokens.Contains(brand))
                return 50;

            return 0;
        }

        private static bool IsSafeProductToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            token = token.Trim().ToLowerInvariant();

            if (token.Length <= 2)
                return false;

            if (token is "honda" or "yamaha" or "suzuki" or "sym" or "piaggio")
                return false;

            return true;
        }
        private static bool ContainsWholePhrase(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
                return false;

            return Regex.IsMatch(
                text,
                $@"(?<!\p{{L}}|\p{{N}}){Regex.Escape(phrase)}(?!\p{{L}}|\p{{N}})",
                RegexOptions.IgnoreCase);
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
        private static bool LooksLikeInventoryListSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasListIntent =
                text.Contains("dua ra") ||
                text.Contains("liet ke") ||
                text.Contains("danh sach") ||
                text.Contains("shop co") ||
                text.Contains("cua hang co") ||
                text.Contains("co nhung xe nao") ||
                text.Contains("tat ca xe") ||
                text.Contains("toan bo xe");

            bool hasRangeOrInventorySignal =
                text.Contains("tu ") ||
                text.Contains("den ") ||
                text.Contains("khoang") ||
                text.Contains("trieu") ||
                text.Contains("shop") ||
                text.Contains("cua hang");

            return hasListIntent && hasRangeOrInventorySignal;
        }
    }
}