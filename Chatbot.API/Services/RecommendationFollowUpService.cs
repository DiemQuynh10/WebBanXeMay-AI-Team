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

            var sourceNames =
     (profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0)
         ? profile.CurrentRecommendedProducts
         : profile.BaseRecommendedProducts;

            if (sourceNames == null || sourceNames.Count == 0)
            {
                _logger.LogInformation(
                    "Follow-up rerank skipped because no recommended products available. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }

            var allowedNames = sourceNames
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
            bool hasExplicitPriceRefinement =
     intent.FilterType == PriceFilterType.MaxOnly ||
     intent.FilterType == PriceFilterType.MinOnly ||
     intent.FilterType == PriceFilterType.Range ||
     intent.FilterType == PriceFilterType.Around;

            if (hasExplicitPriceRefinement)
            {
                products = ProductPriceFilterHelper.ApplyStrictPriceFilter(products, intent);
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
            bool wantsAlternative =
    normalizedMessage.Contains("mau khac") ||
    normalizedMessage.Contains("xe khac") ||
    normalizedMessage.Contains("khac di") ||
    normalizedMessage.Contains("doi mau khac") ||
    normalizedMessage.Contains("con mau nao khac");

            if (wantsAlternative && profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0)
            {
                var currentTop = profile.CurrentRecommendedProducts.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(currentTop))
                {
                    var filteredAlternatives = products
                        .Where(x => !string.Equals(x.Ten, currentTop, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (filteredAlternatives.Count > 0)
                    {
                        products = filteredAlternatives;
                    }
                }
            }
            products = ApplyFollowUpSemanticOrdering(products, intent, normalizedMessage);
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

            var reply = BuildReply(reranked, intent, normalizedMessage, _productRecommendationService);
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
     string normalizedMessage,
     IProductRecommendationService productRecommendationService)
        {
            var top = items[0];
            var backups = items.Skip(1).Take(2).ToList();
            bool wantsAlternativeTone =
    !string.IsNullOrWhiteSpace(normalizedMessage) &&
    (
        normalizedMessage.Contains("mau khac") ||
        normalizedMessage.Contains("xe khac") ||
        normalizedMessage.Contains("khac di") ||
        normalizedMessage.Contains("doi mau khac") ||
        normalizedMessage.Contains("con mau nao khac")
    );
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
            if (focusLabel == null)
            {
                if (intent.WantsLargeStorage) focusLabel = "cốp rộng";
                else if (intent.NeedsLowSeat || intent.WantsEasyControl) focusLabel = "dễ chống chân";
                else if (intent.WantsFuelSaving) focusLabel = "tiết kiệm xăng";
                else if (intent.PrefersFemaleStyle || (!string.IsNullOrWhiteSpace(intent.Target) && intent.Target.Contains("nữ")))
                    focusLabel = "hợp nữ";
                else if (intent.ForWork) focusLabel = "đi làm hằng ngày";
                else if (intent.ForSchool) focusLabel = "đi học / sinh viên";
            }
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
                if (wantsAlternativeTone)
                {
                    intro = $"Nếu đổi sang một phương án khác trong nhóm vừa rồi thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.";
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
        private static List<ProductSummaryDto> ApplyFollowUpSemanticOrdering(
    List<ProductSummaryDto> items,
    ParsedIntent intent,
    string normalizedMessage)
        {
            if (items == null || items.Count == 0)
                return new List<ProductSummaryDto>();

            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            bool prefersFemale =
    intent.PrefersFemaleStyle ||
    (!string.IsNullOrWhiteSpace(intent.Target) &&
     (intent.Target.Contains("nữ", StringComparison.OrdinalIgnoreCase) ||
      intent.Target.Contains("nu", StringComparison.OrdinalIgnoreCase)));

            if (prefersFemale || text.Contains("hop nu") || text.Contains("cho nu"))
            {
                return items
                    .OrderByDescending(x => LooksLikeFemaleFriendly(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.ForWork || text.Contains("di lam"))
            {
                return items
                    .OrderByDescending(x => LooksLikeWorkFriendly(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.ForSchool || text.Contains("di hoc"))
            {
                return items
                    .OrderByDescending(x => LooksLikeSchoolFriendly(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }
            if (intent.WantsLargeStorage || text.Contains("cop rong"))
            {
                return items
                    .OrderByDescending(x => LooksLikeLargeStorage(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.WantsFuelSaving || text.Contains("tiet kiem xang"))
            {
                return items
                    .OrderByDescending(x => LooksLikeFuelSaving(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.NeedsLowSeat || intent.WantsEasyControl || text.Contains("de chong chan"))
            {
                return items
                    .OrderByDescending(x => LooksLikeLowSeat(x))
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            return items;
        }
        private static bool LooksLikeLargeStorage(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("latte") || name.Contains("lead") || name.Contains("freego") || name.Contains("air blade");
        }

        private static bool LooksLikeFuelSaving(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") || name.Contains("wave") || name.Contains("future") || name.Contains("sirius");
        }

        private static bool LooksLikeLowSeat(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") || name.Contains("janus") || name.Contains("zip") || name.Contains("latte");
        }
        private static bool LooksLikeFemaleFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") ||
                   name.Contains("latte") ||
                   name.Contains("janus") ||
                   name.Contains("zip") ||
                   name.Contains("lead");
        }

        private static bool LooksLikeWorkFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") ||
                   name.Contains("future") ||
                   name.Contains("wave") ||
                   name.Contains("air blade") ||
                   name.Contains("freego");
        }

        private static bool LooksLikeSchoolFriendly(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") ||
                   name.Contains("janus") ||
                   name.Contains("zip") ||
                   name.Contains("sirius") ||
                   name.Contains("wave");
        }
    }

}