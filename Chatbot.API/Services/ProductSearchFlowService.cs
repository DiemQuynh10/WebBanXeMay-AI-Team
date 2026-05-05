using System.Text;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ProductSearchFlowService : IProductSearchFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly ILogger<ProductSearchFlowService> _logger;
        private readonly IReplyStyleService _replyStyleService;

        public ProductSearchFlowService(
     IWebBanXeMayToolClient toolClient,
     IConversationPreferenceService conversationPreferenceService,
     IProductRecommendationService productRecommendationService,
     IReplyStyleService replyStyleService,
     ILogger<ProductSearchFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _productRecommendationService = productRecommendationService;
            _replyStyleService = replyStyleService;
            _logger = logger;
        }
        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            static HashSet<string> BuildExcludedBrands(ParsedIntent currentIntent, CustomerPreferenceProfile currentProfile)
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

            static HashSet<string> BuildExcludedCategories(ParsedIntent currentIntent, CustomerPreferenceProfile currentProfile)
            {
                var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var category in currentIntent.ExcludedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(category))
                        excluded.Add(category.Trim());
                }

                foreach (var category in currentProfile.ExcludedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(category))
                        excluded.Add(category.Trim());
                }

                return excluded;
            }

            static List<ProductSummaryDto> ApplyStrictExclusions(
                IEnumerable<ProductSummaryDto> products,
                HashSet<string> excludedBrands,
                HashSet<string> excludedCategories)
            {
                var filtered = products
                    .Where(x => x != null)
                    .Where(x => !excludedBrands.Contains(x.ThuongHieu?.Trim() ?? string.Empty))
                    .ToList();

                if (excludedCategories.Count == 0)
                    return filtered;

                return filtered
                    .Where(x => !excludedCategories.Contains(x.Loai?.Trim() ?? string.Empty))
                    .ToList();
            }

            bool explicitListSearch =
    intent.IsProductSearch ||
    string.Equals(intent.IntentType, "product_search", StringComparison.OrdinalIgnoreCase);

            var excludedBrands = explicitListSearch && !MessageHasExplicitNegativeBrand(normalizedMessage)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : BuildExcludedBrands(intent, profile);

            var excludedCategories = explicitListSearch && !MessageHasExplicitNegativeCategory(normalizedMessage)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : BuildExcludedCategories(intent, profile);

            var hasIntentBrandExclusions = intent.ExcludedBrands.Any();

            bool hasSearchSignals =
                intent.IsProductSearch ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                !string.IsNullOrWhiteSpace(intent.Brand) ||
                !string.IsNullOrWhiteSpace(intent.Category) ||
                intent.ExcludedBrands.Any() ||
                intent.ExcludedCategories.Any() ||
                intent.ExcludedProducts.Any();

            if (!hasSearchSignals)
            {
                return null;
            }

            bool currentMessageHasCategory = MessageHasExplicitCategory(normalizedMessage);
            bool currentMessageHasBrand = MessageHasExplicitBrand(normalizedMessage);

            var effectiveBrand = !string.IsNullOrWhiteSpace(intent.Brand)
     ? intent.Brand
     : explicitListSearch
         ? null
         : currentMessageHasCategory && !currentMessageHasBrand
             ? null
             : profile.PreferredBrand;

            var effectiveCategory = !string.IsNullOrWhiteSpace(intent.Category)
                ? intent.Category
                : explicitListSearch
                    ? null
                    : profile.PreferredCategory;

            if (ProductExclusionHelper.IsBrandExcludedByIntentOrProfile(effectiveBrand, intent, profile))
            {
                effectiveBrand = null;
            }

            if (ProductExclusionHelper.IsCategoryExcludedByIntentOrProfile(effectiveCategory, intent, profile))
            {
                effectiveCategory = null;
            }
            bool shouldCarryProfileBudget =
       profile.HasActiveRecommendationContext &&
       !intent.PriceMin.HasValue &&
       !intent.PriceMax.HasValue &&
       !intent.TargetPrice.HasValue &&
       (
           intent.IsFollowUp ||
           MessageHasExplicitCategory(normalizedMessage) ||
           string.Equals(intent.FollowUpType, "none", StringComparison.OrdinalIgnoreCase)
       );

            var effectiveMinPrice = intent.PriceMin ?? (shouldCarryProfileBudget ? profile.PriceMin : null);
            var effectiveMaxPrice = intent.PriceMax ?? (shouldCarryProfileBudget ? profile.PriceMax : null);
            var effectiveFilterType = intent.FilterType != PriceFilterType.None
    ? intent.FilterType
    : shouldCarryProfileBudget
        ? profile.FilterType
        : PriceFilterType.None;
            _logger.LogInformation(
    "ProductSearch effective filters. ConversationId: {ConversationId}, Brand: {Brand}, Category: {Category}, MinPrice: {MinPrice}, MaxPrice: {MaxPrice}, IntentType: {IntentType}, FilterType: {FilterType}",
    conversationId,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    intent.IntentType,
   effectiveFilterType);
            bool isGeneralListRequest =
    string.IsNullOrWhiteSpace(effectiveBrand) &&
    string.IsNullOrWhiteSpace(effectiveCategory) &&
    !effectiveMinPrice.HasValue &&
    !effectiveMaxPrice.HasValue &&
    !intent.TargetPrice.HasValue &&
    LooksLikeGeneralProductListRequest(normalizedMessage);

            if (isGeneralListRequest)
            {
                var generalResult = await _toolClient.GetProductsByFiltersAsync(
                    brand: null,
                    minPrice: null,
                    maxPrice: null,
                    category: null,
                    take: 10);

                var generalItems = generalResult?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();

                await SaveSearchContextAsync(conversationId, generalItems, intent);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = "Mình tìm thấy một số mẫu xe hiện có trong cửa hàng:",
                    Products = generalItems.Take(5).Select(MapToCard).ToList()
                };
            }
            ProductSearchResponseDto? result;

            // Ưu tiên gọi đúng tool theo độ đặc hiệu
            if (!string.IsNullOrWhiteSpace(effectiveBrand) &&
                effectiveMaxPrice.HasValue &&
                !effectiveMinPrice.HasValue)
            {
                result = await _toolClient.GetProductsByBrandAndPriceAsync(
                    effectiveBrand,
                    effectiveMaxPrice.Value,
                    effectiveCategory,
                    10);
            }
            else if (!string.IsNullOrWhiteSpace(effectiveBrand) ||
                     !string.IsNullOrWhiteSpace(effectiveCategory) ||
                     effectiveMinPrice.HasValue ||
                     effectiveMaxPrice.HasValue)
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    effectiveBrand,
                    effectiveMinPrice,
                    effectiveMaxPrice,
                    effectiveCategory,
                    10);
            }
            else
            {
                result = await _toolClient.SearchProductsAsync(normalizedMessage, 10);
            }

            var items = result?.Items?
    .Where(x => x != null)
    .ToList() ?? new List<ProductSummaryDto>();

            items = explicitListSearch
                ? ApplyExplicitSearchExclusions(items, excludedBrands, excludedCategories)
                : ProductExclusionHelper.ApplyExclusions(items, intent, profile);

            items = ApplyStrictExclusions(items, excludedBrands, excludedCategories);

            var globalStatisticResponse = await TryHandleGlobalStatisticQueryAsync(
    conversationId,
    normalizedMessage,
    intent,
    items,
    excludedBrands,
    excludedCategories);

            if (globalStatisticResponse != null)
                return globalStatisticResponse;

            if (items.Count == 0)
            {
                var nearMatches = new List<ProductSummaryDto>();

                if (!hasIntentBrandExclusions)
                {
                    nearMatches = await FindNearMatchProductsAsync(
                        effectiveBrand,
                        effectiveCategory,
                        effectiveMinPrice,
                        effectiveMaxPrice,
                        normalizedMessage);
                    nearMatches = explicitListSearch
     ? ApplyExplicitSearchExclusions(nearMatches, excludedBrands, excludedCategories)
     : ProductExclusionHelper.ApplyExclusions(nearMatches, intent, profile);

                    nearMatches = ApplyStrictExclusions(nearMatches, excludedBrands, excludedCategories);
                }

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                    Reply = _replyStyleService.BuildSearchEmptyReply(
    intent,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    nearMatches),
                    Products = nearMatches.Take(4).Select(MapToCard).ToList()
                };
            }

            await SaveSearchContextAsync(conversationId, items, intent);

            items = ApplyStrictExclusions(items, excludedBrands, excludedCategories);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ResolveToolName(effectiveBrand, effectiveCategory, effectiveMinPrice, effectiveMaxPrice),
                Reply = _replyStyleService.BuildSearchReply(
    items,
    intent,
    effectiveBrand,
    effectiveCategory,
    effectiveMinPrice,
    effectiveMaxPrice,
    item => BuildShortReason(item, intent, effectiveBrand, effectiveCategory, _productRecommendationService)),
                Products = items.Take(5).Select(MapToCard).ToList()
            };
        }

        private async Task SaveSearchContextAsync(
            string conversationId,
            List<ProductSummaryDto> items,
            ParsedIntent intent)
        {
            var latestProfile = await _conversationPreferenceService.GetAsync(conversationId);

            latestProfile.LastSearchProductNames = items
                .Select(x => x.Ten)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            latestProfile.LastSearchProductIds = items
                .Select(x => x.Id)
                .Distinct()
                .ToList();

            latestProfile.ActiveFlow = ChatFlowType.ProductSearch;
            latestProfile.UpdatedAtUtc = DateTime.UtcNow;
            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
    conversationId,
    items.Take(5).ToList());

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                items.Take(5).ToList(),
                "search");
        }
       
        private async Task<List<ProductSummaryDto>> FindNearMatchProductsAsync(
     string? brand,
     string? category,
     decimal? minPrice,
     decimal? maxPrice,
     string normalizedMessage)
        {
            decimal? relaxedMin = minPrice;
            decimal? relaxedMax = maxPrice;

            if (maxPrice.HasValue)
                relaxedMax = maxPrice.Value + 5_000_000m;

            if (minPrice.HasValue)
                relaxedMin = Math.Max(0, minPrice.Value - 5_000_000m);

            ProductSearchResponseDto? result;

            // Bước 1: giữ nguyên brand + category, chỉ nới giá
            if (!string.IsNullOrWhiteSpace(brand) ||
                !string.IsNullOrWhiteSpace(category) ||
                relaxedMin.HasValue ||
                relaxedMax.HasValue)
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand,
                    relaxedMin,
                    relaxedMax,
                    category,
                    5);

                var items = result?.Items?
                    .Where(x => x != null)
                    .Take(5)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (items.Count > 0)
                    return items;
            }

            // Bước 2: nếu có category mà quá chặt thì bỏ category, giữ brand + giá
            if (!string.IsNullOrWhiteSpace(category))
            {
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand,
                    relaxedMin,
                    relaxedMax,
                    null,
                    5);

                var items = result?.Items?
                    .Where(x => x != null)
                    .Take(5)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (items.Count > 0)
                    return items;
            }

            // Bước 3: nếu vẫn không có mà có brand thì giữ brand, nới giá thêm
            if (!string.IsNullOrWhiteSpace(brand))
            {
                decimal? moreRelaxedMin = relaxedMin.HasValue
    ? Math.Max(0, relaxedMin.Value - 3_000_000m)
    : (decimal?)null;

                decimal? moreRelaxedMax = relaxedMax.HasValue
                    ? relaxedMax.Value + 3_000_000m
                    : (decimal?)null;
                result = await _toolClient.GetProductsByFiltersAsync(
                    brand,
                    moreRelaxedMin,
                    moreRelaxedMax,
                    null,
                    5);

                var items = result?.Items?
                    .Where(x => x != null)
                    .Take(5)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (items.Count > 0)
                    return items;
            }

            // Bước 4: fallback cuối cùng
            result = await _toolClient.SearchProductsAsync(normalizedMessage, 5);

            return result?.Items?
                .Where(x => x != null)
                .Take(5)
                .ToList() ?? new List<ProductSummaryDto>();
        }
        private static string ResolveToolName(
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice)
        {
            if (!string.IsNullOrWhiteSpace(brand) && maxPrice.HasValue && !minPrice.HasValue)
            {
                return ToolNames.GetProductsByBrandAndPrice;
            }

            if (!string.IsNullOrWhiteSpace(brand) ||
                !string.IsNullOrWhiteSpace(category) ||
                minPrice.HasValue ||
                maxPrice.HasValue)
            {
                return ToolNames.GetProductsByFilters;
            }

            return ToolNames.SearchProducts;
        }

        private static string BuildShortReason(
    ProductSummaryDto item,
    ParsedIntent intent,
    string? brand,
    string? category,
    IProductRecommendationService productRecommendationService)
        {
            var name = item.Ten ?? string.Empty;
            var normalizedName = NormalizeText(name);
            var normalizedCategory = NormalizeCategory(!string.IsNullOrWhiteSpace(item.Loai) ? item.Loai : category);
            var price = item.Gia;

            if (normalizedCategory == "con tay")
            {
                return PickReasonByName(name,
                    "hợp nếu bạn thích xe côn tay thể thao và cảm giác lái chủ động",
                    "kiểu dáng cá tính, phù hợp với nhu cầu đi xe mạnh hơn",
                    "đáng cân nhắc nếu bạn muốn một mẫu côn tay trong tầm giá này");
            }

            if (normalizedCategory == "xe ga")
            {
                return PickReasonByName(name,
                    "tiện đi phố, dễ dùng và phù hợp đi lại hằng ngày",
                    "thoải mái khi chạy trong đô thị, không cần thao tác nhiều",
                    "gọn gàng, dễ sử dụng và hợp nhu cầu di chuyển thường xuyên");
            }

            if (normalizedCategory == "xe so")
            {
                return PickReasonByName(name,
                    "dễ đi, bền và chi phí sử dụng hợp lý",
                    "thực dụng, dễ bảo dưỡng và hợp đi lại hằng ngày",
                    "phù hợp nếu bạn muốn xe tiết kiệm, dễ dùng lâu dài");
            }

            if (ContainsAny(normalizedName, "smash", "wave", "sirius", "jupiter", "elegant"))
                return "giá dễ tiếp cận, phù hợp đi học hoặc di chuyển hằng ngày";

            if (ContainsAny(normalizedName, "address", "vision", "freego", "janus", "latte", "impulse"))
                return "gọn nhẹ, dễ đi và phù hợp sử dụng trong thành phố";

            if (ContainsAny(normalizedName, "air blade", "pcx", "sh", "lead"))
                return "thoải mái hơn khi đi phố và phù hợp nếu bạn cần tiện ích hằng ngày";

            if (ContainsAny(normalizedName, "gd110", "axelo", "future"))
                return "thực dụng, dễ bảo dưỡng và hợp dùng lâu dài";

            if (ContainsAny(normalizedName, "winner", "exciter", "raider"))
                return "thiết kế thể thao, phù hợp nếu bạn thích cảm giác lái mạnh mẽ";

            if (price > 0 && price < 25_000_000)
                return "giá rẻ, phù hợp nếu bạn muốn tiết kiệm chi phí";

            if (price > 0 && price < 40_000_000)
                return "mức giá hợp lý, cân bằng giữa chi phí và nhu cầu sử dụng";

            if (!string.IsNullOrWhiteSpace(brand))
                return $"một lựa chọn đáng tham khảo trong nhóm {brand}";

            return "một lựa chọn đáng tham khảo trong nhóm này";
        }
        private static string NormalizeCategory(string? category)
        {
            var text = NormalizeText(category);

            if (text.Contains("ga"))
                return "xe ga";

            if (text.Contains("so"))
                return "xe so";

            if (text.Contains("con"))
                return "con tay";

            return text;
        }
        private static bool ContainsAny(string text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return keywords.Any(k =>
                !string.IsNullOrWhiteSpace(k) &&
                text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim().ToLowerInvariant();

            text = text
                .Replace('à', 'a').Replace('á', 'a').Replace('ạ', 'a').Replace('ả', 'a').Replace('ã', 'a')
                .Replace('â', 'a').Replace('ầ', 'a').Replace('ấ', 'a').Replace('ậ', 'a').Replace('ẩ', 'a').Replace('ẫ', 'a')
                .Replace('ă', 'a').Replace('ằ', 'a').Replace('ắ', 'a').Replace('ặ', 'a').Replace('ẳ', 'a').Replace('ẵ', 'a')
                .Replace('è', 'e').Replace('é', 'e').Replace('ẹ', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e')
                .Replace('ê', 'e').Replace('ề', 'e').Replace('ế', 'e').Replace('ệ', 'e').Replace('ể', 'e').Replace('ễ', 'e')
                .Replace('ì', 'i').Replace('í', 'i').Replace('ị', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i')
                .Replace('ò', 'o').Replace('ó', 'o').Replace('ọ', 'o').Replace('ỏ', 'o').Replace('õ', 'o')
                .Replace('ô', 'o').Replace('ồ', 'o').Replace('ố', 'o').Replace('ộ', 'o').Replace('ổ', 'o').Replace('ỗ', 'o')
                .Replace('ơ', 'o').Replace('ờ', 'o').Replace('ớ', 'o').Replace('ợ', 'o').Replace('ở', 'o').Replace('ỡ', 'o')
                .Replace('ù', 'u').Replace('ú', 'u').Replace('ụ', 'u').Replace('ủ', 'u').Replace('ũ', 'u')
                .Replace('ư', 'u').Replace('ừ', 'u').Replace('ứ', 'u').Replace('ự', 'u').Replace('ử', 'u').Replace('ữ', 'u')
                .Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỵ', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y')
                .Replace('đ', 'd');

            return text;
        }

        private static string PickReasonByName(string? productName, params string[] reasons)
        {
            if (reasons == null || reasons.Length == 0)
                return "là mẫu khá sát với bộ lọc hiện tại";

            var name = productName ?? string.Empty;
            var index = Math.Abs(name.GetHashCode()) % reasons.Length;

            return reasons[index];
        }
        private static bool LooksLikeGeneralProductListRequest(string text)
        {
            text = NormalizeText(text);

            return ContainsAny(text,
                "shop co nhung xe nao",
                "shop co xe nao",
                "cua hang co nhung xe nao",
                "cua hang co xe nao",
                "co nhung xe nao",
                "co xe nao",
                "co xe gi",
                "liet ke xe",
                "danh sach xe",
                "tat ca xe",
                "toan bo xe",
                "xem cac xe",
                "xem danh sach xe");
        }
        private static bool MessageHasExplicitCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("xe so") ||
                   text.Contains("xe ga") ||
                   text.Contains("con tay") ||
                   text.Contains("tay ga");
        }

        private static bool MessageHasExplicitBrand(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("honda") ||
                   text.Contains("yamaha") ||
                   text.Contains("suzuki") ||
                   text.Contains("sym") ||
                   text.Contains("piaggio");
        }
        private async Task<ChatResponse?> TryHandleGlobalStatisticQueryAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    List<ProductSummaryDto> items,
    HashSet<string> excludedBrands,
    HashSet<string> excludedCategories)
        {
            var text = NormalizeText(normalizedMessage);

            bool isGlobalStatistic =
                text.Contains("re nhat") ||
                text.Contains("dat nhat") ||
                text.Contains("gia cao nhat") ||
                text.Contains("gia thap nhat") ||
                text.Contains("bao nhieu xe") ||
                text.Contains("co bao nhieu xe") ||
                text.Contains("tong so xe") ||
                text.Contains("ban chay nhat") ||
                text.Contains("nhieu nguoi mua") ||
                text.Contains("duoc mua nhieu") ||
                text.Contains("phu hop sinh vien nhat") ||
                text.Contains("hop sinh vien nhat") ||
                text.Contains("phu hop voi sinh vien") ||
text.Contains("hop voi sinh vien") ||
text.Contains("sinh vien nen mua") ||
text.Contains("xe sinh vien")||
                text.Contains("cho sinh vien nhat");

            if (!isGlobalStatistic)
                return null;

            var explicitBrand = DetectBrandFromText(text);
            var explicitCategory = DetectCategoryFromText(text);

            bool asksWholeShop =
                text.Contains("cua shop") ||
                text.Contains("shop") ||
                text.Contains("toan shop") ||
                text.Contains("tat ca") ||
                text.Contains("toan bo");
            var latestProfile = await _conversationPreferenceService.GetAsync(conversationId);

            var contextBrand = latestProfile?.PreferredBrand;
            var contextCategory = latestProfile?.PreferredCategory;

            var brand = asksWholeShop ? null : (explicitBrand ?? contextBrand);
            var category = asksWholeShop ? null : (explicitCategory ?? contextCategory);

            var allResult = await _toolClient.GetProductsByFiltersAsync(
                brand: brand,
                minPrice: null,
                maxPrice: null,
                category: category,
                take: 200);

            items = allResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            items = items
                .Where(x => !excludedBrands.Contains(x.ThuongHieu?.Trim() ?? string.Empty))
                .Where(x => !excludedCategories.Contains(x.Loai?.Trim() ?? string.Empty))
                .ToList();
            var products = items
    .Where(x => x != null)
    .GroupBy(x => x.Id)
    .Select(g => g.First())
    .ToList();

            if (products.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = "Mình chưa tìm thấy mẫu xe phù hợp với câu hỏi này trong dữ liệu hiện tại."
                };
            }
            var scopeText = BuildScopeText(brand, category);

            if (text.Contains("bao nhieu xe") ||
                text.Contains("co bao nhieu xe") ||
                text.Contains("tong so xe"))
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = $"Hiện tại {scopeText} có khoảng **{products.Count} mẫu xe** trong dữ liệu của shop.",
                    Products = products
                        .OrderBy(x => x.Gia)
                        .Take(5)
                        .Select(MapToCard)
                        .ToList()
                };
            }

            if (text.Contains("re nhat") || text.Contains("gia thap nhat"))
            {
                var cheapest = products
                    .OrderBy(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .First();

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = $"Mẫu xe rẻ nhất {scopeText} hiện là **{cheapest.Ten}**, giá khoảng **{cheapest.Gia:N0} VNĐ**.",
                    Products = new List<ChatProductCard> { MapToCard(cheapest) }
                };
            }

            if (text.Contains("dat nhat") || text.Contains("gia cao nhat"))
            {
                var mostExpensive = products
                    .OrderByDescending(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .First();

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = $"Mẫu xe đắt nhất {scopeText} hiện là **{mostExpensive.Ten}**, giá khoảng **{mostExpensive.Gia:N0} VNĐ**.",
                    Products = new List<ChatProductCard> { MapToCard(mostExpensive) }
                };
            }

            if (text.Contains("ban chay nhat") ||
                text.Contains("nhieu nguoi mua") ||
                text.Contains("duoc mua nhieu"))
            {
                var bestSellers = products
                    .OrderByDescending(x => x.SoLuong)
                    .ThenBy(x => x.Gia)
                    .Take(3)
                    .ToList();

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply =
                        $"Nếu tạm dựa trên số lượng/tồn kho trong dữ liệu, các mẫu nổi bật {scopeText} là:\n" +
                        string.Join("\n", bestSellers.Select(x => $"- **{x.Ten}** ({x.Gia:N0} VNĐ)")),
                    Products = bestSellers.Select(MapToCard).ToList()
                };
            }

            if (text.Contains("phu hop sinh vien nhat") ||
    text.Contains("hop sinh vien nhat") ||
    text.Contains("cho sinh vien nhat") ||
    text.Contains("phu hop voi sinh vien") ||
    text.Contains("hop voi sinh vien") ||
    text.Contains("sinh vien nen mua") ||
    text.Contains("xe sinh vien"))
            {
                var studentItems = products
                    .OrderBy(x => GetStudentFitRank(x))
                    .ThenBy(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .Take(3)
                    .ToList();

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply =
                        $"Với sinh viên, mình ưu tiên xe giá dễ tiếp cận, dễ đi và chi phí sử dụng thấp. Các mẫu hợp nhất {scopeText} là:\n" +
                        string.Join("\n", studentItems.Select(x => $"- **{x.Ten}** ({x.Gia:N0} VNĐ)")),
                    Products = studentItems.Select(MapToCard).ToList()
                };
            }

            return null;
        }
        private static string? DetectBrandFromText(string text)
        {
            if (text.Contains("honda")) return "Honda";
            if (text.Contains("yamaha")) return "Yamaha";
            if (text.Contains("suzuki")) return "Suzuki";
            if (text.Contains("sym")) return "SYM";
            if (text.Contains("piaggio")) return "Piaggio";

            return null;
        }

        private static string? DetectCategoryFromText(string text)
        {
            if (text.Contains("xe ga") || text.Contains("tay ga"))
                return "xe ga";

            if (text.Contains("xe so"))
                return "xe số";

            if (text.Contains("con tay"))
                return "côn tay";

            return null;
        }

        private static string BuildScopeText(string? brand, string? category)
        {
            if (!string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(category))
                return $"trong nhóm **{brand} {category}**";

            if (!string.IsNullOrWhiteSpace(brand))
                return $"của hãng **{brand}**";

            if (!string.IsNullOrWhiteSpace(category))
                return $"trong nhóm **{category}**";

            return "của shop";
        }

        private static int GetStudentFitRank(ProductSummaryDto product)
        {
            var name = NormalizeText(product.Ten);
            var category = NormalizeCategory(product.Loai);

            if (product.Gia <= 25_000_000 &&
                (category == "xe so" ||
                 ContainsAny(name, "wave", "sirius", "smash", "elegant", "galaxy", "passing")))
            {
                return 0;
            }

            if (product.Gia <= 35_000_000 &&
                ContainsAny(name, "vision", "address", "jupiter", "future", "attila"))
            {
                return 1;
            }

            if (product.Gia <= 40_000_000)
                return 2;

            return 3;
        }
        private static ChatProductCard MapToCard(ProductSummaryDto product)
        {
            return new ChatProductCard
            {
                Id = product.Id,
                Ten = product.Ten,
                Slug = product.Slug,
                Gia = product.Gia,
                SoLuong = product.SoLuong,
                CC = product.CC?.ToString(),
                ImageUrl = product.ImageUrl,
                ThuongHieu = product.ThuongHieu,
                Loai = product.Loai
            };
        }
        private static bool IsExplicitListSearch(string message)
        {
            var text = NormalizeText(message);

            bool hasListVerb =
                text.Contains("dua ra") ||
                text.Contains("liet ke") ||
                text.Contains("ke ra") ||
                text.Contains("danh sach") ||
                text.Contains("shop co") ||
                text.Contains("cua hang co") ||
                text.Contains("co nhung xe nao") ||
                text.Contains("co xe nao") ||
                text.Contains("nhung xe nao") ||
                text.Contains("tat ca xe") ||
                text.Contains("toan bo xe");

            bool hasVehicleSignal =
                text.Contains("xe") ||
                text.Contains("mau") ||
                text.Contains("san pham");

            return hasListVerb && hasVehicleSignal;
        }
        private static bool MessageHasExplicitNegativeBrand(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("khong honda") ||
                   text.Contains("khong yamaha") ||
                   text.Contains("khong suzuki") ||
                   text.Contains("khong sym") ||
                   text.Contains("khong piaggio") ||
                   text.Contains("khong thich honda") ||
                   text.Contains("khong thich yamaha") ||
                   text.Contains("khong thich suzuki") ||
                   text.Contains("khong thich sym") ||
                   text.Contains("khong thich piaggio") ||
                   text.Contains("tru honda") ||
                   text.Contains("tru yamaha") ||
                   text.Contains("tru suzuki") ||
                   text.Contains("tru sym") ||
                   text.Contains("tru piaggio");
        }

        private static bool MessageHasExplicitNegativeCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("khong xe ga") ||
                   text.Contains("khong thich xe ga") ||
                   text.Contains("khong xe so") ||
                   text.Contains("khong thich xe so") ||
                   text.Contains("khong con tay") ||
                   text.Contains("khong thich con tay");
        }
        private static List<ProductSummaryDto> ApplyExplicitSearchExclusions(
    IEnumerable<ProductSummaryDto> products,
    HashSet<string> excludedBrands,
    HashSet<string> excludedCategories)
        {
            var items = products
                .Where(x => x != null)
                .ToList();

            if (excludedBrands.Count > 0)
            {
                items = items
                    .Where(x => !excludedBrands.Contains(x.ThuongHieu?.Trim() ?? string.Empty))
                    .ToList();
            }

            if (excludedCategories.Count > 0)
            {
                items = items
                    .Where(x => !excludedCategories.Contains(x.Loai?.Trim() ?? string.Empty))
                    .ToList();
            }

            return items;
        }
    }
}
