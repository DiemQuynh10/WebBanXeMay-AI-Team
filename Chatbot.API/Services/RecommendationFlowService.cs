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
            static HashSet<string> BuildExcludedBrandSet(ParsedIntent currentIntent, CustomerPreferenceProfile currentProfile)
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

            static List<ProductSummaryDto> ApplyStrictBrandExclusion(
                IEnumerable<ProductSummaryDto> products,
                HashSet<string> excludedBrands)
            {
                if (excludedBrands.Count == 0)
                    return products.Where(x => x != null).ToList();

                return products
                    .Where(x => x != null)
                    .Where(x => !excludedBrands.Contains(x.ThuongHieu?.Trim() ?? string.Empty))
                    .ToList();
            }

            static bool IsExpandIntent(string message)
            {
                if (string.IsNullOrWhiteSpace(message))
                    return false;

                var normalized = message.Trim().ToLowerInvariant();
                return normalized.Contains("xe khac", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("xe khác", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("mau khac", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("mẫu khác", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("con mau nao", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("còn mẫu nào", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("con xe nao", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("còn xe nào", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("goi y them", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("gợi ý thêm", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("them lua chon", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("thêm lựa chọn", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("gia cao hon", StringComparison.OrdinalIgnoreCase) ||
                       normalized.Contains("giá cao hơn", StringComparison.OrdinalIgnoreCase);
            }

            static bool WantsHigherPrice(string message)
            {
                if (string.IsNullOrWhiteSpace(message))
                    return false;

                return message.Contains("gia cao hon", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("giá cao hơn", StringComparison.OrdinalIgnoreCase);
            }

            static List<ProductSummaryDto> ExcludePreviousRecommendations(
                IEnumerable<ProductSummaryDto> products,
                HashSet<int> previousIds,
                HashSet<string> previousNames)
            {
                return products
                    .Where(x => x != null)
                    .Where(x => !previousIds.Contains(x.Id))
                    .Where(x => !previousNames.Contains(x.Ten?.Trim() ?? string.Empty))
                    .ToList();
            }

            var excludedBrands = BuildExcludedBrandSet(intent, profile);
            var hasIntentBrandExclusions = intent.ExcludedBrands.Count > 0;
            var isExpandIntent = IsExpandIntent(normalizedMessage);
            var wantsHigherPrice = WantsHigherPrice(normalizedMessage);
            var previousRecommendedIds = profile.LastRecommendedProductIds
                .Distinct()
                .ToHashSet();
            var previousRecommendedNames = profile.LastRecommendedProducts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (isExpandIntent)
            {
                await _conversationPreferenceService.ClearRecommendationContextAsync(conversationId);
            }

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
            if (ProductExclusionHelper.IsBrandExcludedByIntentOrProfile(requestedBrand, intent, profile))
            {
                requestedBrand = null;
            }

            var effectiveCategory = !string.IsNullOrWhiteSpace(intent.Category)
                ? intent.Category
                : profile.PreferredCategory;
            if (ProductExclusionHelper.IsCategoryExcludedByIntentOrProfile(effectiveCategory, intent, profile))
            {
                effectiveCategory = null;
            }

            decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
            decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;
            var take = isExpandIntent ? 50 : 30;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = target <= 20_000_000m ? 2_000_000m
                    : target <= 35_000_000m ? 3_000_000m
                    : target <= 50_000_000m ? 4_000_000m
                    : 5_000_000m;

                if (isExpandIntent)
                {
                    delta += 3_000_000m;
                }

                minPrice = Math.Max(0, target - delta);
                maxPrice = target + delta;
            }
            else if (isExpandIntent)
            {
                if (wantsHigherPrice)
                {
                    if (maxPrice.HasValue)
                    {
                        minPrice = maxPrice.Value + 1;
                        maxPrice = maxPrice.Value + 10_000_000m;
                    }
                    else if (minPrice.HasValue)
                    {
                        minPrice = minPrice.Value + 3_000_000m;
                        maxPrice = minPrice.Value + 10_000_000m;
                    }
                    else if (profile.TargetPrice.HasValue)
                    {
                        minPrice = profile.TargetPrice.Value + 1;
                        maxPrice = profile.TargetPrice.Value + 10_000_000m;
                    }
                }
                else
                {
                    if (minPrice.HasValue)
                        minPrice = Math.Max(0, minPrice.Value - 3_000_000m);

                    if (maxPrice.HasValue)
                        maxPrice = maxPrice.Value + 5_000_000m;
                }
            }

            var toolResult = await _toolClient.GetProductsByFiltersAsync(
     brand: requestedBrand,
     minPrice: minPrice,
     maxPrice: maxPrice,
     category: effectiveCategory,
     take: take);

            var items = toolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();
            items = ProductExclusionHelper.ApplyExclusions(items, intent, profile);
            items = ApplyStrictBrandExclusion(items, excludedBrands);
            if (isExpandIntent)
            {
                items = ExcludePreviousRecommendations(items, previousRecommendedIds, previousRecommendedNames);
            }

            if (items.Count == 0 && !string.IsNullOrWhiteSpace(effectiveCategory))
            {
                toolResult = await _toolClient.GetProductsByFiltersAsync(
                    brand: requestedBrand,
                    minPrice: minPrice,
                    maxPrice: maxPrice,
                    category: null,
                    take: take);

                items = toolResult?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();
                items = ProductExclusionHelper.ApplyExclusions(items, intent, profile);
                items = ApplyStrictBrandExclusion(items, excludedBrands);
                if (isExpandIntent)
                {
                    items = ExcludePreviousRecommendations(items, previousRecommendedIds, previousRecommendedNames);
                }
            }

            if (items.Count == 0 && !hasIntentBrandExclusions)
            {
                items = await TryGetBrandRelaxedCandidatesAsync(intent, profile, effectiveCategory);
                items = ApplyStrictBrandExclusion(items, excludedBrands);
                if (isExpandIntent)
                {
                    items = ExcludePreviousRecommendations(items, previousRecommendedIds, previousRecommendedNames);
                }
            }

            if (items.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = isExpandIntent
                        ? "Mình đã lọc rộng hơn nhưng hiện chưa thấy thêm mẫu mới nào khác với các xe vừa gợi ý."
                        : BuildNoRecommendationMatchReply(intent, requestedBrand, effectiveCategory)
                };
            }
            var strictlyFilteredItems = ProductPriceFilterHelper.ApplyStrictPriceFilter(items, intent);
            strictlyFilteredItems = ApplyStrictBrandExclusion(strictlyFilteredItems, excludedBrands);
            if (isExpandIntent)
            {
                strictlyFilteredItems = ExcludePreviousRecommendations(strictlyFilteredItems, previousRecommendedIds, previousRecommendedNames);
            }

            if (strictlyFilteredItems.Count > 0)
            {
                items = strictlyFilteredItems;
            }

            items = ProductExclusionHelper.ApplyExclusions(items, intent, profile);
            items = ApplyStrictBrandExclusion(items, excludedBrands);
            if (isExpandIntent)
            {
                items = ExcludePreviousRecommendations(items, previousRecommendedIds, previousRecommendedNames);
            }

            if (items.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = isExpandIntent
                        ? "Mình đã lọc rộng hơn nhưng hiện chưa thấy thêm mẫu mới nào khác với các xe vừa gợi ý."
                        : BuildNoRecommendationMatchReply(intent, requestedBrand, effectiveCategory)
                };
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
            ranked = ApplyStrictBrandExclusion(ranked ?? new List<ProductSummaryDto>(), excludedBrands);
            if (isExpandIntent)
            {
                ranked = ExcludePreviousRecommendations(ranked, previousRecommendedIds, previousRecommendedNames);
            }

            if (ranked == null || ranked.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = isExpandIntent
                        ? "Mình đã tìm rộng hơn nhưng chưa có thêm mẫu mới nào thực sự khác với những xe vừa gợi ý."
                        : "Mình có tìm thấy dữ liệu sản phẩm, nhưng chưa lọc ra được mẫu nổi bật thật sự phù hợp. Bạn nói thêm một tiêu chí ngắn như cốp rộng, dễ chống chân hoặc hãng muốn ưu tiên nhé."
                };
            }

            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
                conversationId,
                ranked);

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                isExpandIntent ? "followup" : "fresh_consultation");

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
    string? effectiveBrand,
    string? effectiveCategory)
        {
            var brand = effectiveBrand;
            var category = effectiveCategory;

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
            items = ProductExclusionHelper.ApplyExclusions(items, intent, profile);

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
            items = ProductExclusionHelper.ApplyExclusions(items, intent, profile);

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
