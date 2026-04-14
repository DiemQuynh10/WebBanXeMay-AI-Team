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

            var hasHardFilterChange = HasHardFilterChange(intent);
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

                            var brandReply = new StringBuilder();
                            brandReply.AppendLine($"Nếu vẫn giữ nhu cầu trước đó thì trong tầm giá hiện tại mình chưa thấy mẫu **{intent.Brand}** nào thật sự sát.");
                            brandReply.AppendLine();
                            brandReply.AppendLine($"Nếu nới nhẹ hơn một chút thì mình thấy các mẫu **{intent.Brand}** này đáng cân nhắc:");
                            brandReply.AppendLine();

                            foreach (var item in rankedRelaxed)
                            {
                                brandReply.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ): đúng hãng {intent.Brand}, còn {item.SoLuong} chiếc");
                            }

                            brandReply.AppendLine();
                            brandReply.AppendLine("Bạn có thể lọc tiếp thêm theo loại xe, cốp rộng, dễ chống chân hoặc siết lại mức giá.");

                            return new ChatResponse
                            {
                                Success = true,
                                ConversationId = conversationId,
                                UsedAI = false,
                                Reply = brandReply.ToString().Trim(),
                                Products = ChatProductCardMapper.MapMany(rankedRelaxed, 4)
                            };
                        }
                    }

                    return new ChatResponse
                    {
                        Success = true,
                        ConversationId = conversationId,
                        UsedAI = false,
                        Reply = "Trong nhóm đang xét, mình chưa thấy mẫu nào khớp thêm tiêu chí mới. Bạn có thể nới nhẹ giá hoặc đổi sang hãng khác để mình lọc tiếp."
                    };
                }
                var rankedHard = _productRecommendationService.RankProducts(
                    items,
                    intent,
                    profile,
                    normalizedMessage,
                    take: Math.Min(4, items.Count));

                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    rankedHard,
                    "refine");

                var hardReply = BuildRefineReply(rankedHard, intent, normalizedMessage);

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

                List<ProductSummaryDto> candidateProducts;

                bool shouldRefetchBroader = ShouldRefetchBroaderCandidatesForSoftRefine(normalizedMessage, intent);

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

                    candidateProducts = toolResult?.Items?
                        .Where(x => x != null)
                        .ToList() ?? new List<ProductSummaryDto>();

                    candidateProducts = ProductPriceFilterHelper.ApplyStrictPriceFilter(candidateProducts, intent);

                    if (intent.ExcludedCategories.Any())
                    {
                        candidateProducts = candidateProducts
                            .Where(x => !intent.ExcludedCategories.Any(ex => IsSameCategory(x.Loai, ex)))
                            .ToList();
                    }

                    if (intent.ExcludedBrands.Any())
                    {
                        candidateProducts = candidateProducts
                            .Where(x => !intent.ExcludedBrands.Any(ex =>
                                string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                    }

                    // nếu refetch mà không ra gì thì fallback về nhóm cũ
                    if (candidateProducts.Count == 0)
                    {
                        candidateProducts = previousProducts.ToList();
                    }

                    _logger.LogInformation(
                        "Soft refinement with broader refetch. ConversationId={ConversationId}, CandidateCount={CandidateCount}, Brand={Brand}, Category={Category}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, WantsFuelSaving={WantsFuelSaving}, WantsLargeStorage={WantsLargeStorage}, NeedsLowSeat={NeedsLowSeat}, ComparisonFeature={ComparisonFeature}",
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
                    candidateProducts = previousProducts.ToList();

                    _logger.LogInformation(
                        "Soft refinement rerank within previous products only. ConversationId={ConversationId}, CandidateCount={CandidateCount}, WantsFuelSaving={WantsFuelSaving}, WantsLargeStorage={WantsLargeStorage}, NeedsLowSeat={NeedsLowSeat}, ComparisonFeature={ComparisonFeature}",
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
                var rankedSoft = _productRecommendationService.RankProducts(
                    candidateProducts,
                    intent,
                    profile,
                    normalizedMessage,
                    take: Math.Min(4, candidateProducts.Count));

                if (rankedSoft == null || rankedSoft.Count == 0)
                {
                    return new ChatResponse
                    {
                        Success = true,
                        ConversationId = conversationId,
                        UsedAI = false,
                        Reply = "Trong nhóm đang xét, mình chưa thấy mẫu nào nổi bật hơn theo tiêu chí mới. Bạn có thể đổi thêm hãng, giá hoặc loại xe để mình lọc tiếp."
                    };
                }

                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    rankedSoft,
                    "refine");

                var softReply = BuildRefineReply(rankedSoft, intent, normalizedMessage);

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

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                filtered = filtered.Where(x =>
     !intent.ExcludedBrands.Any(ex => string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)));
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

            filteredList = ProductPriceFilterHelper.ApplyStrictPriceFilter(filteredList, intent);

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
                return null;
            }

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                "refine");

            var reply = BuildRefineReply(ranked, intent, normalizedMessage);

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
        private static string BuildRefineIntro(string refineMode, ParsedIntent intent, string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            if (refineMode == "expand")
            {
                if (!string.IsNullOrWhiteSpace(intent.Brand))
                {
                    return $"Nếu giữ nhu cầu trước đó và ưu tiên thêm {intent.Brand}, mình thấy các mẫu này khá đáng chú ý:";
                }

                if (!string.IsNullOrWhiteSpace(intent.Category))
                {
                    return $"Nếu giữ nhu cầu trước đó và lọc thêm theo {intent.Category}, mình thấy các mẫu này khá phù hợp:";
                }

                return "Nếu giữ nhu cầu trước đó và lọc tiếp theo tiêu chí mới, mình thấy các mẫu này khá đáng chú ý:";
            }

            if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
            {
                return $"Nếu lọc hẹp hơn theo mức giá dưới {intent.PriceMax.Value:N0} VNĐ, hiện mình thấy các mẫu này phù hợp hơn:";
            }

            if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return $"Nếu lọc hẹp hơn theo khoảng giá từ {intent.PriceMin.Value:N0} đến {intent.PriceMax.Value:N0} VNĐ, hiện mình thấy các mẫu này phù hợp hơn:";
            }

            if (text.Contains("cốp rộng") || text.Contains("cop rong"))
            {
                return "Nếu ưu tiên cốp rộng hơn trong nhóm đang xét, mình thấy các mẫu này đáng cân nhắc hơn:";
            }

            if (text.Contains("dễ chống chân") || text.Contains("de chong chan"))
            {
                return "Nếu ưu tiên dễ chống chân hơn trong nhóm đang xét, mình thấy các mẫu này phù hợp hơn:";
            }

            return "Nếu lọc hẹp hơn từ nhóm trước, hiện mình thấy các mẫu này phù hợp hơn:";
        }
        private string BuildRefineReply(
    IReadOnlyList<ProductSummaryDto> products,
    ParsedIntent intent,
    string message)
        {
            var refineMode = DetectRefineMode(message, intent);
            var intro = BuildRefineIntro(refineMode, intent, message);

            var sb = new StringBuilder();
            sb.AppendLine(intro);
            sb.AppendLine();

            foreach (var item in products)
            {
                var reason = BuildSimpleRefineReason(item, intent, message);
                sb.AppendLine($"- **{item.Ten}** ({item.Gia:N0} VNĐ): {reason}");
            }

            sb.AppendLine();
            sb.AppendLine("Bạn có thể lọc tiếp thêm một chút nữa như đổi hãng, siết giá hoặc thêm tiêu chí sử dụng.");

            return sb.ToString().Trim();
        }
        private static string BuildSimpleRefineReason(
    ProductSummaryDto item,
    ParsedIntent intent,
    string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(intent.Brand))
                return $"đúng hãng {intent.Brand}, còn {item.SoLuong} chiếc";

            if (!string.IsNullOrWhiteSpace(intent.Category))
                return $"đúng nhóm {intent.Category}, còn {item.SoLuong} chiếc";

            if (intent.PriceMax.HasValue || intent.PriceMin.HasValue)
                return $"nằm trong mức giá đang lọc, còn {item.SoLuong} chiếc";

            if (text.Contains("cốp rộng") || text.Contains("cop rong"))
                return $"đang là một lựa chọn đáng cân nhắc khi ưu tiên cốp rộng";

            if (text.Contains("dễ chống chân") || text.Contains("de chong chan"))
                return $"đang là một lựa chọn đáng cân nhắc khi ưu tiên dễ chống chân";

            return $"đang là một phương án phù hợp hơn sau khi lọc tiếp";
        }
        private static bool HasHardFilterChange(ParsedIntent intent)
        {
            return
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                intent.ExcludedCategories.Any() ||
                intent.ExcludedBrands.Any();
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
    }
}