using System.Text;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class RecommendationFlowService : IRecommendationFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IRecommendationClarificationService _recommendationClarificationService;
        private readonly ILogger<RecommendationFlowService> _logger;

        public RecommendationFlowService(
            IWebBanXeMayToolClient toolClient,
            IConversationPreferenceService conversationPreferenceService,
            IProductRecommendationService productRecommendationService,
            IRecommendationClarificationService recommendationClarificationService,
            ILogger<RecommendationFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _productRecommendationService = productRecommendationService;
            _recommendationClarificationService = recommendationClarificationService;
            _logger = logger;
        }

        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            bool hasEnoughSignals =
                _recommendationClarificationService.HasEnoughSignalsForDirectRecommendation(
                    normalizedMessage,
                    intent,
                    profile);

            bool shouldClarify =
                !hasEnoughSignals &&
                _recommendationClarificationService.NeedsClarificationForConsultation(
                    normalizedMessage,
                    intent,
                    profile);

            if (shouldClarify)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = _recommendationClarificationService.BuildClarificationQuestion(
                        normalizedMessage,
                        intent,
                        profile)
                };
            }

            var requestedBrand = intent.Brand ?? profile.PreferredBrand;

            var effectiveCategory = !string.IsNullOrWhiteSpace(intent.Category)
                ? intent.Category
                : profile.PreferredCategory;

            decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
            decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = target <= 20_000_000m ? 2_000_000m
                    : target <= 35_000_000m ? 3_000_000m
                    : target <= 50_000_000m ? 4_000_000m
                    : 5_000_000m;

                minPrice = Math.Max(0, target - delta);
                maxPrice = target + delta;
            }

            var toolResult = await _toolClient.GetProductsByFiltersAsync(
     brand: requestedBrand,
     minPrice: minPrice,
     maxPrice: maxPrice,
     category: effectiveCategory,
     take: 30);

            var items = toolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            if (items.Count == 0 && !string.IsNullOrWhiteSpace(effectiveCategory))
            {
                toolResult = await _toolClient.GetProductsByFiltersAsync(
                    brand: requestedBrand,
                    minPrice: minPrice,
                    maxPrice: maxPrice,
                    category: null,
                    take: 30);

                items = toolResult?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();
            }

            if (items.Count == 0)
            {
                items = await TryGetBrandRelaxedCandidatesAsync(intent, profile, effectiveCategory);
            }

            if (items.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = BuildNoRecommendationMatchReply(intent, profile, effectiveCategory)
                };
            }
            var strictlyFilteredItems = ProductPriceFilterHelper.ApplyStrictPriceFilter(items, intent);

            if (strictlyFilteredItems.Count > 0)
            {
                items = strictlyFilteredItems;
            }
            else
            {
                _logger.LogInformation(
                    "Strict price filter produced no items. Keep relaxed candidates for ranking. ConversationId={ConversationId}, RequestedBrand={Brand}, RequestedCategory={Category}",
                    conversationId,
                    requestedBrand,
                    effectiveCategory);
            }
            var ranked = _productRecommendationService.RankProducts(
                items,
                intent,
                profile,
                normalizedMessage,
                take: 4);

            if (ranked == null || ranked.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = "Mình có tìm thấy dữ liệu sản phẩm, nhưng chưa lọc ra được mẫu nổi bật thật sự phù hợp. Bạn nói thêm một tiêu chí ngắn như cốp rộng, dễ chống chân hoặc hãng muốn ưu tiên nhé."
                };
            }

            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
                conversationId,
                ranked);

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                "fresh_consultation");

            var reply = BuildRecommendationReply(ranked, intent);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ToolNames.GetProductsByFilters,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(ranked, 4)
            };
        }
        private static string BuildNoRecommendationMatchReply(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string? effectiveCategory)
        {
            var brand = intent.Brand ?? profile.PreferredBrand;
            var category = !string.IsNullOrWhiteSpace(intent.Category) ? intent.Category : effectiveCategory;

            bool hasBudget =
                intent.TargetPrice.HasValue ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue;

            if (!string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(category) && hasBudget)
            {
                return $"Hiện mình chưa thấy mẫu **{category}** của **{brand}** nào thật sự khớp sát mức giá bạn đang muốn. Bạn có thể nới nhẹ ngân sách hoặc bỏ bớt một tiêu chí để mình lọc tiếp sát hơn.";
            }

            if (!string.IsNullOrWhiteSpace(brand) && hasBudget)
            {
                return $"Hiện mình chưa thấy mẫu **{brand}** nào thật sự khớp sát mức giá bạn đang muốn. Bạn có thể nới nhẹ ngân sách hoặc để mình gợi ý thêm các mẫu gần nhất.";
            }

            if (!string.IsNullOrWhiteSpace(category) && hasBudget)
            {
                return $"Hiện mình chưa thấy mẫu **{category}** nào thật sự khớp sát mức giá bạn đang muốn. Bạn có thể nới nhẹ ngân sách hoặc đổi sang hãng khác để mình lọc tiếp.";
            }

            return "Mình chưa lọc ra được mẫu nào thật sự phù hợp từ dữ liệu hiện tại. Bạn thử nói thêm một tiêu chí như loại xe, hãng hoặc mức giá sát hơn nhé.";
        }
        private async Task<List<ProductSummaryDto>> TryGetBrandRelaxedCandidatesAsync(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string? effectiveCategory)
        {
            var requestedBrand = intent.Brand ?? profile.PreferredBrand;

            decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
            decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = ProductPriceFilterHelper.GetAroundDelta(target) + 3_000_000m;
                minPrice = Math.Max(0, target - delta);
                maxPrice = target + delta;
            }
            else
            {
                if (minPrice.HasValue)
                    minPrice = Math.Max(0, minPrice.Value - 3_000_000m);

                if (maxPrice.HasValue)
                    maxPrice = maxPrice.Value + 3_000_000m;
            }

            // Bước 1: giữ brand, bỏ category
            var result = await _toolClient.GetProductsByFiltersAsync(
                brand: requestedBrand,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: null,
                take: 30);

            var items = result?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            if (items.Count > 0)
                return items;

            // Bước 2: nếu vẫn không có thì thử giữ category, bỏ brand
            result = await _toolClient.GetProductsByFiltersAsync(
                brand: null,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: effectiveCategory,
                take: 30);

            items = result?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            return items;
        }
        private string BuildRecommendationReply(
    IReadOnlyList<ProductSummaryDto> ranked,
    ParsedIntent intent)
        {
            var sb = new StringBuilder();

            bool hasBudget =
                intent.TargetPrice.HasValue ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue;

            bool hasBrandOrCategory =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category);

            if (hasBudget && hasBrandOrCategory)
            {
                sb.AppendLine($"Mình thấy có {ranked.Count} mẫu khá gần với tiêu chí bạn đang muốn:");
            }
            else if (hasBudget)
            {
                sb.AppendLine($"Trong tầm bạn đang cân nhắc, mình thấy {ranked.Count} mẫu khá đáng chú ý:");
            }
            else
            {
                sb.AppendLine($"Mình thấy {ranked.Count} mẫu khá hợp với nhu cầu bạn đang nói tới:");
            }
            sb.AppendLine();

            foreach (var item in ranked)
            {
                var reason = _productRecommendationService.BuildMainReason(item, intent);
                sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ): {reason}");
            }

            sb.AppendLine();
            if (string.IsNullOrWhiteSpace(intent.Brand) && string.IsNullOrWhiteSpace(intent.Category))
            {
                sb.AppendLine("Bạn có thể lọc tiếp theo hãng, loại xe hoặc tiêu chí như cốp rộng, dễ chống chân, tiết kiệm xăng.");
            }
            else
            {
                sb.AppendLine("Bạn có thể lọc tiếp thêm theo mức giá, nhu cầu đi lại hoặc các tiêu chí như cốp rộng, dễ chống chân, tiết kiệm xăng.");
            }
            return sb.ToString().Trim();
        }
      
    }
}