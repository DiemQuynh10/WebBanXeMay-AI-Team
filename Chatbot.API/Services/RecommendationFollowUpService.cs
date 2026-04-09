using System.Text;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class RecommendationFollowUpService : IRecommendationFollowUpService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<RecommendationFollowUpService> _logger;

        public RecommendationFollowUpService(
            IWebBanXeMayToolClient toolClient,
            IProductRecommendationService productRecommendationService,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<RecommendationFollowUpService> logger)
        {
            _toolClient = toolClient;
            _productRecommendationService = productRecommendationService;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }

        public async Task<ChatResponse?> HandleAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            if (!profile.HasActiveRecommendationContext)
            {
                _logger.LogInformation(
                    "Follow-up rerank skipped because recommendation context is inactive. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }
            if (profile.LastRecommendedProducts == null || profile.LastRecommendedProducts.Count == 0)
            {
                _logger.LogInformation(
                    "Follow-up rerank skipped because no last recommended products. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }

            var allowedNames = profile.LastRecommendedProducts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Follow-up rerank started. ConversationId: {ConversationId}, AllowedProducts: {AllowedProducts}",
                conversationId,
                string.Join(", ", allowedNames));

            var products = new List<ProductSummaryDto>();

            foreach (var name in allowedNames)
            {
                var result = await _toolClient.SearchProductsAsync(name, 3);

                var matched = result?.Items?
                    .OrderByDescending(x => string.Equals(x.Ten, name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (matched != null)
                {
                    products.Add(matched);
                }
                else
                {
                    _logger.LogInformation(
                        "Follow-up rerank could not find product by name. ConversationId: {ConversationId}, ProductName: {ProductName}",
                        conversationId,
                        name);
                }
            }

            products = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Ten) && allowedNames.Contains(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            _logger.LogInformation(
                "Follow-up rerank candidates after strict filter. ConversationId: {ConversationId}, Count: {Count}, Products: {Products}",
                conversationId,
                products.Count,
                string.Join(", ", products.Select(x => x.Ten)));

            if (products.Count == 0)
            {
                return null;
            }

            var reranked = _productRecommendationService.RankProducts(
                products,
                intent,
                profile,
                normalizedMessage,
                take: Math.Min(4, products.Count));

            if (reranked == null || reranked.Count == 0)
            {
                return null;
            }

            var reply = BuildReply(reranked, intent);
            await _conversationPreferenceService.SetRecommendedProductsAsync(
    conversationId,
    reranked,
    "followup");
            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(reranked, 4)
            };
        }

        private static string BuildReply(IReadOnlyList<ProductSummaryDto> items, ParsedIntent intent)
        {
            var top = items[0];
            var backups = items.Skip(1).Take(2).ToList();

            var focusLabel = intent.ComparisonFeature switch
            {
                "storage" => "cốp rộng",
                "low_seat" => "dễ chống chân",
                "fuel_saving" => "tiết kiệm xăng",
                "female_fit" => "hợp nữ",
                "work_fit" => "thực dụng / đi làm hằng ngày",
                "school_fit" => "đi học / sinh viên",
                "ride_comfort" => "đi êm",
                _ => null
            };

            var topReason = BuildTopReason(top, intent.ComparisonFeature);

            var intro = focusLabel switch
            {
                "cốp rộng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                "dễ chống chân" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                "tiết kiệm xăng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                "hợp nữ" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                "thực dụng / đi làm hằng ngày" => $"Nếu chọn trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                "đi học / sinh viên" => $"Nếu xét trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                "đi êm" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}.",
                _ => $"Trong các mẫu vừa rồi, mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ){topReason}."
            };

            var sb = new StringBuilder();
            sb.AppendLine(intro);

            if (backups.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Các phương án phụ bạn vẫn có thể cân nhắc thêm:");
                foreach (var item in backups)
                {
                    sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ)");
                }
            }

            return sb.ToString().Trim();
        }

        private static string BuildTopReason(ProductSummaryDto item, string? feature)
        {
            var name = item.Ten ?? string.Empty;

            return feature switch
            {
                "storage" when name.Contains("Freego", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này thiên về nhóm tiện ích và khá hợp nếu bạn hay mang đồ",
                "storage" when name.Contains("Lead", StringComparison.OrdinalIgnoreCase)
                    => " vì đây là mẫu nổi bật hơn về hướng tiện dụng và chứa đồ",
                "storage" when name.Contains("Latte", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này cân bằng khá tốt giữa dáng xe và tính tiện dụng",

                "low_seat" when name.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                    => " vì dáng xe gọn và khá dễ làm quen",
                "low_seat" when name.Contains("Zip", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này thiên về nhóm nhỏ gọn, dễ chống chân hơn",
                "low_seat" when name.Contains("Latte", StringComparison.OrdinalIgnoreCase)
                    => " vì đi theo hướng mềm, dễ đi và khá thân thiện với người vóc dáng nhỏ",

                "fuel_saving" when name.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                    => " vì đây là mẫu khá cân bằng giữa dễ đi và chi phí sử dụng",
                "fuel_saving" when name.Contains("Wave", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này thiên về chi phí dùng lâu dài thấp",
                "fuel_saving" when name.Contains("Future", StringComparison.OrdinalIgnoreCase)
                    => " vì khá hợp nếu bạn ưu tiên sự bền bỉ và thực dụng",

                "female_fit" when name.Contains("Latte", StringComparison.OrdinalIgnoreCase)
                    => " vì dáng xe mềm và hợp hơn nếu bạn thích phong cách nữ tính",
                "female_fit" when name.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này gọn, dễ đi và dễ làm quen",
                "female_fit" when name.Contains("Grande", StringComparison.OrdinalIgnoreCase)
                    => " vì thiên về phong cách mềm mại và thanh lịch hơn",
                "female_fit" when name.Contains("Attila", StringComparison.OrdinalIgnoreCase)
=> " vì mẫu này thiên về kiểu dáng nữ tính và mềm mại hơn",
                "female_fit" when name.Contains("Venus", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này thiên về kiểu dáng nữ tính và thanh lịch hơn",

                "work_fit" when name.Contains("Air Blade", StringComparison.OrdinalIgnoreCase)
                    => " vì đi theo hướng đầm xe và đi làm hằng ngày khá ổn",
                "work_fit" when name.Contains("Freego", StringComparison.OrdinalIgnoreCase)
                    => " vì khá thực dụng cho nhu cầu đi lại mỗi ngày",
                "work_fit" when name.Contains("Future", StringComparison.OrdinalIgnoreCase)
                    => " vì thiên về độ bền và tính thực dụng",

                "school_fit" when name.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                    => " vì gọn, dễ đi và khá hợp nhu cầu đi học",
                "school_fit" when name.Contains("Wave", StringComparison.OrdinalIgnoreCase)
                    => " vì chi phí sử dụng thường mềm hơn",
                "school_fit" when name.Contains("Sirius", StringComparison.OrdinalIgnoreCase)
                    => " vì đây là mẫu phổ thông dễ dùng hằng ngày",
                "ride_comfort" when name.Contains("Latte", StringComparison.OrdinalIgnoreCase)
    => " vì mẫu này thiên về cảm giác lái nhẹ nhàng và đi phố khá êm",
                "ride_comfort" when name.Contains("Grande", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này thiên về cảm giác vận hành êm và khá thư thái khi đi phố",
                "ride_comfort" when name.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                    => " vì mẫu này khá dễ đi và cho cảm giác vận hành nhẹ nhàng",
                _ => string.Empty
            };
        }
    }
}