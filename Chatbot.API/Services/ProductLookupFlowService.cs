using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ProductLookupFlowService : IProductLookupFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<ProductLookupFlowService> _logger;

        private static readonly Dictionary<string, string> ProductAliasMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["ab"] = "Honda Air Blade",
                ["air blade"] = "Honda Air Blade",
                ["vision"] = "Honda Vision",
                ["future"] = "Honda Future",
                ["winner"] = "Honda Winner X",
                ["winner x"] = "Honda Winner X",
                ["sh"] = "Honda SH 150i",
                ["sh 150"] = "Honda SH 150i",
                ["sh150"] = "Honda SH 150i",
                ["sh 150i"] = "Honda SH 150i",
                ["sh150i"] = "Honda SH 150i",
                ["honda sh"] = "Honda SH 150i",
                ["honda sh 150i"] = "Honda SH 150i",
                ["wave"] = "Honda Wave",
                ["janus"] = "Yamaha Janus",
                ["latte"] = "Yamaha Latte",
                ["grande"] = "Yamaha Grande",
                ["exciter"] = "Yamaha Exciter",
                ["jupiter"] = "Yamaha Jupiter",
                ["freego"] = "Yamaha Freego",
                ["zip"] = "Piaggio Zip 100",
                ["zip 100"] = "Piaggio Zip 100",
                ["medley"] = "Piaggio Medley",
                ["liberty"] = "Piaggio Liberty",
                ["impulse"] = "Suzuki Impulse 125",
                ["impulse 125"] = "Suzuki Impulse 125",
                ["address"] = "Suzuki Address 110",
                ["address 110"] = "Suzuki Address 110",
                ["burgman"] = "Suzuki Burgman 125",
                ["gd110"] = "Suzuki GD110",
                ["gd 110"] = "Suzuki GD110",
                ["husky"] = "SYM Husky",
                ["attila"] = "SYM Attila Venus",
                ["attila venus"] = "SYM Attila Venus",
                ["galaxy"] = "SYM Galaxy Sport",
                ["galaxy sport"] = "SYM Galaxy Sport",
                ["visison"] = "Honda Vision",
                ["visson"] = "Honda Vision",
                ["visoin"] = "Honda Vision",

                ["airbalde"] = "Honda Air Blade",
                ["air balde"] = "Honda Air Blade",
                ["air blad"] = "Honda Air Blade",
                ["air blae"] = "Honda Air Blade",
                ["airb lade"] = "Honda Air Blade"
            };

        public ProductLookupFlowService(
            IWebBanXeMayToolClient toolClient,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<ProductLookupFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }

        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var effectiveLookupField = !string.IsNullOrWhiteSpace(intent.LookupField)
    ? intent.LookupField
    : InferLookupField(normalizedMessage);

            bool hasLookupField = !string.IsNullOrWhiteSpace(effectiveLookupField);
            bool hasLookupContext =
      !string.IsNullOrWhiteSpace(profile.LastLookupProductName) ||
      !string.IsNullOrWhiteSpace(profile.LastResolvedProductName) ||
      profile.LastMentionedProducts?.Any() == true ||
      profile.LastRecommendedProducts?.Any() == true;

            bool isReferenceLookupFollowUp = IsLookupReferenceLikeMessage(normalizedMessage);
            bool hasExplicitProductSignal = HasExplicitProductSignal(normalizedMessage, intent);

            bool isPriceLookup = string.Equals(effectiveLookupField, "price", StringComparison.OrdinalIgnoreCase);
            if (hasLookupField &&
    isPriceLookup &&
    !string.IsNullOrWhiteSpace(intent.Brand) &&
    (intent.MentionedProducts == null || intent.MentionedProducts.Count == 0) &&
    string.IsNullOrWhiteSpace(DetectProductNameFromText(normalizedMessage)))
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = $"Bạn muốn mình tra giá mẫu xe nào của hãng {intent.Brand}? Ví dụ: Yamaha Freego, Yamaha Latte, Yamaha Grande hoặc Yamaha Jupiter."
                };
            }
            bool isStockLookup = string.Equals(effectiveLookupField, "stock", StringComparison.OrdinalIgnoreCase);

            // 1. Nếu user hỏi giá mà không nói rõ mẫu xe -> hỏi lại, không đoán theo context
            if (hasLookupField &&
                isPriceLookup &&
                !hasExplicitProductSignal &&
                !isReferenceLookupFollowUp)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Bạn muốn mình tra giá của mẫu xe nào vậy? Bạn nhập rõ tên xe giúp mình nhé, ví dụ: Honda Vision, Air Blade hoặc SH 150i."
                };
            }

            // 2. Nếu hỏi tồn kho mà không nói rõ xe:
            // - có context lookup gần nhất thì cho phép follow-up
            // - không có context thì hỏi lại
            if (hasLookupField &&
                isStockLookup &&
                !hasExplicitProductSignal &&
                !isReferenceLookupFollowUp &&
                !hasLookupContext)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Bạn muốn mình kiểm tra tồn kho của mẫu xe nào vậy? Bạn nhập rõ tên xe giúp mình nhé, ví dụ: Honda Vision, Air Blade hoặc SH 150i."
                };
            }

            // 3. Các lookup khác mà không rõ xe thì hỏi lại
            if (hasLookupField &&
                !isPriceLookup &&
                !isStockLookup &&
                !hasExplicitProductSignal &&
                !isReferenceLookupFollowUp)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Bạn muốn mình tra thông tin của mẫu xe nào vậy? Bạn nhập rõ tên xe giúp mình nhé, ví dụ: Honda Vision, Air Blade hoặc SH 150i."
                };
            }

            bool isLookupFollowUp =
                hasLookupContext &&
                (
                    isReferenceLookupFollowUp ||
                    (
                        string.Equals(profile.ActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) &&
                        isStockLookup &&
                        !hasExplicitProductSignal
                    )
                );

            bool routeAlreadyLookup =
                string.Equals(profile.ActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intent.RouteFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intent.IntentType, "product_lookup", StringComparison.OrdinalIgnoreCase);

            bool hasOnlyProductName =
     !string.IsNullOrWhiteSpace(DetectProductNameFromText(normalizedMessage)) &&
     string.IsNullOrWhiteSpace(intent.LookupField);

            if (!routeAlreadyLookup &&
                !intent.IsDirectProductLookup &&
                !isLookupFollowUp &&
                !hasOnlyProductName)
            {
                return null;
            }

            var hasSearchableCandidate =
     intent.MentionedProducts != null && intent.MentionedProducts.Count > 0;

            if (!hasSearchableCandidate && !hasLookupContext && !intent.IsDirectProductLookup)
            {
                return null;
            }

            ProductSummaryDto? resolvedProduct = null;

            var candidateNames = BuildCandidateNames(intent, profile, normalizedMessage);

            if (isReferenceLookupFollowUp && candidateNames.Count > 0)
            {
                foreach (var candidate in candidateNames)
                {
                    resolvedProduct = await SearchBestProductByNameAsync(candidate);
                    if (resolvedProduct != null)
                        break;
                }
            }
            else if (isLookupFollowUp && hasLookupContext)
            {
                resolvedProduct = await ResolveFromLookupContextAsync(profile, normalizedMessage);
            }
            _logger.LogWarning(
    "LOOKUP CANDIDATES => Message={Message}, Candidates={Candidates}, IntentMentioned={Mentioned}, LastLookup={LastLookup}, LastResolved={LastResolved}",
    normalizedMessage,
    string.Join(" | ", candidateNames),
    string.Join(" | ", intent.MentionedProducts ?? new List<string>()),
    profile.LastLookupProductName,
    profile.LastResolvedProductName);
            resolvedProduct ??= await ResolveBestProductAsync(intent, profile, normalizedMessage);

            if (resolvedProduct == null)
            {
                var fallbackReply = await BuildLookupNotFoundReplyAsync(
                    intent,
                    profile,
                    normalizedMessage);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.SearchProducts,
                    Reply = fallbackReply
                };
            }
            var lookupContextNames =
                candidateNames != null && candidateNames.Count > 0
                    ? candidateNames
                    : new List<string> { resolvedProduct.Ten };

            await SaveLookupContextAsync(conversationId, resolvedProduct, lookupContextNames);
            _logger.LogWarning(
    "LOOKUP CONTEXT SAVED => {ProductName}",
    resolvedProduct.Ten);
            var lookupIntent = intent.Clone();
            lookupIntent.LookupField = effectiveLookupField;

            var reply = await BuildLookupReplyAsync(resolvedProduct, lookupIntent, normalizedMessage);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ToolNames.SearchProducts,
                Reply = reply,
                Products = new List<ChatProductCard>
                {
                    MapToCard(resolvedProduct)
                }
            };
        }

        private async Task<ProductSummaryDto?> ResolveBestProductAsync(
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage)
        {
            var candidateNames = BuildCandidateNames(intent, profile, normalizedMessage);

            foreach (var candidate in candidateNames)
            {
                var bestMatch = await SearchBestProductByNameAsync(candidate);
                if (bestMatch != null)
                    return bestMatch;
            }

            return null;
        }
        private Task<string> BuildLookupNotFoundReplyAsync(
      ParsedIntent intent,
      CustomerPreferenceProfile profile,
      string normalizedMessage)
        {
            var reply =
                "Mình chưa tìm thấy đúng mẫu xe bạn đang hỏi trong dữ liệu hiện tại. " +
                "Bạn nhập lại tên xe rõ hơn giúp mình nhé, ví dụ: Honda Vision, Air Blade, Yamaha Freego.";

            return Task.FromResult(reply);
        }

        private async Task<List<ProductSummaryDto>> FindFallbackProductsAsync(
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage)
        {
            var brand = intent.Brand ?? profile.PreferredBrand;
            var category = intent.Category ?? profile.PreferredCategory;

            // Ưu tiên: nếu user có nói hãng hoặc category thì tìm theo bộ lọc trước
            if (!string.IsNullOrWhiteSpace(brand) || !string.IsNullOrWhiteSpace(category))
            {
                var filtered = await _toolClient.GetProductsByFiltersAsync(
                    brand: string.IsNullOrWhiteSpace(brand) ? null : brand,
                    minPrice: intent.PriceMin ?? profile.PriceMin,
                    maxPrice: intent.PriceMax ?? profile.PriceMax,
                    category: string.IsNullOrWhiteSpace(category) ? null : category,
                    take: 6);

                var filteredItems = filtered?.Items?
                    .Where(x => x != null)
                    .OrderByDescending(x => x.SoLuong)
                    .ToList() ?? new List<ProductSummaryDto>();

                if (filteredItems.Count > 0)
                    return filteredItems;
            }

            // Fallback: lấy từ các candidate còn dùng được
            var candidateNames = BuildCandidateNames(intent, profile, normalizedMessage);

            foreach (var candidate in candidateNames)
            {
                var core = RemoveKnownBrandPrefix(candidate);
                var query = !string.IsNullOrWhiteSpace(core) ? core : candidate;

                var searchResult = await _toolClient.SearchProductsAsync(query, 6);
                var items = searchResult?.Items?
                    .Where(x => x != null)
                    .OrderByDescending(x => x.SoLuong)
                    .ToList();

                if (items != null && items.Count > 0)
                    return items;
            }

            return new List<ProductSummaryDto>();
        }
        private static List<string> BuildCandidateNames(
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage)
        {
            var productFromText = DetectProductNameFromText(normalizedMessage);
            if (!string.IsNullOrWhiteSpace(productFromText))
            {
                return new List<string> { productFromText };
            }

            var candidates = new List<string>();
            bool isReferenceFollowUp = IsLookupReferenceLikeMessage(normalizedMessage);

            if (isReferenceFollowUp)
            {
                // Ưu tiên xe vừa lookup / vừa resolve trước
                if (!string.IsNullOrWhiteSpace(profile.LastResolvedProductName))
                    candidates.Add(profile.LastResolvedProductName);

                if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                    candidates.Add(profile.LastLookupProductName);

                // Sau đó mới tới danh sách candidate cũ
                if (profile.LastLookupCandidateNames != null && profile.LastLookupCandidateNames.Count > 0)
                    candidates.AddRange(profile.LastLookupCandidateNames);

                if (profile.LastMentionedProducts != null && profile.LastMentionedProducts.Count > 0)
                    candidates.AddRange(profile.LastMentionedProducts);

                if (profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count > 0)
                    candidates.AddRange(profile.LastRecommendedProducts);
            }

            if (candidates.Count == 0 &&
     intent.MentionedProducts != null &&
     intent.MentionedProducts.Count > 0)
            {
                candidates.AddRange(intent.MentionedProducts);
            }
            if (candidates.Count == 0)
            {
                var unknownCandidate = ExtractUnknownProductCandidate(normalizedMessage);
                if (!string.IsNullOrWhiteSpace(unknownCandidate))
                    candidates.Add(unknownCandidate);
            }
            return candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        private static string? DetectProductNameFromText(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var text = NormalizeText(message);

            foreach (var alias in ProductAliasMap.OrderByDescending(x => x.Key.Length))
            {
                if (ContainsWholePhrase(text, NormalizeText(alias.Key)))
                    return alias.Value;
            }

            return null;
        }
        private static bool ContainsWholePhrase(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
                return false;

            return System.Text.RegularExpressions.Regex.IsMatch(
                text,
                $@"(?<!\p{{L}}|\p{{N}}){System.Text.RegularExpressions.Regex.Escape(phrase)}(?!\p{{L}}|\p{{N}})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        private static List<string> BuildSearchQueries(string candidate)
        {
            var queries = new List<string>();
            var normalized = NormalizeText(candidate);

            if (!string.IsNullOrWhiteSpace(candidate))
                queries.Add(candidate.Trim());

            if (ProductAliasMap.TryGetValue(normalized, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
                queries.Add(mapped);

            var coreName = RemoveKnownBrandPrefix(candidate);
            if (!string.IsNullOrWhiteSpace(coreName) &&
                !queries.Any(x => string.Equals(x, coreName, StringComparison.OrdinalIgnoreCase)))
            {
                queries.Add(coreName);
            }

            if (ProductAliasMap.TryGetValue(NormalizeText(coreName), out var mappedFromCore) &&
                !queries.Any(x => string.Equals(x, mappedFromCore, StringComparison.OrdinalIgnoreCase)))
            {
                queries.Add(mappedFromCore);
            }

            return queries
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        private static bool IsLookupReferenceLikeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);

            return text.Contains("mau nay") ||
                   text.Contains("con nay") ||
                   text.Contains("xe nay") ||
                   text.Contains("mau do") ||
                   text.Contains("con do") ||
                   text.Contains("xe do") ||
                   text.Contains("mau kia") ||
                   text.Contains("con kia") ||
                   text.Contains("xe kia") ||
                   text.Contains("dau tien") ||
                   text.Contains("thu 2") ||
                   text.Contains("thu hai");
        }
        private async Task<ProductSummaryDto?> ResolveFromLookupContextAsync(
    CustomerPreferenceProfile profile,
    string normalizedMessage)
        {
            var candidateNames = profile.LastLookupCandidateNames?
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList() ?? new List<string>();

            if (candidateNames.Count == 0 && !string.IsNullOrWhiteSpace(profile.LastResolvedProductName))
            {
                candidateNames.Add(profile.LastResolvedProductName);
            }

            if (candidateNames.Count == 0 && profile.LastMentionedProducts != null)
            {
                candidateNames = profile.LastMentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            int index = ExtractReferencedIndex(normalizedMessage);

            string selectedName;
            if (index >= 0 && index < candidateNames.Count)
            {
                selectedName = candidateNames[index];
            }
            else
            {
                selectedName = !string.IsNullOrWhiteSpace(profile.LastResolvedProductName)
     ? profile.LastResolvedProductName
     : !string.IsNullOrWhiteSpace(profile.LastLookupProductName)
         ? profile.LastLookupProductName
         : candidateNames.First();
            }

            return await SearchBestProductByNameAsync(selectedName);
        }
        private static int ExtractReferencedIndex(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return -1;

            var text = message.Trim().ToLowerInvariant();

            if (text.Contains("đầu tiên"))
                return 0;

            if (text.Contains("thứ 2") || text.Contains("thứ hai"))
                return 1;

            return -1;
        }
        private async Task<ProductSummaryDto?> SearchBestProductByNameAsync(string candidate)
        {
            var searchQueries = BuildSearchQueries(candidate);

            foreach (var query in searchQueries)
            {
                var searchResult = await _toolClient.SearchProductsAsync(query, 8);
                var items = searchResult?.Items;

                if (items == null || items.Count == 0)
                    continue;

                var bestMatch = items
                    .OrderByDescending(x => ScoreProductMatch(x, candidate, query))
                    .ThenByDescending(x => x.SoLuong)
                    .FirstOrDefault();

                var bestScore = bestMatch == null ? 0 : ScoreProductMatch(bestMatch, candidate, query);

                if (bestMatch != null && bestScore >= 80)
                {
                    _logger.LogInformation(
                        "Product lookup resolved product. Candidate={Candidate}, Query={Query}, Resolved={ResolvedName}, Id={Id}",
                        candidate,
                        query,
                        bestMatch.Ten,
                        bestMatch.Id);

                    return bestMatch;
                }
            }

            return null;
        }
        private static int ScoreProductMatch(ProductSummaryDto product, string candidate, string query)
        {
            var productName = NormalizeText(product.Ten);
            var candidateName = NormalizeText(candidate);
            var queryName = NormalizeText(query);
            var candidateCore = NormalizeText(RemoveKnownBrandPrefix(candidate));
            var productCore = NormalizeText(RemoveKnownBrandPrefix(product.Ten));

            int score = 0;

            if (string.Equals(productName, candidateName, StringComparison.OrdinalIgnoreCase)) score += 120;
            if (string.Equals(productName, queryName, StringComparison.OrdinalIgnoreCase)) score += 110;
            if (!string.IsNullOrWhiteSpace(candidateCore) && string.Equals(productCore, candidateCore, StringComparison.OrdinalIgnoreCase)) score += 95;
            if (productName.Contains(candidateName, StringComparison.OrdinalIgnoreCase)) score += 70;
            if (productName.Contains(queryName, StringComparison.OrdinalIgnoreCase)) score += 65;
            if (!string.IsNullOrWhiteSpace(candidateCore) && productCore.Contains(candidateCore, StringComparison.OrdinalIgnoreCase)) score += 60;

            var candidateTokens = Tokenize(candidateName);
            var queryTokens = Tokenize(queryName);
            var productTokens = Tokenize(productName);

            score += candidateTokens.Count(t => productTokens.Contains(t)) * 12;
            score += queryTokens.Count(t => productTokens.Contains(t)) * 8;

            var distance = LevenshteinDistance(productCore, string.IsNullOrWhiteSpace(candidateCore) ? candidateName : candidateCore);
            if (distance <= 2) score += 20;
            else if (distance <= 4) score += 10;

            return score;
        }

        private async Task SaveLookupContextAsync(
    string conversationId,
    ProductSummaryDto product,
    IEnumerable<string>? candidateNames = null)
        {
            await _conversationPreferenceService.SaveProductLookupContextAsync(
                conversationId,
                product.Id,
                product.Ten,
                candidateNames);
        }
        private async Task<string> BuildLookupReplyAsync(ProductSummaryDto product, ParsedIntent intent, string normalizedMessage)
        {
            var lookupField = intent.LookupField?.Trim().ToLowerInvariant();

            switch (lookupField)
            {
                case "price":
                    return $"**{product.Ten}** hiện có giá khoảng **{product.Gia:N0} VNĐ**.";

                case "stock":
                    if (product.SoLuong > 0)
                    {
                        return $"**{product.Ten}** hiện vẫn còn hàng. Số lượng trong hệ thống là **{product.SoLuong}** chiếc.";
                    }

                    return $"**{product.Ten}** hiện đang hết hàng trong dữ liệu hệ thống.";

                case "cc":
                    if (product.CC.HasValue)
                    {
                        return $"**{product.Ten}** có dung tích khoảng **{product.CC.Value} cc**.";
                    }

                    return $"Mình đã tìm thấy **{product.Ten}**, nhưng hiện dữ liệu chưa có thông tin chính xác về số cc.";

                case "installment":
                    return $"Mình nhận ra bạn đang hỏi về **trả góp** cho **{product.Ten}**. Hiện dữ liệu tra cứu chưa có bảng trả góp chi tiết, nhưng giá xe hiện tại là **{product.Gia:N0} VNĐ**. Nếu bạn muốn, mình có thể giúp bạn ước lượng mức trả trước và số tiền cần chuẩn bị ban đầu.";
                case "category":
                    return BuildCategoryAnswer(product, normalizedMessage);
                case "detail":
                    return BuildDetailReply(product);

                default:
                    var detail = await _toolClient.GetProductDetailAsync(product.Id);
                    return BuildDetailReply(detail ?? product);
            }
        }

        private static string? InferLookupField(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var text = message.Trim().ToLowerInvariant();

            if (text.Contains("trả góp") || text.Contains("tra gop") || text.Contains("góp") || text.Contains("gop"))
                return "installment";

            if (text.Contains("giá") || text.Contains("gia") || text.Contains("bao nhiêu") || text.Contains("bao nhieu"))
                return "price";

            if (text.Contains("còn hàng") || text.Contains("con hang") ||
                text.Contains("còn hàng không") || text.Contains("con hang khong") ||
                text.Contains("tồn kho") || text.Contains("ton kho") ||
                text.Contains("hết hàng") || text.Contains("het hang") ||
                text.Contains("còn không") || text.Contains("con khong") ||
                text.Contains("còn không vậy") || text.Contains("con khong vay") ||
                text.Contains("còn ko") || text.Contains("con ko") ||
                text.Contains("còn mấy chiếc") || text.Contains("con may chiec") ||
                text.Contains("bao nhiêu chiếc") || text.Contains("bao nhieu chiec"))
                return "stock";

            if (text.Contains("cc"))
                return "cc";
            if (text.Contains("loai xe") ||
    text.Contains("dong xe") ||
    text.Contains("xe gi") ||
    text.Contains("xe ga") ||
    text.Contains("xe so") ||
    text.Contains("con tay") ||
    text.Contains("co phai xe ga") ||
    text.Contains("co phai xe so") ||
    text.Contains("co phai xe con tay"))
            {
                return "category";
            }
            return "detail";
        }

        private static string BuildDetailReply(ProductSummaryDto product)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"**{product.Ten}** là mẫu xe thuộc hãng **{product.ThuongHieu}**, dòng **{product.Loai}**.");

            sb.AppendLine();
            sb.AppendLine("Thông tin chính:");
            sb.AppendLine($"- Giá: **{product.Gia:N0} VNĐ**");
            sb.AppendLine($"- Hãng: **{product.ThuongHieu}**");
            sb.AppendLine($"- Loại xe: **{product.Loai}**");

            if (product.CC.HasValue)
            {
                sb.AppendLine($"- Dung tích: **{product.CC.Value} cc**");
            }

            if (product.SoLuong > 0)
            {
                sb.AppendLine($"- Tồn kho: **{product.SoLuong} chiếc**");
            }
            else
            {
                sb.AppendLine("- Tình trạng: hiện đang hết hàng");
            }

            sb.AppendLine();
            sb.AppendLine(BuildProductAdvice(product));

            return sb.ToString().Trim();
        }
        private static string BuildProductAdvice(ProductSummaryDto product)
        {
            var name = NormalizeText(product.Ten);
            var type = NormalizeText(product.Loai);

            if (name.Contains("vision"))
                return "Điểm nổi bật: Honda Vision nhỏ gọn, dễ điều khiển, hợp đi phố, đi học hoặc đi làm hằng ngày. Mẫu này khá phù hợp với khách cần xe nhẹ, tiết kiệm và dễ sử dụng.";

            if (name.Contains("air blade"))
                return "Điểm nổi bật: Honda Air Blade có kiểu dáng thể thao, cảm giác chạy đầm hơn các mẫu xe nhỏ, phù hợp với người đi làm, thích xe ga khỏe và hiện đại hơn.";

            if (name.Contains("grande"))
                return "Điểm nổi bật: Yamaha Grande có thiết kế thanh lịch, nhẹ nhàng, hợp khách thích xe ga đẹp, dễ đi và có phong cách mềm mại.";

            if (name.Contains("latte"))
                return "Điểm nổi bật: Yamaha Latte nhỏ gọn, dễ điều khiển, phù hợp đi phố và đặc biệt hợp với khách ưu tiên xe nhẹ, dáng thanh lịch.";

            if (name.Contains("freego"))
                return "Điểm nổi bật: Yamaha Freego thực dụng, giá dễ tiếp cận, phù hợp nhu cầu đi lại hằng ngày và khách muốn xe ga trong tầm tiền vừa phải.";

            if (name.Contains("winner"))
                return "Điểm nổi bật: Honda Winner X hợp với khách thích xe côn tay, kiểu dáng thể thao, máy khỏe và cảm giác lái cá tính hơn xe tay ga.";

            if (type.Contains("tay ga") || type.Contains("xe ga"))
                return "Điểm nổi bật: mẫu xe này phù hợp đi phố, dễ sử dụng hằng ngày và tiện hơn nếu bạn ưu tiên sự thoải mái.";

            if (type.Contains("xe so"))
                return "Điểm nổi bật: mẫu xe này thiên về sự bền bỉ, tiết kiệm và chi phí sử dụng thấp.";

            if (type.Contains("con tay"))
                return "Điểm nổi bật: mẫu xe này hợp với khách thích cảm giác lái thể thao và chủ động hơn khi di chuyển.";

            return "Điểm nổi bật: mẫu xe này phù hợp để cân nhắc nếu bạn muốn xem nhanh giá, tình trạng còn hàng và so sánh thêm với các mẫu cùng tầm.";
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
        private static string BuildCategoryAnswer(ProductSummaryDto product, string message)
        {
            var text = NormalizeText(message);
            var actualType = NormalizeText(product.Loai);

            string? askedType = null;
            string? askedDisplay = null;

            if (text.Contains("con tay"))
            {
                askedType = "con tay";
                askedDisplay = "xe côn tay";
            }
            else if (text.Contains("xe so"))
            {
                askedType = "xe so";
                askedDisplay = "xe số";
            }
            else if (text.Contains("xe ga") || text.Contains("tay ga"))
            {
                askedType = "tay ga";
                askedDisplay = "xe ga";
            }

            if (string.IsNullOrWhiteSpace(askedType))
                return $"**{product.Ten}** thuộc dòng **{product.Loai}**.";

            bool isMatch =
                actualType.Contains(askedType) ||
                (askedType == "tay ga" && actualType.Contains("xe ga"));

            if (isMatch)
                return $"Đúng rồi, **{product.Ten}** thuộc dòng **{product.Loai}**.";

            return $"Không, **{product.Ten}** không phải {askedDisplay}. Mẫu này thuộc dòng **{product.Loai}**.";
        }
        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim().ToLowerInvariant()
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
        }

        private static string RemoveKnownBrandPrefix(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text.Trim();
            var lower = value.ToLowerInvariant();

            string[] prefixes = { "honda ", "yamaha ", "suzuki ", "piaggio ", "sym " };
            foreach (var prefix in prefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(prefix.Length).Trim();
            }

            return value;
        }

        private static HashSet<string> Tokenize(string text)
        {
            return text
                .Split(new[] { ' ', '-', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static int LevenshteinDistance(string a, string b)
        {
            a ??= string.Empty;
            b ??= string.Empty;

            var dp = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[a.Length, b.Length];
        }
        private static bool HasExplicitProductSignal(string normalizedMessage, ParsedIntent intent)
        {
            if (intent.MentionedProducts != null &&
                intent.MentionedProducts.Any(x => !string.IsNullOrWhiteSpace(x)))
            {
                return true;
            }

            var productFromText = DetectProductNameFromText(normalizedMessage);
            if (!string.IsNullOrWhiteSpace(productFromText))
                return true;

            var unknownCandidate = ExtractUnknownProductCandidate(normalizedMessage);
            return !string.IsNullOrWhiteSpace(unknownCandidate);
        }
        private static string? ExtractUnknownProductCandidate(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var text = NormalizeText(message);

            var patterns = new[]
            {
        @"^(?:xe\s+)?(?<name>[a-z0-9\s]{2,50})\s+(?:co|con)\s+khong$",
        @"^(?:co|con)\s+(?:xe\s+)?(?<name>[a-z0-9\s]{2,50})\s+khong$"
    };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                var name = match.Groups["name"].Value.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (IsGenericLookupWord(name))
                    continue;

                return name;
            }

            return null;
        }

        private static bool IsGenericLookupWord(string value)
        {
            value = NormalizeText(value);

            var genericWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "nao",
        "gi",
        "xe nao",
        "mau nao",
        "hang nao",
        "san pham nao",
        "shop",
        "cua hang"
    };

            return genericWords.Contains(value);
        }
    }
}
