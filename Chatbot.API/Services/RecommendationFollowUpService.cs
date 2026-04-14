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

            if (profile.BaseRecommendedProducts == null || profile.BaseRecommendedProducts.Count == 0)
            {
                _logger.LogInformation(
                    "Follow-up rerank skipped because no last recommended products. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }

            var allowedNames = profile.BaseRecommendedProducts
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
            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                products = products
                    .Where(x => string.Equals(x.ThuongHieu, intent.Brand, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                products = products
                    .Where(x => (x.Loai ?? string.Empty).Contains(intent.Category, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (intent.ExcludedBrands.Any())
            {
                products = products
                    .Where(x => !intent.ExcludedBrands.Any(ex =>
                        string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            if (intent.ExcludedCategories.Any())
            {
                products = products
                    .Where(x => !intent.ExcludedCategories.Any(ex =>
                        (x.Loai ?? string.Empty).Contains(ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            if (products.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = BuildNoMatchReply(intent)
                };
            }
            products = ProductPriceFilterHelper.ApplyStrictPriceFilter(products, intent);

            if (products.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = BuildNoMatchReply(intent)
                };
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

            var reply = BuildReply(reranked, intent, _productRecommendationService);
            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
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
        private static string BuildReply(
     IReadOnlyList<ProductSummaryDto> items,
     ParsedIntent intent,
     IProductRecommendationService productRecommendationService)
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

            var topReason = productRecommendationService.BuildMainReason(top, intent);

            string intro;

            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu giữ mức **dưới {intent.PriceMax.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu xét các mẫu **từ {intent.PriceMin.Value:N0} VNĐ trở lên** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else if (intent.FilterType == PriceFilterType.Range &&
                     intent.PriceMin.HasValue &&
                     intent.PriceMax.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu lọc trong khoảng **{intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                intro = $"Trong các mẫu vừa rồi, nếu ưu tiên quanh mức **{intent.TargetPrice.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
            }
            else
            {
                intro = focusLabel switch
                {
                    "cốp rộng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "dễ chống chân" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "tiết kiệm xăng" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "hợp nữ" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "thực dụng / đi làm hằng ngày" => $"Nếu chọn trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "đi học / sinh viên" => $"Nếu xét trong nhóm này theo hướng **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    "đi êm" => $"Trong nhóm mình vừa gợi ý, nếu ưu tiên **{focusLabel}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.",
                    _ => $"Trong các mẫu vừa rồi, mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}."
                };
            }

            var sb = new StringBuilder();
            sb.AppendLine(intro);

            if (backups.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Các phương án phụ bạn vẫn có thể cân nhắc thêm:");

                foreach (var item in backups)
                {
                    var reason = productRecommendationService.BuildMainReason(item, intent);
                    sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ): {reason}");
                }
            }

            return sb.ToString().Trim();
        }
        private static string BuildNoMatchReply(ParsedIntent intent)
        {
            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự nằm **dưới {intent.PriceMax.Value:N0} VNĐ**.";
            }

            if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự nằm **từ {intent.PriceMin.Value:N0} VNĐ trở lên**.";
            }

            if (intent.FilterType == PriceFilterType.Range &&
                intent.PriceMin.HasValue &&
                intent.PriceMax.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự nằm trong khoảng **{intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ**.";
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                return $"Trong nhóm mình vừa gợi ý, hiện chưa có mẫu nào thật sự đủ sát mức **khoảng {intent.TargetPrice.Value:N0} VNĐ**.";
            }

            return "Trong nhóm mình vừa gợi ý, hiện chưa còn mẫu nào thật sự phù hợp với tiêu chí này.";
        }
    }

}