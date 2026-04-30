using System.Text;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ProductSearchFlowService : IProductSearchFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly ILogger<ProductSearchFlowService> _logger;
        private readonly IReplyStyleService _replyStyleService;

        public ProductSearchFlowService(
     IWebBanXeMayToolClient toolClient,
     IConversationPreferenceService conversationPreferenceService,
     IProductRecommendationService productRecommendationService,
     IReplyStyleService replyStyleService,
     ILogger<ProductSearchFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _productRecommendationService = productRecommendationService;
            _replyStyleService = replyStyleService;
            _logger = logger;
        }
        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            static HashSet<string> BuildExcludedBrands(ParsedIntent currentIntent, CustomerPreferenceProfile currentProfile)
            {
                var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var brand in currentIntent.ExcludedBrands)
                {
                    if (!string.IsNullOrWhiteSpace(brand))
                        excluded.Add(brand.Trim());
                }

                foreach (var brand in currentProfile.ExcludedBrands)
                {
                    if (!string.IsNullOrWhiteSpace(brand))
                        excluded.Add(brand.Trim());
                }

                return excluded;
            }

            static HashSet<string> BuildExcludedCategories(ParsedIntent currentIntent, CustomerPreferenceProfile currentProfile)
            {
                var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var category in currentIntent.ExcludedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(category))
                        excluded.Add(category.Trim());
                }

                foreach (var category in currentProfile.ExcludedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(category))
                        excluded.Add(category.Trim());
                }

                return excluded;
            }

            static List<ProductSummaryDto> ApplyStrictExclusions(
                IEnumerable<ProductSummaryDto> products,
                HashSet<string> excludedBrands,
                HashSet<string> excludedCategories)
            {
                var filtered = products
                    .Where(x => x != null)
                    .Where(x => !excludedBrands.Contains(x.ThuongHieu?.Trim() ?? string.Empty))
                    .ToList();

                if (excludedCategories.Count == 0)
                    return filtered;

                return filtered
                    .Where(x => !excludedCategories.Contains(x.Loai?.Trim() ?? string.Empty))
                    .ToList();
            }

            var excludedBrands = BuildExcludedBrands(intent, profile);
            var excludedCategories = BuildExcludedCategories(intent, profile);
            var hasIntentBrandExclusions = intent.ExcludedBrands.Any();

            bool hasSearchSignals =
    intent.IsProductSearch ||
    intent.PriceMin.HasValue ||
    intent.PriceMax.HasValue ||
    intent.TargetPrice.HasValue ||
    !string.IsNullOrWhiteSpace(intent.Brand) ||
            !string.IsNullOrWhiteSpace(intent.Category) ||
            intent.ExcludedBrands.Any() ||
            intent.ExcludedCategories.Any() ||
            intent.ExcludedProducts.Any();

            if (!hasSearchSignals)
            {
                return null;
            }

            bool currentMessageHasCategory = MessageHasExplicitCategory(normalizedMessage);
            bool currentMessageHasBrand = MessageHasExplicitBrand(normalizedMessage);

            var effectiveBrand = !string.IsNullOrWhiteSpace(intent.Brand)
                ? intent.Brand
                : currentMessageHasCategory && !currentMessageHasBrand
                    ? null
                    : profile.PreferredBrand;

            var effectiveCategory = intent.Category ?? profile.PreferredCategory;

            if (ProductExclusionHelper.IsBrandExcludedByIntentOrProfile(effectiveBrand, intent, profile))
            {
                effectiveBrand = null;
            }

            if (ProductExclusionHelper.IsCategoryExcludedByIntentOrProfile(effectiveCategory, intent, profile))
            {
                effectiveCategory = null;
            }
            var effectiveMinPrice = intent.PriceMin ?? profile.PriceMin;
            var effectiveMaxPrice = intent.PriceMax ?? profile.PriceMax;
            _logger.LogInformation(
    "ProductSearch effective filters. ConversationId: {ConversationId}, Brand: {Brand}, Category: {Category}, MinPrice: {MinPrice}, MaxPrice: {MaxPrice}, IntentType: {IntentType}, FilterType: {FilterType}",
    conversationId,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    intent.IntentType,
    intent.FilterType);
            ProductSearchResponseDto? result;

            // Ưu tiên gọi đúng tool theo độ đặc hiệu
            if (!string.IsNullOrWhiteSpace(effectiveBrand) &&
                effectiveMaxPrice.HasValue &&
                !effectiveMinPrice.HasValue)
            {
                result = await _toolClient.GetProductsByBrandAndPriceAsync(
                    effectiveBrand,
                    effectiveMaxPrice.Value,
                    effectiveCategory,
                    10);
            }
            else if (!string.IsNullOrWhiteSpace(effectiveBrand) ||
                     !string.IsNullOrWhiteSpace(effectiveCategory) ||
                     effectiveMinPrice.HasValue ||
                     effectiveMaxPrice.HasValue)
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    effectiveBrand,
                    effectiveMinPrice,
                    effectiveMaxPrice,
                    effectiveCategory,
                    10);
            }
            else
            {
                result = await _toolClient.SearchProductsAsync(normalizedMessage, 10);
            }

            var items = result?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();
            items = ProductExclusionHelper.ApplyExclusions(items, intent, profile);
            items = ApplyStrictExclusions(items, excludedBrands, excludedCategories);

            if (items.Count == 0)
            {
                var nearMatches = new List<ProductSummaryDto>();

                if (!hasIntentBrandExclusions)
                {
                    nearMatches = await FindNearMatchProductsAsync(
                        effectiveBrand,
                        effectiveCategory,
                        effectiveMinPrice,
                        effectiveMaxPrice,
                        normalizedMessage);
                    nearMatches = ProductExclusionHelper.ApplyExclusions(nearMatches, intent, profile);
                    nearMatches = ApplyStrictExclusions(nearMatches, excludedBrands, excludedCategories);
                }

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                    Reply = _replyStyleService.BuildSearchEmptyReply(
    intent,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    nearMatches),
                    Products = nearMatches.Take(4).Select(MapToCard).ToList()
                };
            }

            await SaveSearchContextAsync(conversationId, items, intent);

            items = ApplyStrictExclusions(items, excludedBrands, excludedCategories);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                Reply = _replyStyleService.BuildSearchReply(
    items,
    intent,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    item => BuildShortReason(item, intent, effectiveBrand, effectiveCategory, _productRecommendationService)),
                Products = items.Take(5).Select(MapToCard).ToList()
            };
        }

        private async Task SaveSearchContextAsync(
            string conversationId,
            List<ProductSummaryDto> items,
            ParsedIntent intent)
        {
            var latestProfile = await _conversationPreferenceService.GetAsync(conversationId);

            latestProfile.LastSearchProductNames = items
                .Select(x => x.Ten)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            latestProfile.LastSearchProductIds = items
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            latestProfile.ActiveFlow = ChatFlowType.ProductSearch;
            latestProfile.UpdatedAtUtc = DateTime.UtcNow;
            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
    conversationId,
    items.Take(5).ToList());

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                items.Take(5).ToList(),
                "search");
        }
        private async Task<List<ProductSummaryDto>> FindNearMatchProductsAsync(
     string? brand,
     string? category,
     decimal? minPrice,
     decimal? maxPrice,
     string normalizedMessage)
        {
            decimal? relaxedMin = minPrice;
            decimal? relaxedMax = maxPrice;

            if (maxPrice.HasValue)
                relaxedMax = maxPrice.Value + 5_000_000m;

            if (minPrice.HasValue)
                relaxedMin = Math.Max(0, minPrice.Value - 5_000_000m);

            ProductSearchResponseDto? result;

            // Bước 1: giữ nguyên brand + category, chỉ nới giá
            if (!string.IsNullOrWhiteSpace(brand) ||
                !string.IsNullOrWhiteSpace(category) ||
                relaxedMin.HasValue ||
                relaxedMax.HasValue)
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand,
                    relaxedMin,
                    relaxedMax,
                    category,
                    5);

                var items = result?.Items?
                    .Where(x => x != null)
                    .Take(5)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (items.Count > 0)
                    return items;
            }

            // Bước 2: nếu có category mà quá chặt thì bỏ category, giữ brand + giá
            if (!string.IsNullOrWhiteSpace(category))
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand,
                    relaxedMin,
                    relaxedMax,
                    null,
                    5);

                var items = result?.Items?
                    .Where(x => x != null)
                    .Take(5)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (items.Count > 0)
                    return items;
            }

            // Bước 3: nếu vẫn không có mà có brand thì giữ brand, nới giá thêm
            if (!string.IsNullOrWhiteSpace(brand))
            {
                decimal? moreRelaxedMin = relaxedMin.HasValue
    ? Math.Max(0, relaxedMin.Value - 3_000_000m)
    : (decimal?)null;

                decimal? moreRelaxedMax = relaxedMax.HasValue
                    ? relaxedMax.Value + 3_000_000m
                    : (decimal?)null;
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand,
                    moreRelaxedMin,
                    moreRelaxedMax,
                    null,
                    5);

                var items = result?.Items?
                    .Where(x => x != null)
                    .Take(5)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (items.Count > 0)
                    return items;
            }

            // Bước 4: fallback cuối cùng
            result = await _toolClient.SearchProductsAsync(normalizedMessage, 5);

            return result?.Items?
                .Where(x => x != null)
                .Take(5)
                .ToList() ?? new List<ProductSummaryDto>();
        }
        private static string ResolveToolName(
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice)
        {
            if (!string.IsNullOrWhiteSpace(brand) && maxPrice.HasValue && !minPrice.HasValue)
            {
                return ToolNames.GetProductsByBrandAndPrice;
            }

            if (!string.IsNullOrWhiteSpace(brand) ||
                !string.IsNullOrWhiteSpace(category) ||
                minPrice.HasValue ||
                maxPrice.HasValue)
            {
                return ToolNames.GetProductsByFilters;
            }

            return ToolNames.SearchProducts;
        }

        private static string BuildShortReason(
     ProductSummaryDto item,
     ParsedIntent intent,
     string? brand,
     string? category,
     IProductRecommendationService productRecommendationService)
        {
            var name = item.Ten ?? string.Empty;
            var normalizedCategory = NormalizeCategory(category);

            if (normalizedCategory == "con tay")
            {
                return PickReasonByName(name,
                    "hợp nếu bạn thích xe côn tay thể thao và cảm giác lái chủ động",
                    "kiểu dáng cá tính, phù hợp với nhu cầu đi xe mạnh hơn",
                    "đáng cân nhắc nếu bạn muốn một mẫu côn tay trong tầm giá này");
            }

            if (normalizedCategory == "xe ga")
            {
                return PickReasonByName(name,
                    "tiện đi phố, dễ dùng và phù hợp đi lại hằng ngày",
                    "thoải mái khi chạy trong đô thị, không cần thao tác nhiều",
                    "gọn gàng, dễ sử dụng và hợp nhu cầu di chuyển thường xuyên");
            }

            if (normalizedCategory == "xe so")
            {
                return PickReasonByName(name,
                    "dễ đi, bền và chi phí sử dụng hợp lý",
                    "thực dụng, dễ bảo dưỡng và hợp đi lại hằng ngày",
                    "phù hợp nếu bạn muốn xe tiết kiệm, dễ dùng lâu dài");
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                return $"đáng cân nhắc nếu bạn đang ưu tiên hãng {brand}";
            }

            return "là mẫu khá sát với bộ lọc hiện tại";
        }
        private static string NormalizeCategory(string? category)
        {
            var text = NormalizeText(category);

            if (text.Contains("ga"))
                return "xe ga";

            if (text.Contains("so"))
                return "xe so";

            if (text.Contains("con"))
                return "con tay";

            return text;
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

        private static string PickReasonByName(string? productName, params string[] reasons)
        {
            if (reasons == null || reasons.Length == 0)
                return "là mẫu khá sát với bộ lọc hiện tại";

            var name = productName ?? string.Empty;
            var index = Math.Abs(name.GetHashCode()) % reasons.Length;

            return reasons[index];
        }
        private static bool MessageHasExplicitCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("xe so") ||
                   text.Contains("xe ga") ||
                   text.Contains("con tay") ||
                   text.Contains("tay ga");
        }

        private static bool MessageHasExplicitBrand(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("honda") ||
                   text.Contains("yamaha") ||
                   text.Contains("suzuki") ||
                   text.Contains("sym") ||
                   text.Contains("piaggio");
        }
        private static ChatProductCard MapToCard(ProductSummaryDto product)
        {
            return new ChatProductCard
            {
                Id = product.Id,
                Ten = product.Ten,
                Slug = product.Slug,
                Gia = product.Gia,
                SoLuong = product.SoLuong,
                CC = product.CC?.ToString(),
                ImageUrl = product.ImageUrl,
                ProductUrl = ChatProductCardMapper.BuildProductUrl(product),
                ThuongHieu = product.ThuongHieu,
                Loai = product.Loai
            };
        }
    }
}
