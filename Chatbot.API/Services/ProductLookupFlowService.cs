using System.Text;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ProductLookupFlowService : IProductLookupFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<ProductLookupFlowService> _logger;

        public ProductLookupFlowService(
            IWebBanXeMayToolClient toolClient,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<ProductLookupFlowService> logger)
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
            if (!intent.IsDirectProductLookup || !intent.MentionedProducts.Any())
            {
                return null;
            }

            var resolvedProduct = await ResolveBestProductAsync(intent, profile);

            if (resolvedProduct == null)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.SearchProducts,
                    Reply = "Mình chưa tìm thấy mẫu xe đúng với tên bạn đang hỏi trong dữ liệu hiện tại. Bạn thử ghi rõ tên mẫu hơn giúp mình nhé."
                };
            }

            await SaveLookupContextAsync(conversationId, resolvedProduct);

            var reply = await BuildLookupReplyAsync(resolvedProduct, intent);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ToolNames.SearchProducts,
                Reply = reply,
                Products = new List<ChatProductCard>
                {
                    MapToCard(resolvedProduct)
                }
            };
        }

        private async Task<ProductSummaryDto?> ResolveBestProductAsync(
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var candidateNames = new List<string>();

            if (intent.MentionedProducts.Any())
                candidateNames.AddRange(intent.MentionedProducts);

            if (candidateNames.Count == 0 && !string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                candidateNames.Add(profile.LastLookupProductName!);

            foreach (var candidate in candidateNames
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var searchResult = await _toolClient.SearchProductsAsync(candidate, 5);

                var bestMatch = searchResult?.Items?
                    .OrderByDescending(x => string.Equals(x.Ten, candidate, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(candidate, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.SoLuong)
                    .FirstOrDefault();

                if (bestMatch != null)
                {
                    _logger.LogInformation(
                        "Product lookup resolved product. Candidate: {Candidate}, Resolved: {ResolvedName}, Id: {Id}",
                        candidate,
                        bestMatch.Ten,
                        bestMatch.Id);

                    return bestMatch;
                }
            }

            return null;
        }

        private async Task SaveLookupContextAsync(string conversationId, ProductSummaryDto product)
        {
            var latestProfile = await _conversationPreferenceService.GetAsync(conversationId);
            latestProfile.LastLookupProductId = product.Id;
            latestProfile.LastLookupProductName = product.Ten;
            latestProfile.ActiveFlow = ChatFlowType.ProductLookup;
            latestProfile.UpdatedAtUtc = DateTime.UtcNow;
        }

        private async Task<string> BuildLookupReplyAsync(ProductSummaryDto product, ParsedIntent intent)
        {
            var lookupField = intent.LookupField?.Trim().ToLowerInvariant();

            switch (lookupField)
            {
                case "price":
                    return $"**{product.Ten}** hiện có giá khoảng **{product.Gia:N0} VNĐ**.";

                case "stock":
                    if (product.SoLuong > 0)
                    {
                        return $"**{product.Ten}** hiện vẫn còn hàng, số lượng trong hệ thống là **{product.SoLuong}** chiếc.";
                    }

                    return $"**{product.Ten}** hiện đang hết hàng trong dữ liệu hệ thống.";

                case "cc":
                    if (product.CC.HasValue)
                    {
                        return $"**{product.Ten}** có dung tích khoảng **{product.CC.Value} cc**.";
                    }

                    return $"Mình đã tìm thấy **{product.Ten}**, nhưng hiện dữ liệu chưa có thông tin chính xác về số cc.";

                case "detail":
                    return BuildDetailReply(product);

                default:
                    // fallback: thử lấy detail ngắn gọn
                    var detail = await _toolClient.GetProductDetailAsync(product.Id);
                    if (detail != null)
                    {
                        return BuildDetailReply(product);
                    }

                    return BuildDetailReply(product);
            }
        }

        private static string BuildDetailReply(ProductSummaryDto product)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"**{product.Ten}**");
            sb.AppendLine($"- Giá: **{product.Gia:N0} VNĐ**");
            sb.AppendLine($"- Hãng: {product.ThuongHieu}");
            sb.AppendLine($"- Loại xe: {product.Loai}");

            if (product.CC.HasValue)
            {
                sb.AppendLine($"- Dung tích: {product.CC.Value} cc");
            }

            if (product.SoLuong > 0)
            {
                sb.AppendLine($"- Tồn kho: {product.SoLuong} chiếc");
            }
            else
            {
                sb.AppendLine("- Tình trạng: hiện đang hết hàng");
            }

            return sb.ToString().Trim();
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