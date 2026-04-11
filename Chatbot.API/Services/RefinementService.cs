using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using System.Text;

namespace Chatbot.API.Services
{
    public class RefinementService : IRefinementService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<RefinementService> _logger;

        public RefinementService(
            IWebBanXeMayToolClient toolClient,
            IProductRecommendationService productRecommendationService,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<RefinementService> logger)
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
            if (profile.HasActiveCompareContext && profile.LastComparedProducts.Count >= 2)
            {
                return null;
            }

            if (!profile.HasActiveRecommendationContext ||
                profile.BaseRecommendedProducts == null ||
                profile.BaseRecommendedProducts.Count == 0)
            {
                return null;
            }

            var allowedNames = profile.BaseRecommendedProducts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var previousProducts = new List<ProductSummaryDto>();

            foreach (var name in allowedNames)
            {
                var result = await _toolClient.SearchProductsAsync(name, 3);

                var matched = result?.Items?
                    .OrderByDescending(x => string.Equals(x.Ten, name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (matched != null)
                {
                    previousProducts.Add(matched);
                }
            }

            previousProducts = previousProducts
                .Where(x => !string.IsNullOrWhiteSpace(x.Ten) && allowedNames.Contains(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (previousProducts.Count == 0)
            {
                return null;
            }

            IEnumerable<ProductSummaryDto> filtered = previousProducts;

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                filtered = filtered.Where(x => string.Equals(x.ThuongHieu, intent.Brand, StringComparison.OrdinalIgnoreCase));
            }

            if (intent.ExcludedCategories.Any())
            {
                filtered = filtered.Where(x =>
                    !intent.ExcludedCategories.Any(ex => IsSameCategory(x.Loai, ex)));
            }

            if (intent.ExcludedBrands.Any())
            {
                filtered = filtered.Where(x =>
                    !intent.ExcludedBrands.Any(ex => x.ThuongHieu.Equals(ex, StringComparison.OrdinalIgnoreCase)));
            }

            var filteredList = filtered.ToList();
            if (filteredList.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = BuildNoMatchReply(intent)
                };
            }
            filteredList = ApplyStrictPriceFilter(filteredList, intent);
            if (filteredList.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = BuildNoMatchReply(intent)
                };
            }
            var text = normalizedMessage.Trim().ToLowerInvariant();

            if (text.Contains("rẻ hơn") || text.Contains("re hon"))
            {
                filteredList = filteredList
                    .OrderBy(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .ToList();
            }

            if (text.Contains("đẹp hơn") || text.Contains("dep hon"))
            {
                filteredList = filteredList
                    .OrderByDescending(x =>
                        (x.Ten ?? "").Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                        (x.Ten ?? "").Contains("Grande", StringComparison.OrdinalIgnoreCase) ||
                        (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                        (x.Ten ?? "").Contains("Lead", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(x => x.Gia)
                    .ToList();
            }

            if (text.Contains("loại khác") || text.Contains("loai khac") ||
                text.Contains("xe khác") || text.Contains("xe khac") ||
                text.Contains("mẫu khác") || text.Contains("mau khac"))
            {
                var currentTopNames = profile.LastRecommendedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                filteredList = filteredList
                    .Where(x => !currentTopNames.Contains(x.Ten))
                    .ToList();

                if (filteredList.Count == 0)
                {
                    filteredList = previousProducts
                        .Where(x => !currentTopNames.Contains(x.Ten))
                        .ToList();
                }
            }
            if ((intent.WantsLargeStorage || intent.WantsFuelSaving || intent.NeedsLowSeat || !string.IsNullOrWhiteSpace(intent.ComparisonFeature))
    && !intent.ExcludedCategories.Any()
    && !intent.ExcludedBrands.Any()
    && string.IsNullOrWhiteSpace(intent.Brand))
            {
                filteredList = filteredList
                    .OrderByDescending(x =>
                        intent.WantsLargeStorage && (
                            (x.Ten ?? "").Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Address", StringComparison.OrdinalIgnoreCase))
                            ? 1 : 0)
                    .ThenByDescending(x =>
                        intent.NeedsLowSeat && (
                            (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Latte", StringComparison.OrdinalIgnoreCase))
                            ? 1 : 0)
                    .ThenByDescending(x =>
                        intent.WantsFuelSaving && (
                            (x.Ten ?? "").Contains("Wave", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Future", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                            (x.Ten ?? "").Contains("Sirius", StringComparison.OrdinalIgnoreCase))
                            ? 1 : 0)
                    .ToList();
            }
            List<ProductSummaryDto> ranked;

            bool preferCheaper =
                text.Contains("rẻ hơn") || text.Contains("re hon");

            bool preferDifferent =
                text.Contains("loại khác") || text.Contains("loai khac") ||
                text.Contains("xe khác") || text.Contains("xe khac") ||
                text.Contains("mẫu khác") || text.Contains("mau khac");

            if (preferCheaper || preferDifferent)
            {
                ranked = filteredList
                    .Take(Math.Min(3, filteredList.Count))
                    .ToList();
            }
            else
            {
                ranked = _productRecommendationService.RankProducts(
                    filteredList,
                    intent,
                    profile,
                    normalizedMessage,
                    take: Math.Min(3, filteredList.Count));
            }

            if (ranked == null || ranked.Count == 0)
            {
                return null;
            }

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
    conversationId,
    ranked,
    "refine");

            var reply = BuildRefineReply(ranked, intent, _productRecommendationService);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(ranked, 4)
            };
        }
      
        private static string BuildRefineReply(
    IReadOnlyList<ProductSummaryDto> ranked,
    ParsedIntent intent,
    IProductRecommendationService productRecommendationService)
        {
            var top = ranked[0];
            var others = ranked.Skip(1).Take(2).ToList();

            var topReason = productRecommendationService.BuildMainReason(top, intent);

            var sb = new StringBuilder();
            if ((intent.RawMessage ?? string.Empty).Contains("rẻ hơn", StringComparison.OrdinalIgnoreCase) ||
    (intent.RawMessage ?? string.Empty).Contains("re hon", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Nếu ưu tiên **rẻ hơn trong nhóm đang gợi ý** thì mình nghiêng về **{top.Ten}** ({top.Gia:N0} VNĐ).");
            }
            else if ((intent.RawMessage ?? string.Empty).Contains("đẹp hơn", StringComparison.OrdinalIgnoreCase) ||
                     (intent.RawMessage ?? string.Empty).Contains("dep hon", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"Nếu ưu tiên **đẹp hơn / hợp gu hơn** trong nhóm đang gợi ý thì mình nghiêng về **{top.Ten}** ({top.Gia:N0} VNĐ).");
            }
            else if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu giữ mức **dưới {intent.PriceMax.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu xét các mẫu **từ {intent.PriceMin.Value:N0} VNĐ trở lên** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (intent.FilterType == PriceFilterType.Range &&
                     intent.PriceMin.HasValue &&
                     intent.PriceMax.HasValue)
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu lọc trong khoảng **{intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu ưu tiên quanh mức **{intent.TargetPrice.Value:N0} VNĐ** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                sb.AppendLine($"Nếu chỉ xét theo **{intent.Brand}** trong nhóm mình vừa gợi ý thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (intent.ExcludedBrands.Any())
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedBrands)}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (intent.ExcludedCategories.Any())
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedCategories)}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else if (!string.IsNullOrWhiteSpace(intent.ComparisonFeature))
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu chỉ xét theo tiêu chí này thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }
            else
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu lọc theo tiêu chí mới thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ), vì mẫu này {topReason}.");
            }

            if (others.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Các phương án phụ bạn vẫn có thể cân nhắc:");

                foreach (var item in others)
                {
                    var reason = productRecommendationService.BuildMainReason(item, intent);
                    sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ): {reason}");
                }
            }

            return sb.ToString().Trim();
        }
        private static bool IsSameCategory(string? actualCategory, string excludedCategory)
        {
            var actual = NormalizeCategory(actualCategory);
            var excluded = NormalizeCategory(excludedCategory);

            return string.Equals(actual, excluded, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCategory(string? category)
        {
            var text = (category ?? string.Empty).Trim().ToLowerInvariant();

            if (text.Contains("ga"))
                return "xe ga";

            if (text.Contains("số") || text.Contains("so"))
                return "xe số";

            if (text.Contains("côn") || text.Contains("con"))
                return "côn tay";

            return text;
        }
        private static List<ProductSummaryDto> ApplyStrictPriceFilter(
    List<ProductSummaryDto> items,
    ParsedIntent intent)
        {
            if (items == null || items.Count == 0)
                return new List<ProductSummaryDto>();

            IEnumerable<ProductSummaryDto> query = items;

            // 1. Khoảng giá cứng: từ X đến Y
            if (intent.FilterType == PriceFilterType.Range &&
                intent.PriceMin.HasValue &&
                intent.PriceMax.HasValue)
            {
                query = query.Where(x => x.Gia >= intent.PriceMin.Value && x.Gia <= intent.PriceMax.Value);
                return query.ToList();
            }

            // 2. Giá tối đa cứng: dưới / tối đa / không quá
            if (intent.FilterType == PriceFilterType.MaxOnly &&
                intent.PriceMax.HasValue)
            {
                query = query.Where(x => x.Gia <= intent.PriceMax.Value);
                return query.ToList();
            }

            // 3. Giá tối thiểu cứng: trên / từ ... trở lên
            if (intent.FilterType == PriceFilterType.MinOnly &&
                intent.PriceMin.HasValue)
            {
                query = query.Where(x => x.Gia >= intent.PriceMin.Value);
                return query.ToList();
            }

            // 4. Giá "tầm / khoảng / quanh"
            if (intent.FilterType == PriceFilterType.Around &&
                intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = GetAroundDelta(target);

                query = query.Where(x => x.Gia >= target - delta && x.Gia <= target + delta);

                var filtered = query.ToList();

                // nếu lọc quá chặt mà không còn gì thì nới nhẹ
                if (filtered.Count == 0)
                {
                    var relaxedDelta = delta + 2_000_000m;
                    filtered = items
                        .Where(x => x.Gia >= target - relaxedDelta && x.Gia <= target + relaxedDelta)
                        .ToList();
                }

                return filtered;
            }

            return query.ToList();
        }

        private static decimal GetAroundDelta(decimal target)
        {
            if (target <= 20_000_000m) return 2_000_000m;
            if (target <= 35_000_000m) return 3_000_000m;
            if (target <= 50_000_000m) return 4_000_000m;
            return 5_000_000m;
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

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                return $"Trong nhóm mình vừa gợi ý thì hiện không còn mẫu **{intent.Brand}** nào thật sự phù hợp nữa.";
            }

            if (intent.ExcludedBrands.Any())
            {
                return $"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedBrands)}** thì hiện chưa còn mẫu nào thật sự phù hợp.";
            }

            if (intent.ExcludedCategories.Any())
            {
                return $"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedCategories)}** thì hiện chưa còn mẫu nào thật sự phù hợp.";
            }

            return "Trong nhóm mình vừa gợi ý thì sau khi lọc theo tiêu chí này hiện chưa còn mẫu nào thật sự phù hợp.";
        }
    }
}