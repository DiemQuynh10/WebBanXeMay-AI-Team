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

            if (!profile.HasActiveRecommendationContext || profile.LastRecommendedProducts == null || profile.LastRecommendedProducts.Count == 0)
            {
                return null;
            }

            var allowedNames = profile.LastRecommendedProducts
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
                    !intent.ExcludedCategories.Any(ex => x.Loai.Contains(ex, StringComparison.OrdinalIgnoreCase)));
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
                    Reply = !string.IsNullOrWhiteSpace(intent.Brand)
    ? $"Trong nhóm mình vừa gợi ý thì hiện không còn mẫu **{intent.Brand}** nào thật sự phù hợp nữa."
    : intent.ExcludedBrands.Any()
        ? $"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedBrands)}** thì hiện chưa còn mẫu nào thật sự phù hợp."
        : intent.ExcludedCategories.Any()
            ? $"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedCategories)}** thì hiện chưa còn mẫu nào thật sự phù hợp."
            : "Trong nhóm mình vừa gợi ý thì sau khi lọc theo tiêu chí này hiện chưa còn mẫu nào thật sự phù hợp."
                };
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
            var ranked = _productRecommendationService.RankProducts(
                filteredList,
                intent,
                profile,
                normalizedMessage,
                take: Math.Min(3, filteredList.Count));

            if (ranked == null || ranked.Count == 0)
            {
                return null;
            }

            await _conversationPreferenceService.SetRecommendedProductsAsync(
                conversationId,
                ranked,
                "refine");

            var reply = BuildRefineReply(ranked, intent);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply
            };
        }
        private static string BuildRefineReply(IReadOnlyList<ProductSummaryDto> ranked, ParsedIntent intent)
        {
            var top = ranked[0];
            var others = ranked.Skip(1).Take(2).ToList();

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                sb.AppendLine($"Nếu chỉ xét theo **{intent.Brand}** trong nhóm mình vừa gợi ý thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ).");
            }
            else if (intent.ExcludedBrands.Any())
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedBrands)}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ).");
            }
            else if (intent.ExcludedCategories.Any())
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, sau khi bỏ **{string.Join(", ", intent.ExcludedCategories)}** thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ).");
            }
            else
            {
                sb.AppendLine($"Trong nhóm mình vừa gợi ý, nếu lọc theo tiêu chí mới thì mình nghiêng hơn về **{top.Ten}** ({top.Gia:N0} VNĐ).");
            }

            if (others.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Các phương án phụ bạn vẫn có thể cân nhắc:");
                foreach (var item in others)
                {
                    sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ)");
                }
            }

            return sb.ToString().Trim();
        }

    }
}