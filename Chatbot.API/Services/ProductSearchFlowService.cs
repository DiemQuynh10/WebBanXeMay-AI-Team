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
        private readonly ILogger<ProductSearchFlowService> _logger;

        public ProductSearchFlowService(
            IWebBanXeMayToolClient toolClient,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<ProductSearchFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }

        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            if (!intent.IsProductSearch)
            {
                return null;
            }

            var effectiveBrand = intent.Brand ?? profile.PreferredBrand;
            var effectiveCategory = intent.Category ?? profile.PreferredCategory;
            var effectiveMinPrice = intent.PriceMin ?? profile.PriceMin;
            var effectiveMaxPrice = intent.PriceMax ?? profile.PriceMax;

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
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                    Reply = BuildEmptyReply(intent, effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice)
                };
            }

            await SaveSearchContextAsync(conversationId, items, intent);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                Reply = BuildSearchReply(items, intent, effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
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

            if (parts.Count == 0)
            {
                return "Mình chưa tìm thấy sản phẩm phù hợp trong dữ liệu hiện tại.";
            }

            return $"Mình chưa tìm thấy mẫu xe phù hợp với bộ lọc: {string.Join(", ", parts)}.";
        }

        private static string BuildSearchReply(
            List<ProductSummaryDto> items,
            ParsedIntent intent,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice)
        {
            var shown = items.Take(5).ToList();

            var intro = BuildIntro(shown.Count, brand, category, minPrice, maxPrice);

            var sb = new StringBuilder();
            sb.AppendLine(intro);
            sb.AppendLine();

            foreach (var item in shown)
            {
                sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ) - {BuildShortReason(item, intent, brand, category)}");
            }

            if (items.Count > shown.Count)
            {
                sb.AppendLine();
                sb.AppendLine($"Hiện mình đang thấy tổng cộng khoảng **{items.Count}** mẫu phù hợp trong dữ liệu, trên đây là các mẫu nổi bật trước.");
            }

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
            string? category)
        {
            var reasons = new List<string>();

            if (!string.IsNullOrWhiteSpace(category) &&
                item.Loai.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"đúng nhóm {item.Loai.ToLowerInvariant()}");
            }

            if (!string.IsNullOrWhiteSpace(brand) &&
                item.ThuongHieu.Equals(brand, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"đúng hãng {brand}");
            }

            if (item.SoLuong > 0)
            {
                reasons.Add($"còn {item.SoLuong} chiếc");
            }
            else
            {
                reasons.Add("đang hết hàng");
            }

            return string.Join(", ", reasons.Take(2));
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