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
        private readonly IRecommendationLLMService _recommendationLLMService;
        private readonly IReplyStyleService _replyStyleService;
        private readonly IReplyRewriteService _replyRewriteService;
        public RefinementService(
     IWebBanXeMayToolClient toolClient,
     IProductRecommendationService productRecommendationService,
     IConversationPreferenceService conversationPreferenceService,
     IRecommendationLLMService recommendationLLMService,
     IReplyStyleService replyStyleService,
     IReplyRewriteService replyRewriteService,
     ILogger<RefinementService> logger)
        {
            _toolClient = toolClient;
            _productRecommendationService = productRecommendationService;
            _conversationPreferenceService = conversationPreferenceService;
            _recommendationLLMService = recommendationLLMService;
            _replyStyleService = replyStyleService;
            _replyRewriteService = replyRewriteService;
            _logger = logger;
        }
        private async Task<List<ProductSummaryDto>> LoadPreviousProductsAsync(
    HashSet<string> allowedNames)
        {
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

            return previousProducts
                .Where(x => !string.IsNullOrWhiteSpace(x.Ten) && allowedNames.Contains(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
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

            var previousProducts = await LoadPreviousProductsAsync(allowedNames);

            if (previousProducts.Count == 0)
            {
                return null;
            }

            var hasHardFilterChange = HasHardFilterChange(normalizedMessage, intent);
            var hasSoftPreferenceChange = HasSoftPreferenceChange(normalizedMessage, intent);
            _logger.LogInformation(
    "Refinement entry. ConversationId={ConversationId}, Message={Message}, Brand={Brand}, Category={Category}, ExcludedCategories={ExcludedCategories}, ExcludedBrands={ExcludedBrands}, PriceMin={PriceMin}, PriceMax={PriceMax}, TargetPrice={TargetPrice}, FilterType={FilterType}, HardChange={HardChange}, SoftChange={SoftChange}",
    conversationId,
    normalizedMessage,
    intent.Brand,
    intent.Category,
    string.Join(",", intent.ExcludedCategories),
    string.Join(",", intent.ExcludedBrands),
    intent.PriceMin,
    intent.PriceMax,
    intent.TargetPrice,
    intent.FilterType,
    hasHardFilterChange,
    hasSoftPreferenceChange);
            // =========================
            // NHÁNH 1: HARD-FILTER REFINE
            // ví dụ: còn honda thì sao / xe ga thôi / dưới 35 triệu thôi
            // =========================
            if (hasHardFilterChange)
            {
                decimal? minPrice = intent.PriceMin;
                decimal? maxPrice = intent.PriceMax;

                if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
                {
                    var delta = ProductPriceFilterHelper.GetAroundDelta(intent.TargetPrice.Value);
                    minPrice = intent.TargetPrice.Value - delta;
                    maxPrice = intent.TargetPrice.Value + delta;
                }

                var toolResult = await _toolClient.GetProductsByFiltersAsync(
                    brand: intent.Brand,
                    minPrice: minPrice,
                    maxPrice: maxPrice,
                    category: intent.Category,
                    take: 30);

                var items = toolResult?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();

                items = ProductPriceFilterHelper.ApplyStrictPriceFilter(items, intent);
                if (!string.IsNullOrWhiteSpace(intent.Category))
                {
                    if (intent.Category.Contains("số", StringComparison.OrdinalIgnoreCase) ||
                        intent.Category.Contains("so", StringComparison.OrdinalIgnoreCase))
                    {
                        items = items
                            .Where(x => IsSameCategory(x.Loai, "xe số"))
                            .ToList();
                    }
                    else if (intent.Category.Contains("ga", StringComparison.OrdinalIgnoreCase))
                    {
                        items = items
                            .Where(x => IsSameCategory(x.Loai, "xe ga"))
                            .ToList();
                    }
                    else if (intent.Category.Contains("côn", StringComparison.OrdinalIgnoreCase) ||
                             intent.Category.Contains("con", StringComparison.OrdinalIgnoreCase))
                    {
                        items = items
                            .Where(x => IsSameCategory(x.Loai, "côn tay"))
                            .ToList();
                    }
                }

                if (intent.ExcludedCategories.Any())
                {
                    items = items
                        .Where(x => !intent.ExcludedCategories.Any(ex => IsSameCategory(x.Loai, ex)))
                        .ToList();
                }

                if (intent.ExcludedBrands.Any())
                {
                    items = items
                        .Where(x => !intent.ExcludedBrands.Any(ex =>
                            string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                if (items.Count == 0)
                {
                    bool isBrandSwitchOnly =
                        !string.IsNullOrWhiteSpace(intent.Brand) &&
                        string.IsNullOrWhiteSpace(intent.Category) &&
                        !intent.ExcludedBrands.Any() &&
                        !intent.ExcludedCategories.Any();

                    if (isBrandSwitchOnly)
                    {
                        decimal? relaxedMin = null;
                        decimal? relaxedMax = maxPrice.HasValue ? maxPrice.Value + 8_000_000m : null;
        

                        var relaxedToolResult = await _toolClient.GetProductsByFiltersAsync(
                            brand: intent.Brand,
                            minPrice: relaxedMin,
                            maxPrice: relaxedMax,
                            category: intent.Category,
                            take: 20);

                        var relaxedItems = relaxedToolResult?.Items?
                            .Where(x => x != null)
                            .ToList() ?? new List<ProductSummaryDto>();

                        if (relaxedItems.Count > 0)
                        {
                            var rankedRelaxed = _productRecommendationService.RankProducts(
                                relaxedItems,
                                intent,
                                profile,
                                normalizedMessage,
                                take: Math.Min(3, relaxedItems.Count));

                            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                                conversationId,
                                rankedRelaxed,
                                "refine");

                            var brandDraftReply = _replyStyleService.BuildRefinementBrandRelaxedReply(
     rankedRelaxed,
     intent);

                            var brandReply = await _replyRewriteService.RewriteAsync(
                                normalizedMessage,
                                brandDraftReply);

                            return new ChatResponse
                            {
                                Success = true,
                                ConversationId = conversationId,
                                UsedAI = false,
                                Reply = brandReply,
                                Products = ChatProductCardMapper.MapMany(rankedRelaxed, 4)
                            };
                        }
                    }
                    var draftNoMatch = BuildSmartRefinementNoMatchReply(intent, profile);

                    var noMatchReply = await _replyRewriteService.RewriteAsync(
                        normalizedMessage,
                        draftNoMatch);

                    return new ChatResponse
                    {
                        Success = true,
                        ConversationId = conversationId,
                        UsedAI = false,
                        Reply = noMatchReply
                    };
                }
                var rankedHard = _productRecommendationService.RankProducts(
                    items,
                    intent,
                    profile,
                    normalizedMessage,
                    take: Math.Min(4, items.Count));
                _logger.LogInformation(
    "Hard refinement rule ranking completed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, RankedHardCount={RankedHardCount}",
    conversationId,
    items.Count,
    rankedHard.Count);
                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    rankedHard,
                    "refine");

                var hardDraftReply = _replyStyleService.BuildRefinementReply(
     rankedHard,
     intent,
     normalizedMessage,
     item => BuildSimpleRefineReason(item, intent, normalizedMessage));

                var hardReply = await _replyRewriteService.RewriteAsync(
                    normalizedMessage,
                    hardDraftReply);
                _logger.LogInformation(
    "Hard refinement final result. ConversationId={ConversationId}, FinalProductCount={FinalProductCount}",
    conversationId,
    rankedHard.Count);
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = hardReply,
                    Products = ChatProductCardMapper.MapMany(rankedHard, 4)
                };
            }

            if (hasSoftPreferenceChange)
            {
                EnrichSoftPreferenceIntent(normalizedMessage, intent);

                List<ProductSummaryDto> candidateProducts = previousProducts.ToList();

                // Bước 1: luôn ưu tiên current set trước
                var shouldRefetchBroader = ShouldRefetchBroaderForSoftRefine(candidateProducts, intent);

                if (shouldRefetchBroader)
                {
                    decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
                    decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;

                    // Carry lại target price từ profile để bộ rank giữ được "trọng tâm giá" gần nhất
                    if (!intent.TargetPrice.HasValue && profile.TargetPrice.HasValue)
                    {
                        intent.TargetPrice = profile.TargetPrice;
                    }

                    if (intent.FilterType == PriceFilterType.None && profile.FilterType != PriceFilterType.None)
                    {
                        intent.FilterType = profile.FilterType;
                    }

                    string? brand = !string.IsNullOrWhiteSpace(intent.Brand) ? intent.Brand : profile.PreferredBrand;
                    string? category = !string.IsNullOrWhiteSpace(intent.Category) ? intent.Category : profile.PreferredCategory;

                    if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
                    {
                        var delta = ProductPriceFilterHelper.GetAroundDelta(intent.TargetPrice.Value);
                        minPrice = intent.TargetPrice.Value - delta;
                        maxPrice = intent.TargetPrice.Value + delta;
                    }
                    else if (profile.FilterType == PriceFilterType.Around && profile.TargetPrice.HasValue &&
                             !intent.PriceMin.HasValue && !intent.PriceMax.HasValue && !intent.TargetPrice.HasValue)
                    {
                        var delta = ProductPriceFilterHelper.GetAroundDelta(profile.TargetPrice.Value);
                        minPrice = profile.TargetPrice.Value - delta;
                        maxPrice = profile.TargetPrice.Value + delta;
                    }

                    var toolResult = await _toolClient.GetProductsByFiltersAsync(
                        brand: brand,
                        minPrice: minPrice,
                        maxPrice: maxPrice,
                        category: category,
                        take: 30);

                    var broaderCandidates = toolResult?.Items?
                        .Where(x => x != null)
                        .ToList() ?? new List<ProductSummaryDto>();

                    broaderCandidates = ProductPriceFilterHelper.ApplyStrictPriceFilter(broaderCandidates, intent);

                    if (intent.ExcludedCategories.Any())
                    {
                        broaderCandidates = broaderCandidates
                            .Where(x => !intent.ExcludedCategories.Any(ex => IsSameCategory(x.Loai, ex)))
                            .ToList();
                    }

                    if (intent.ExcludedBrands.Any())
                    {
                        broaderCandidates = broaderCandidates
                            .Where(x => !intent.ExcludedBrands.Any(ex =>
                                string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                    }

                    // Chỉ dùng broader set nếu nó thực sự có dữ liệu
                    if (broaderCandidates.Count > 0)
                    {
                        candidateProducts = broaderCandidates;
                    }

                    _logger.LogInformation(
                        "Soft refinement broader refetch executed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, Brand={Brand}, Category={Category}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, WantsFuelSaving={WantsFuelSaving}, WantsLargeStorage={WantsLargeStorage}, NeedsLowSeat={NeedsLowSeat}, ComparisonFeature={ComparisonFeature}",
                        conversationId,
                        candidateProducts.Count,
                        brand,
                        category,
                        minPrice,
                        maxPrice,
                        intent.WantsFuelSaving,
                        intent.WantsLargeStorage,
                        intent.NeedsLowSeat,
                        intent.ComparisonFeature);
                }
                else
                {
                    _logger.LogInformation(
                        "Soft refinement using current set first. ConversationId={ConversationId}, CandidateCount={CandidateCount}, WantsFuelSaving={WantsFuelSaving}, WantsLargeStorage={WantsLargeStorage}, NeedsLowSeat={NeedsLowSeat}, ComparisonFeature={ComparisonFeature}",
                        conversationId,
                        candidateProducts.Count,
                        intent.WantsFuelSaving,
                        intent.WantsLargeStorage,
                        intent.NeedsLowSeat,
                        intent.ComparisonFeature);
                }
                // Với soft refine, vẫn giữ candidate gần cửa sổ giá hiện tại của profile
                if (profile.PriceMin.HasValue || profile.PriceMax.HasValue)
                {
                    var guardMin = profile.PriceMin.HasValue ? profile.PriceMin.Value - 3_000_000m : decimal.MinValue;
                    var guardMax = profile.PriceMax.HasValue ? profile.PriceMax.Value + 3_000_000m : decimal.MaxValue;

                    candidateProducts = candidateProducts
                        .Where(x => x.Gia >= guardMin && x.Gia <= guardMax)
                        .ToList();

                    if (candidateProducts.Count == 0)
                    {
                        candidateProducts = previousProducts.ToList();
                    }
                }
                var rankedSoftByRule = _productRecommendationService.RankProducts(
      candidateProducts,
      intent,
      profile,
      normalizedMessage,
      take: Math.Min(5, candidateProducts.Count));
                _logger.LogInformation(
    "Soft refinement rule ranking completed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, RankedByRuleCount={RankedByRuleCount}",
    conversationId,
    candidateProducts.Count,
    rankedSoftByRule.Count);
                var rankedSoft = rankedSoftByRule;

                if (rankedSoftByRule.Count >= 3)
                {
                    try
                    {
                        var llmResult = await _recommendationLLMService.RerankAsync(
                            normalizedMessage,
                            intent,
                            profile,
                            rankedSoftByRule);

                        var llmRanked = ApplyLlmRerank(
                            rankedSoftByRule,
                            llmResult,
                            Math.Min(4, rankedSoftByRule.Count));

                        if (llmRanked.Count > 0)
                        {
                            _logger.LogInformation(
                                "Soft refinement LLM rerank applied. ConversationId={ConversationId}, RuleCount={RuleCount}, LlmSelectedCount={LlmSelectedCount}",
                                conversationId,
                                rankedSoftByRule.Count,
                                llmRanked.Count);

                            rankedSoft = llmRanked;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "LLM rerank failed in RefinementService. Fallback to rule ranking.");
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "Skip LLM rerank in soft refinement because only {RuleCount} rule-ranked candidates remain. ConversationId={ConversationId}",
                        rankedSoftByRule.Count,
                        conversationId);
                }

                if (rankedSoft == null || rankedSoft.Count == 0)
                {
                    var draftNoMatch = BuildSmartRefinementNoMatchReply(intent, profile);

                    var noMatchReply = await _replyRewriteService.RewriteAsync(
                        normalizedMessage,
                        draftNoMatch);

                    return new ChatResponse
                    {
                        Success = true,
                        ConversationId = conversationId,
                        UsedAI = false,
                        Reply = noMatchReply
                    };
                }

                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    rankedSoft,
                    "refine");

                var softDraftReply = _replyStyleService.BuildRefinementReply(
     rankedSoft,
     intent,
     normalizedMessage,
     item => BuildSimpleRefineReason(item, intent, normalizedMessage));

                var softReply = await _replyRewriteService.RewriteAsync(
                    normalizedMessage,
                    softDraftReply);
                _logger.LogInformation(
    "Soft refinement final result. ConversationId={ConversationId}, FinalProductCount={FinalProductCount}, ComparisonFeature={ComparisonFeature}",
    conversationId,
    rankedSoft.Count,
    intent.ComparisonFeature);
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = softReply,
                    Products = ChatProductCardMapper.MapMany(rankedSoft, 4)
                };
            }
            IEnumerable<ProductSummaryDto> filtered = previousProducts;

            if (intent.ExcludedBrands.Any())
            {
                filtered = filtered.Where(x =>
                    !intent.ExcludedBrands.Any(ex => string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)));
            }

            if (intent.ExcludedCategories.Any())
            {
                filtered = filtered.Where(x =>
                    !intent.ExcludedCategories.Any(ex => IsSameCategory(x.Loai, ex)));
            }

            var filteredList = filtered.ToList();

            if (filteredList.Count == 0)
            {
                var draftNoMatch = BuildSmartRefinementNoMatchReply(intent, profile);

                var noMatchReply = await _replyRewriteService.RewriteAsync(
                    normalizedMessage,
                    draftNoMatch);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = noMatchReply
                };
            }

            filteredList = ProductPriceFilterHelper.ApplyStrictPriceFilter(filteredList, intent);

            if (filteredList.Count == 0)
            {
                var draftNoMatch = _replyStyleService.BuildRefinementNoMatchReply(intent);

                var noMatchReply = await _replyRewriteService.RewriteAsync(
                    normalizedMessage,
                    draftNoMatch);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = noMatchReply
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

            if (intent.WantsLargeStorage || intent.WantsFuelSaving || intent.NeedsLowSeat || !string.IsNullOrWhiteSpace(intent.ComparisonFeature))
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
                var draftNoMatch = BuildSmartRefinementNoMatchReply(intent, profile);

                var noMatchReply = await _replyRewriteService.RewriteAsync(
                    normalizedMessage,
                    draftNoMatch);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = noMatchReply
                };
            }

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                "refine");

            var draftReply = _replyStyleService.BuildRefinementReply(
      ranked,
      intent,
      normalizedMessage,
      item => BuildSimpleRefineReason(item, intent, normalizedMessage));

            var reply = await _replyRewriteService.RewriteAsync(
                normalizedMessage,
                draftReply);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(ranked, 4)
            };
        }
        private static void EnrichSoftPreferenceIntent(string normalizedMessage, ParsedIntent intent)
        {
            if (normalizedMessage.Contains("cốp rộng", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("cop rong", StringComparison.OrdinalIgnoreCase))
            {
                intent.WantsLargeStorage = true;
                intent.ComparisonFeature ??= "storage";
            }

            if (normalizedMessage.Contains("dễ chống chân", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("de chong chan", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("yên thấp", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("yen thap", StringComparison.OrdinalIgnoreCase))
            {
                intent.NeedsLowSeat = true;
                intent.WantsEasyControl = true;
                intent.ComparisonFeature ??= "low_seat";
            }

            if (normalizedMessage.Contains("tiết kiệm xăng", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("tiet kiem xang", StringComparison.OrdinalIgnoreCase))
            {
                intent.WantsFuelSaving = true;
                intent.ComparisonFeature ??= "fuel_saving";
            }

            if (normalizedMessage.Contains("đi làm", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("di lam", StringComparison.OrdinalIgnoreCase))
            {
                intent.ForWork = true;
                intent.ComparisonFeature ??= "work_fit";
            }

            if (normalizedMessage.Contains("đi học", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("di hoc", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("sinh viên", StringComparison.OrdinalIgnoreCase) ||
                normalizedMessage.Contains("sinh vien", StringComparison.OrdinalIgnoreCase))
            {
                intent.ForSchool = true;
                intent.ComparisonFeature ??= "school_fit";
            }
        }
        private static int GetSoftPreferenceMatchScore(ProductSummaryDto item, ParsedIntent intent)
        {
            if (item == null) return 0;

            int score = 0;
            var name = item.Ten ?? string.Empty;

            if (intent.WantsLargeStorage)
            {
                if (name.Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Address", StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            if (intent.NeedsLowSeat || intent.WantsEasyControl)
            {
                if (name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Latte", StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            if (intent.WantsFuelSaving)
            {
                if (name.Contains("Wave", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Future", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Sirius", StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                string.Equals(item.ThuongHieu, intent.Brand, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }

            if (!string.IsNullOrWhiteSpace(intent.Category) &&
                IsSameCategory(item.Loai, intent.Category))
            {
                score += 1;
            }

            return score;
        }

        private static bool ShouldRefetchBroaderForSoftRefine(
     List<ProductSummaryDto> currentProducts,
     ParsedIntent intent)
        {
            if (currentProducts == null || currentProducts.Count == 0)
                return true;

            if (currentProducts.Count <= 1)
                return true;

            var matchCount = currentProducts.Count(x => GetSoftPreferenceMatchScore(x, intent) > 0);

            // 1) Nếu có ít nhất 2 mẫu match thì chắc chắn giữ current set
            if (matchCount >= 2)
                return false;

            // 2) Nếu current set chỉ có 2 mẫu mà đã có 1 mẫu match,
            //    thì vẫn ưu tiên current set trước để tránh refetch quá sớm
            if (currentProducts.Count <= 2 && matchCount >= 1)
                return false;

            // 3) Nếu đang là refine mềm theo đúng ngữ cảnh hiện tại
            //    và current set không quá nhỏ, cho current set một cơ hội trước
            if ((intent.WantsLargeStorage || intent.WantsFuelSaving || intent.NeedsLowSeat || intent.WantsEasyControl)
                && currentProducts.Count >= 3
                && matchCount >= 1)
            {
                return false;
            }

            return true;
        }
        private static bool ShouldRefetchBroaderCandidatesForSoftRefine(string message, ParsedIntent intent)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            return
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("yên thấp") ||
                text.Contains("yen thap") ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("đi học") ||
                text.Contains("di hoc");
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
        private static List<ProductSummaryDto> ApplyLlmRerank(
     IReadOnlyList<ProductSummaryDto> rankedByRule,
     LLMRecommendationResult? llmResult,
     int take)
        {
            if (rankedByRule == null || rankedByRule.Count == 0)
                return new List<ProductSummaryDto>();

            if (llmResult?.Recommendations == null || llmResult.Recommendations.Count == 0)
                return rankedByRule.Take(take).ToList();

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

            if (selected.Count > 0)
            {
                return selected.Take(take).ToList();
            }

            return rankedByRule.Take(take).ToList();
        }
       
        private static string DetectRefineMode(string message, ParsedIntent intent)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool hasHardFilterChange =
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue;

            bool hasSoftNarrowSignal =
                text.Contains("rẻ hơn") ||
                text.Contains("re hon") ||
                text.Contains("dưới ") ||
                text.Contains("duoi ") ||
                text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang");

            bool looksExpand =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.Contains("thì sao") ||
                text.Contains("thi sao") ||
                text.Contains("loại khác") ||
                text.Contains("loai khac");

            if (hasSoftNarrowSignal)
                return "narrow";

            if (hasHardFilterChange && looksExpand)
                return "expand";

            if (hasHardFilterChange)
                return "expand";

            return "narrow";
        }
     
        private static string BuildSimpleRefineReason(
     ProductSummaryDto item,
     ParsedIntent intent,
     string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            // 1. Ưu tiên tiêu chí mềm trước
            if (text.Contains("cốp rộng") || text.Contains("cop rong"))
                return "mẫu này đáng cân nhắc hơn nếu bạn ưu tiên cốp rộng và tiện mang đồ";

            if (text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
                text.Contains("yên thấp") || text.Contains("yen thap"))
                return "mẫu này dễ phù hợp hơn nếu bạn ưu tiên dễ chống chân và dễ làm quen";

            if (text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang"))
                return "mẫu này đáng cân nhắc hơn nếu bạn ưu tiên tiết kiệm xăng";

            if (text.Contains("đi làm") || text.Contains("di lam"))
                return "mẫu này hợp hơn nếu bạn ưu tiên đi làm hằng ngày";

            if (text.Contains("đi học") || text.Contains("di hoc") ||
                text.Contains("sinh viên") || text.Contains("sinh vien"))
                return "mẫu này hợp hơn nếu bạn ưu tiên đi học hằng ngày";

            // 2. Sau đó mới fallback sang brand / category / price
            if (!string.IsNullOrWhiteSpace(intent.Brand))
                return $"mẫu này đúng hãng {intent.Brand} và hiện còn {item.SoLuong} chiếc";

            if (!string.IsNullOrWhiteSpace(intent.Category))
                return $"mẫu này đúng nhóm {intent.Category} và hiện còn {item.SoLuong} chiếc";

            if (intent.PriceMax.HasValue || intent.PriceMin.HasValue || intent.TargetPrice.HasValue)
                return $"mẫu này đang nằm khá sát mức giá bạn vừa lọc và hiện còn {item.SoLuong} chiếc";

            return "mẫu này đang là phương án phù hợp hơn sau khi lọc tiếp";
        }
        private static bool HasHardFilterChange(string message, ParsedIntent intent)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool mentionsBrandInCurrentTurn =
                !string.IsNullOrWhiteSpace(intent.Brand) &&
                text.Contains((intent.Brand ?? string.Empty).ToLowerInvariant());

            bool mentionsCategoryInCurrentTurn =
                (!string.IsNullOrWhiteSpace(intent.Category) &&
                 (text.Contains("xe ga") ||
                  text.Contains("xe số") ||
                  text.Contains("xe so") ||
                  text.Contains("côn tay") ||
                  text.Contains("con tay")));

            bool hasExplicitBrandOrCategoryThisTurn =
                mentionsBrandInCurrentTurn ||
                mentionsCategoryInCurrentTurn ||
                intent.ExcludedCategories.Any() ||
                intent.ExcludedBrands.Any();

            bool hasExplicitPricePhrase =
                text.Contains("dưới ") ||
                text.Contains("duoi ") ||
                text.Contains("trên ") ||
                text.Contains("tren ") ||
                text.Contains("từ ") ||
                text.Contains("tu ") ||
                text.Contains("đến ") ||
                text.Contains("den ") ||
                text.Contains("khoảng ") ||
                text.Contains("khoang ") ||
                text.Contains("tầm ") ||
                text.Contains("tam ") ||
                text.Contains("quanh ") ||
                text.Contains("triệu") ||
                text.Contains("trieu") ||
                text.Contains("tr");

            bool hasPriceIntent =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                intent.FilterType != PriceFilterType.None;

            bool isSoftOnlyPhrase =
                text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("yên thấp") ||
                text.Contains("yen thap") ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("đi êm") ||
                text.Contains("di em");

            if (isSoftOnlyPhrase && !hasExplicitBrandOrCategoryThisTurn && !hasExplicitPricePhrase)
                return false;

            return hasExplicitBrandOrCategoryThisTurn || (hasPriceIntent && hasExplicitPricePhrase);
        }
        private static bool HasSoftPreferenceChange(string message, ParsedIntent intent)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            return
                intent.WantsLargeStorage ||
                intent.WantsFuelSaving ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang");
        }
        private string BuildSmartRefinementNoMatchReply(ParsedIntent intent, CustomerPreferenceProfile profile)
        {
            // Ưu tiên giải thích theo brand + category + budget
            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                !string.IsNullOrWhiteSpace(intent.Category) &&
                (intent.PriceMax.HasValue || intent.PriceMin.HasValue || intent.TargetPrice.HasValue))
            {
                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return $"Hiện tại {intent.Brand} gần như không có mẫu {intent.Category} trong tầm dưới {intent.PriceMax.Value:N0} VNĐ. Bạn có thể tăng ngân sách hoặc đổi sang hãng khác để mình lọc tiếp.";
                }

                if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
                {
                    return $"Hiện tại {intent.Brand} gần như không có mẫu {intent.Category} trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
                }

                return $"Hiện tại {intent.Brand} gần như không có mẫu {intent.Category} thật sự phù hợp với mức giá bạn đang nhắm tới. Bạn có thể tăng ngân sách hoặc đổi sang hãng khác để mình lọc tiếp.";
            }

            if (!string.IsNullOrWhiteSpace(intent.Category) &&
                (intent.PriceMax.HasValue || intent.PriceMin.HasValue || intent.TargetPrice.HasValue))
            {
                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return $"Hiện tại nhóm {intent.Category} trong tầm dưới {intent.PriceMax.Value:N0} VNĐ khá ít lựa chọn với tiêu chí này. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
                }
            }
            if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return $"Hiện tại nhóm {intent.Category} trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ khá ít lựa chọn với tiêu chí này. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
            }

            if (intent.TargetPrice.HasValue)
            {
                return $"Hiện tại nhóm {intent.Category} quanh mức {intent.TargetPrice.Value:N0} VNĐ khá ít lựa chọn với tiêu chí này. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
            }

            if (intent.PriceMin.HasValue && !intent.PriceMax.HasValue)
            {
                return $"Hiện tại nhóm {intent.Category} từ {intent.PriceMin.Value:N0} VNĐ trở lên vẫn chưa có nhiều lựa chọn thật sự phù hợp với tiêu chí này. Bạn có thể đổi hãng hoặc điều chỉnh thêm điều kiện để mình lọc tiếp.";
            }
            return _replyStyleService.BuildRefinementNoMatchReply(intent);
        }
    }
}