using System.Text;
using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Services
{
    public class RecommendationFlowService : IRecommendationFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IRecommendationClarificationService _recommendationClarificationService;
        private readonly ILogger<RecommendationFlowService> _logger;
        private readonly IRecommendationLLMService _recommendationLLMService;
        private readonly IReplyStyleService _replyStyleService;
        private readonly IReplyRewriteService _replyRewriteService;
        private readonly ReplyRewriteOptions _replyRewriteOptions;
        public RecommendationFlowService(
     IWebBanXeMayToolClient toolClient,
     IConversationPreferenceService conversationPreferenceService,
     IProductRecommendationService productRecommendationService,
     IRecommendationClarificationService recommendationClarificationService,
     IRecommendationLLMService recommendationLLMService,
     IReplyStyleService replyStyleService,
     IReplyRewriteService replyRewriteService,
     IOptions<ReplyRewriteOptions> replyRewriteOptions,
     ILogger<RecommendationFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _productRecommendationService = productRecommendationService;
            _recommendationClarificationService = recommendationClarificationService;
            _recommendationLLMService = recommendationLLMService;
            _replyStyleService = replyStyleService;
            _replyRewriteService = replyRewriteService;
            _replyRewriteOptions = replyRewriteOptions.Value;
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

            var (minPrice, maxPrice) = ResolveRecommendationPriceRange(intent, profile);

            // 1) Strict query đầu tiên
            var items = await GetStrictCandidatesAsync(
                requestedBrand,
                effectiveCategory,
                minPrice,
                maxPrice);

            _logger.LogInformation(
                "Recommendation strict query. ConversationId={ConversationId}, Brand={Brand}, Category={Category}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, CandidateCount={CandidateCount}",
                conversationId,
                requestedBrand,
                effectiveCategory,
                minPrice,
                maxPrice,
                items.Count);

            // 2) Nếu strict query không ra gì và có category thì bỏ category trước
            if (items.Count == 0 && !string.IsNullOrWhiteSpace(effectiveCategory))
            {
                items = await GetStrictCandidatesAsync(
                    requestedBrand,
                    null,
                    minPrice,
                    maxPrice);

                _logger.LogInformation(
                    "Recommendation fallback query without category. ConversationId={ConversationId}, Brand={Brand}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, CandidateCount={CandidateCount}",
                    conversationId,
                    requestedBrand,
                    minPrice,
                    maxPrice,
                    items.Count);
            }

            // 3) Nếu vẫn không ra gì mới nới rộng
            if (items.Count == 0)
            {
                items = await TryGetBrandRelaxedCandidatesAsync(intent, profile, effectiveCategory);

                _logger.LogInformation(
                    "Recommendation relaxed fallback query. ConversationId={ConversationId}, Brand={Brand}, Category={Category}, CandidateCount={CandidateCount}",
                    conversationId,
                    requestedBrand,
                    effectiveCategory,
                    items.Count);
            }

            if (items.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = _replyStyleService.BuildRecommendationNoMatchReply(
    intent,
    profile,
    effectiveCategory)
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
            var rankedByRule = _productRecommendationService.RankProducts(
    items,
    intent,
    profile,
    normalizedMessage,
    take: 5);
            _logger.LogInformation(
    "Recommendation rule ranking completed. ConversationId={ConversationId}, RankedByRuleCount={RankedByRuleCount}",
    conversationId,
    rankedByRule.Count);
            var ranked = rankedByRule;

            if (rankedByRule.Count > 1)
            {
                try
                {
                    var llmResult = await _recommendationLLMService.RerankAsync(
                        normalizedMessage,
                        intent,
                        profile,
                        rankedByRule);

                    var llmRanked = ApplyLlmRerank(rankedByRule, llmResult);
                    if (llmResult?.Recommendations != null && llmResult.Recommendations.Count > 0 && llmRanked.Count == 0)
                    {
                        _logger.LogWarning(
                            "LLM rerank returned recommendations but none could be mapped back to rankedByRule. ConversationId={ConversationId}",
                            conversationId);
                    }

                    if (llmRanked.Count > 0)
                    {
                        _logger.LogInformation(
                            "LLM rerank applied. ConversationId={ConversationId}, RuleCount={RuleCount}, LlmSelectedCount={LlmSelectedCount}",
                            conversationId,
                            rankedByRule.Count,
                            llmRanked.Count);

                        ranked = llmRanked;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LLM rerank failed in RecommendationFlowService. Fallback to rule ranking.");
                }
            }
            else
            {
                _logger.LogInformation(
                    "Skip LLM rerank because only one ranked candidate remains. ConversationId={ConversationId}",
                    conversationId);
            }
            ranked = ranked.Take(4).ToList();
            if (ranked == null || ranked.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = _replyStyleService.BuildRecommendationNoMatchReply(
                        intent,
                        profile,
                        effectiveCategory)
                };
            }
            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
                conversationId,
                ranked);

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                "fresh_consultation");

            var draftReply = _replyStyleService.BuildRecommendationReply(
    ranked,
    intent,
    item => _productRecommendationService.BuildMainReason(item, intent));

            var reply = draftReply;

            if (_replyRewriteOptions.EnableRecommendationRewrite)
            {
                reply = await _replyRewriteService.RewriteAsync(
                    normalizedMessage,
                    draftReply);
            }
            _logger.LogInformation(
    "Recommendation final result. ConversationId={ConversationId}, FinalProductCount={FinalProductCount}",
    conversationId,
    ranked.Count);
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
        private static (decimal? minPrice, decimal? maxPrice) ResolveRecommendationPriceRange(
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
            decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = ProductPriceFilterHelper.GetAroundDelta(target);

                minPrice = Math.Max(0, target - delta);
                maxPrice = target + delta;
            }

            return (minPrice, maxPrice);
        }
        private async Task<List<ProductSummaryDto>> GetStrictCandidatesAsync(
    string? requestedBrand,
    string? effectiveCategory,
    decimal? minPrice,
    decimal? maxPrice)
        {
            var toolResult = await _toolClient.GetProductsByFiltersAsync(
                brand: requestedBrand,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: effectiveCategory,
                take: 30);

            return toolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();
        }
        private async Task<List<ProductSummaryDto>> TryGetBrandRelaxedCandidatesAsync(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string? effectiveCategory)
        {
            var requestedBrand = intent.Brand ?? profile.PreferredBrand;
            bool userExplicitBrand = !string.IsNullOrWhiteSpace(intent.Brand);
            var (minPrice, maxPrice) = ResolveRecommendationPriceRange(intent, profile);

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var relaxedDelta = ProductPriceFilterHelper.GetAroundDelta(target) + 3_000_000m;
                minPrice = Math.Max(0, target - relaxedDelta);
                maxPrice = target + relaxedDelta;
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
            // Bước 2: chỉ bỏ brand nếu brand hiện tại KHÔNG phải do user nói rõ
            if (!userExplicitBrand)
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand: null,
                    minPrice: minPrice,
                    maxPrice: maxPrice,
                    category: effectiveCategory,
                    take: 30);

                items = result?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();
            }

            items = result?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            return items;
        }
        private static List<ProductSummaryDto> ApplyLlmRerank(
    IReadOnlyList<ProductSummaryDto> rankedByRule,
    LLMRecommendationResult? llmResult)
        {
            if (rankedByRule == null || rankedByRule.Count == 0)
                return new List<ProductSummaryDto>();

            if (llmResult?.Recommendations == null || llmResult.Recommendations.Count == 0)
                return rankedByRule.Take(4).ToList();

            var byId = rankedByRule.ToDictionary(x => x.Id, x => x);

            var orderedLlmRecs = llmResult.Recommendations
                .Where(x => x != null)
                .GroupBy(x => x.ProductId)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ProductId)
                .ToList();

            var selected = new List<ProductSummaryDto>();

            foreach (var rec in orderedLlmRecs)
            {
                if (byId.TryGetValue(rec.ProductId, out var product))
                {
                    selected.Add(product);
                }
            }

            // Nếu LLM có chọn được sản phẩm hợp lệ,
            // thì trả đúng nhóm đó, không nhồi thêm rule-ranking nữa.
            if (selected.Count > 0)
            {
                return selected.Take(4).ToList();
            }

            // Chỉ fallback về rule ranking khi LLM không map được sản phẩm nào
            return rankedByRule.Take(4).ToList();
        }
    }
}