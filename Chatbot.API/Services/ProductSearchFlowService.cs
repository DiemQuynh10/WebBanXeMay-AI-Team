using System.Text;
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

        public ProductSearchFlowService(
    IWebBanXeMayToolClient toolClient,
    IConversationPreferenceService conversationPreferenceService,
    IProductRecommendationService productRecommendationService,
    ILogger<ProductSearchFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _productRecommendationService = productRecommendationService;
            _logger = logger;
        }
        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            bool hasSearchSignals =
    intent.IsProductSearch ||
    intent.PriceMin.HasValue ||
    intent.PriceMax.HasValue ||
    intent.TargetPrice.HasValue ||
    !string.IsNullOrWhiteSpace(intent.Brand) ||
    !string.IsNullOrWhiteSpace(intent.Category);

            if (!hasSearchSignals)
            {
                return null;
            }

            var effectiveBrand = intent.Brand ?? profile.PreferredBrand;
            var effectiveCategory = intent.Category ?? profile.PreferredCategory;
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

            if (items.Count == 0)
            {
                var nearMatches = await FindNearMatchProductsAsync(
                    effectiveBrand,
                    effectiveCategory,
                    effectiveMinPrice,
                    effectiveMaxPrice,
                    normalizedMessage);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                    Reply = BuildEmptyReply(intent, effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice, nearMatches),
                    Products = nearMatches.Take(4).Select(MapToCard).ToList()
                };
            }

            await SaveSearchContextAsync(conversationId, items, intent);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                Reply = BuildSearchReply(
    items,
    intent,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    _productRecommendationService),
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
            }
            else
            {
                result = await _toolClient.SearchProductsAsync(normalizedMessage, 5);
            }

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

        private static string BuildEmptyReply(
    ParsedIntent intent,
    string? brand,
    string? category,
    decimal? minPrice,
    decimal? maxPrice,
    List<ProductSummaryDto> nearMatches)
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

            var filterText = parts.Count == 0
                ? "tiêu chí này"
                : string.Join(", ", parts);

            if (nearMatches == null || nearMatches.Count == 0)
            {
                return $"Hiện mình chưa tìm thấy mẫu xe nào khớp hoàn toàn với {filterText} trong dữ liệu.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Hiện chưa có mẫu nào khớp hoàn toàn với {filterText} trong dữ liệu.");
            sb.AppendLine("Tuy vậy, nếu nới điều kiện một chút, bạn có thể tham khảo:");
            sb.AppendLine();

            foreach (var item in nearMatches.Take(3))
            {
                sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ)");
            }

            return sb.ToString().Trim();
        }
        private static string BuildSearchReply(
    List<ProductSummaryDto> items,
    ParsedIntent intent,
    string? brand,
    string? category,
    decimal? minPrice,
    decimal? maxPrice,
    IProductRecommendationService productRecommendationService)
        {
            var shown = items.Take(5).ToList();
            var intro = BuildIntro(shown.Count, brand, category, minPrice, maxPrice);

            var sb = new StringBuilder();
            sb.AppendLine(intro);
            sb.AppendLine();

            foreach (var item in shown)
            {
                var reason = BuildShortReason(item, intent, brand, category, productRecommendationService);
                sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ) - {reason}");
            }

            if (items.Count > shown.Count)
            {
                sb.AppendLine();
                sb.AppendLine($"Hiện mình đang thấy tổng cộng khoảng **{items.Count}** mẫu phù hợp trong dữ liệu, trên đây là các mẫu nổi bật trước.");
            }

            sb.AppendLine();
            sb.AppendLine("Bạn có thể lọc tiếp theo hãng, loại xe hoặc mức giá sát hơn.");

            return sb.ToString().Trim();
        }

        private static string BuildIntro(
            int count,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(category))
                parts.Add(category);

            if (!string.IsNullOrWhiteSpace(brand))
                parts.Add(brand);

            if (maxPrice.HasValue && !minPrice.HasValue)
                parts.Add($"dưới {maxPrice.Value:N0} VNĐ");
            else if (minPrice.HasValue && maxPrice.HasValue)
                parts.Add($"từ {minPrice.Value:N0} đến {maxPrice.Value:N0} VNĐ");
            else if (minPrice.HasValue)
                parts.Add($"từ {minPrice.Value:N0} VNĐ trở lên");

            if (parts.Count == 0)
                return $"Mình tìm được {count} mẫu xe phù hợp:";

            return $"Mình tìm được {count} mẫu khá phù hợp cho bộ lọc {string.Join(", ", parts)}:";
        }

        private static string BuildShortReason(
    ProductSummaryDto item,
    ParsedIntent intent,
    string? brand,
    string? category,
    IProductRecommendationService productRecommendationService)
        {
            var reasons = new List<string>();

            if (!string.IsNullOrWhiteSpace(category) &&
                (item.Loai ?? string.Empty).Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"đúng nhóm {item.Loai.ToLowerInvariant()}");
            }

            if (!string.IsNullOrWhiteSpace(brand) &&
                (item.ThuongHieu ?? string.Empty).Equals(brand, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"đúng hãng {brand}");
            }

            var mainReason = productRecommendationService.BuildMainReason(item, intent);
            if (!string.IsNullOrWhiteSpace(mainReason) &&
                mainReason != "là một phương án khá cân bằng trong nhóm đang lọc")
            {
                reasons.Add(mainReason);
            }

            if (item.SoLuong > 0)
            {
                reasons.Add($"còn {item.SoLuong} chiếc");
            }
            else
            {
                reasons.Add("đang hết hàng");
            }

            return string.Join(", ", reasons.Distinct().Take(3));
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
                ThuongHieu = product.ThuongHieu,
                Loai = product.Loai
            };
        }
    }
}