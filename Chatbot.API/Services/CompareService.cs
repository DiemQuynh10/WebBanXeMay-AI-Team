using System.Text;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class CompareService : ICompareService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IRagService _ragService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<CompareService> _logger;

        public CompareService(
            IWebBanXeMayToolClient toolClient,
            IRagService ragService,
            IConversationPreferenceService conversationPreferenceService,
            ILogger<CompareService> logger)
        {
            _toolClient = toolClient;
            _ragService = ragService;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }
        public async Task<ChatResponse?> CompareAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var explicitProductTargets = ResolveExplicitComparisonTargets(intent);
            var explicitBrandTargets = ResolveExplicitBrandComparisonTargets(normalizedMessage, intent);
            var contextProductTargets = ResolveContextComparisonTargets(profile);
            var budgetHint = BuildBudgetHint(intent, normalizedMessage);

            ProductSummaryDto? first = null;
            ProductSummaryDto? second = null;

            bool isFollowUpCompare =
    profile != null &&
    profile.HasActiveCompareContext &&
    profile.LastComparedProducts != null &&
    profile.LastComparedProducts.Count >= 2 &&
    (intent.MentionedProducts == null || intent.MentionedProducts.Count < 2);

            if (explicitProductTargets.Count >= 2)
            {
                first = await FindBestMatchAsync(explicitProductTargets[0]);
                second = await FindBestMatchAsync(explicitProductTargets[1]);
            }
            else if (explicitBrandTargets.Count >= 2)
            {
                first = await FindBestBrandRepresentativeAsync(explicitBrandTargets[0], intent);
                second = await FindBestBrandRepresentativeAsync(explicitBrandTargets[1], intent);
            }
            else if (contextProductTargets.Count >= 2)
            {
                // Only reuse context pair when user does not explicitly provide product/brand targets.
                first = await FindBestMatchAsync(contextProductTargets[0]);
                second = await FindBestMatchAsync(contextProductTargets[1]);
            }
            else if (isFollowUpCompare && profile?.LastComparedProducts?.Count >= 2)
            {
                var fallbackCompared = profile.LastComparedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();

                if (fallbackCompared.Count >= 2)
                {
                    first = await FindBestMatchAsync(fallbackCompared[0]);
                    second = await FindBestMatchAsync(fallbackCompared[1]);
                }
            }

            if (first == null || second == null)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Mình chưa tìm đủ 2 mẫu xe phù hợp để so sánh từ dữ liệu hiện tại. Bạn có thể nói rõ như \"Vision với Latte\" hoặc \"so sánh Honda và Yamaha dưới 40 triệu\" nhé."
                };
            }
            var questionKind = DetectCompareQuestionKind(normalizedMessage, intent);

            string? ragContext = null;
            try
            {
                var ragQuery = BuildRagCompareQuery(first, second, intent, profile, normalizedMessage);
                var ragResult = await _ragService.QueryAsync(ragQuery, topK: 8);
                if (ragResult?.Success == true && !string.IsNullOrWhiteSpace(ragResult.Context))
                {
                    ragContext = ragResult.Context;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG compare failed. ConversationId: {ConversationId}", conversationId);
            }
            var resolvedFeature = ResolveComparisonFeature(intent, normalizedMessage)
    ?? InferFeatureFromMessageOnly(normalizedMessage);

            _logger.LogInformation(
                "Compare feature resolved. ConversationId: {ConversationId}, Message: {Message}, RawComparisonFeature: {RawComparisonFeature}, ResolvedFeature: {ResolvedFeature}",
                conversationId,
                normalizedMessage,
                intent?.ComparisonFeature,
                resolvedFeature);
            string reply = questionKind == CompareQuestionKind.Price
             ? BuildPriceCompareReply(first, second, budgetHint)
             : BuildDeterministicCompareReply(first, second, intent, profile, normalizedMessage, ragContext, isFollowUpCompare, budgetHint);

            var comparedTargets = new List<string>
{
    first.Ten,
    second.Ten
}
 .Where(x => !string.IsNullOrWhiteSpace(x))
 .Distinct(StringComparer.OrdinalIgnoreCase)
 .ToList();

            await _conversationPreferenceService.SetComparedProductsAsync(
                conversationId,
                comparedTargets);

            var latestProfile = await _conversationPreferenceService.GetAsync(conversationId);
            latestProfile.LastComparedProducts.Clear();
            latestProfile.LastComparedProducts.AddRange(comparedTargets);
            latestProfile.HasActiveCompareContext = comparedTargets.Count >= 2;
            latestProfile.ActiveFlow = ChatFlowType.Compare;
            latestProfile.HasActiveRecommendationContext = false;
            latestProfile.UpdatedAtUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(resolvedFeature))
            {
                latestProfile.LastComparisonFeature = resolvedFeature;
            }

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply,
                Products = new List<ChatProductCard>
    {
        ChatProductCardMapper.Map(first),
        ChatProductCardMapper.Map(second)
    }
            };

        }

        private async Task<ProductSummaryDto?> FindBestMatchAsync(string productName)
        {
            var result = await _toolClient.SearchProductsAsync(productName, 5);
            if (result?.Items == null || !result.Items.Any())
            {
                return null;
            }

            return result.Items
                .OrderByDescending(x => string.Equals(x.Ten, productName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(productName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.SoLuong)
                .FirstOrDefault();
        }

        private async Task<ProductSummaryDto?> FindBestBrandRepresentativeAsync(string brand, ParsedIntent intent)
        {
            decimal? minPrice = intent.PriceMin;
            decimal? maxPrice = intent.PriceMax;

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

            var primaryResult = await _toolClient.GetProductsByFiltersAsync(
                brand: brand,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: intent.Category,
                take: 20);

            var items = primaryResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            if (items.Count == 0 && !string.IsNullOrWhiteSpace(intent.Category))
            {
                var fallbackByCategory = await _toolClient.GetProductsByFiltersAsync(
                    brand: brand,
                    minPrice: minPrice,
                    maxPrice: maxPrice,
                    category: null,
                    take: 20);

                items = fallbackByCategory?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();
            }

            if (items.Count == 0)
            {
                var fallbackNoPrice = await _toolClient.GetProductsByFiltersAsync(
                    brand: brand,
                    minPrice: null,
                    maxPrice: null,
                    category: intent.Category,
                    take: 20);

                items = fallbackNoPrice?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();
            }

            return SelectRepresentativeByBudget(items, intent);
        }

        private static ProductSummaryDto? SelectRepresentativeByBudget(
            List<ProductSummaryDto> items,
            ParsedIntent intent)
        {
            if (items == null || items.Count == 0)
                return null;

            var inStock = items.Where(x => x.SoLuong > 0).ToList();
            var candidates = inStock.Count > 0 ? inStock : items;

            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                var underMax = candidates
                    .Where(x => x.Gia <= intent.PriceMax.Value)
                    .OrderByDescending(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .ToList();

                if (underMax.Count > 0)
                    return underMax[0];
            }

            if (intent.FilterType == PriceFilterType.Range && intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                var inRange = candidates
                    .Where(x => x.Gia >= intent.PriceMin.Value && x.Gia <= intent.PriceMax.Value)
                    .OrderByDescending(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .ToList();

                if (inRange.Count > 0)
                    return inRange[0];
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                return candidates
                    .OrderBy(x => Math.Abs(x.Gia - target))
                    .ThenByDescending(x => x.SoLuong)
                    .FirstOrDefault();
            }

            if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                var aboveMin = candidates
                    .Where(x => x.Gia >= intent.PriceMin.Value)
                    .OrderBy(x => x.Gia)
                    .ThenByDescending(x => x.SoLuong)
                    .ToList();

                if (aboveMin.Count > 0)
                    return aboveMin[0];
            }

            return candidates
                .OrderByDescending(x => x.SoLuong)
                .ThenByDescending(x => x.Gia)
                .FirstOrDefault();
        }

        private static List<string> ResolveExplicitComparisonTargets(ParsedIntent intent)
        {
            if (intent.MentionedProducts.Count >= 2)
            {
                return intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
            }

            return new List<string>();
        }

        private static List<string> ResolveContextComparisonTargets(CustomerPreferenceProfile profile)
        {
            if (profile.LastComparedProducts.Count >= 2)
            {
                return profile.LastComparedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
            }

            if (profile.LastMentionedProducts.Count >= 2)
            {
                return profile.LastMentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
            }

            if (profile.LastRecommendedProducts.Count >= 2)
            {
                return profile.LastRecommendedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
            }

            return new List<string>();
        }
        private static CompareQuestionKind DetectCompareQuestionKind(string message, ParsedIntent intent)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool isPriceQuestion =
                text.Contains("giá") ||
                text.Contains("gia") ||
                text.Contains("bao nhiêu") ||
                text.Contains("bao nhieu") ||
                text.Contains("mức giá");

            bool isFeatureQuestion =
                !string.IsNullOrWhiteSpace(intent?.ComparisonFeature) ||
                text.Contains("cốp") ||
                text.Contains("cop") ||
                text.Contains("chống chân") ||
                text.Contains("chong chan") ||
                text.Contains("tiết kiệm") ||
                text.Contains("tiet kiem") ||
                text.Contains("êm") ||
                text.Contains("em") ||
                text.Contains("đẹp") ||
                text.Contains("dep") ||
                text.Contains("hợp nữ") ||
                text.Contains("hop nu");

            if (isPriceQuestion)
                return CompareQuestionKind.Price;

            if (isFeatureQuestion)
                return CompareQuestionKind.Feature;

            return CompareQuestionKind.General;
        }

        private static List<string> ResolveExplicitBrandComparisonTargets(
            string normalizedMessage,
            ParsedIntent intent)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            var results = new List<string>();

            AddBrandIfMentioned(text, "honda", "Honda", results);
            AddBrandIfMentioned(text, "yamaha", "Yamaha", results);
            AddBrandIfMentioned(text, "suzuki", "Suzuki", results);
            AddBrandIfMentioned(text, "sym", "SYM", results);
            AddBrandIfMentioned(text, "piaggio", "Piaggio", results);

            if (!string.IsNullOrWhiteSpace(intent?.Brand))
            {
                results.Add(intent.Brand.Trim());
            }

            return results
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();
        }

        private static void AddBrandIfMentioned(string text, string keyword, string displayName, List<string> results)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
                return;

            if (HasWholeWord(text, keyword))
            {
                results.Add(displayName);
            }
        }

        private static bool HasWholeWord(string text, string keyword)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
                return false;

            return System.Text.RegularExpressions.Regex.IsMatch(
                text,
                $@"(^|\s){System.Text.RegularExpressions.Regex.Escape(keyword)}(\s|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        private static string? ResolveComparisonFeature(ParsedIntent intent, string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();
            var rawFeature = intent?.ComparisonFeature?.Trim().ToLowerInvariant();

            // 1. Ưu tiên tín hiệu explicit ngay từ câu user
            if (text.Contains("đẹp hơn") || text.Contains("dep hon") ||
                text.Contains("thanh lịch hơn") || text.Contains("thanh lich hon") ||
                text.Contains("mềm mại hơn") || text.Contains("mem mai hon") ||
                text.Contains("kiểu dáng") || text.Contains("kieu dang"))
                return "design_fit";

            if (text.Contains("cốp rộng") || text.Contains("cop rong"))
                return "storage";

            if (text.Contains("dễ chống chân") || text.Contains("de chong chan") ||
                text.Contains("yên thấp") || text.Contains("yen thap"))
                return "low_seat";

            if (text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang"))
                return "fuel_saving";

            if (text.Contains("hợp nữ") || text.Contains("hop nu") ||
                text.Contains("nữ tính hơn") || text.Contains("nu tinh hon"))
                return "female_fit";

            if (text.Contains("đi làm") || text.Contains("di lam"))
                return "work_fit";

            if (text.Contains("đi học") || text.Contains("di hoc") ||
                text.Contains("sinh viên") || text.Contains("sinh vien"))
                return "school_fit";

            // 2. Sau đó mới normalize giá trị từ parser/LLM
            return rawFeature switch
            {
                "storage" => "storage",
                "large_storage" => "storage",
                "cop_rong" => "storage",

                "low_seat" => "low_seat",
                "easy_control" => "low_seat",
                "de_chong_chan" => "low_seat",

                "fuel_saving" => "fuel_saving",
                "tiet_kiem_xang" => "fuel_saving",

                "female_fit" => "female_fit",
                "hop_nu" => "female_fit",
                "nu_tinh" => "female_fit",

                "work_fit" => "work_fit",
                "di_lam" => "work_fit",

                "school_fit" => "school_fit",
                "di_hoc" => "school_fit",
                "sinh_vien" => "school_fit",

                "design_fit" => "design_fit",
                "style_fit" => "design_fit",
                "beauty" => "design_fit",
                "dep" => "design_fit",
                "thanh_lich" => "design_fit",

                _ => null
            };
        }
        private enum CompareQuestionKind
        {
            General = 0,
            Price = 1,
            Feature = 2
        }
        private static string BuildRagCompareQuery(
            ProductSummaryDto first,
            ProductSummaryDto second,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"So sánh {first.Ten} và {second.Ten}.");
            sb.AppendLine($"Câu hỏi hiện tại: {normalizedMessage}");

            if (!string.IsNullOrWhiteSpace(intent.ComparisonFeature))
                sb.AppendLine($"Tiêu chí so sánh chính: {intent.ComparisonFeature}");

            if (!string.IsNullOrWhiteSpace(profile.Target))
                sb.AppendLine($"Đối tượng: {profile.Target}");

            if (profile.ForWork)
                sb.AppendLine("Nhu cầu: đi làm");

            if (profile.ForSchool)
                sb.AppendLine("Nhu cầu: đi học");

            if (profile.NeedsLowSeat)
                sb.AppendLine("Ưu tiên: dễ chống chân");

            if (profile.WantsLargeStorage)
                sb.AppendLine("Ưu tiên: cốp rộng");

            if (profile.WantsFuelSaving)
                sb.AppendLine("Ưu tiên: tiết kiệm xăng");

            sb.AppendLine("Hãy nêu ngắn gọn mẫu nào hợp hơn theo từng trường hợp.");

            return sb.ToString().Trim();
        }

          private static string BuildDeterministicCompareReply(
     ProductSummaryDto first,
     ProductSummaryDto second,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage,
     string? ragContext,
      bool isFollowUpCompare,
      string? budgetHint)
        {
            var feature = ResolveComparisonFeature(intent, normalizedMessage)
               ?? InferFeatureFromMessageOnly(normalizedMessage);

            var verdict = feature switch
            {
                "storage" => BuildStorageVerdict(first, second, isFollowUpCompare),
                "fuel_saving" => BuildFuelSavingVerdict(first, second, isFollowUpCompare),
                "low_seat" => BuildLowSeatVerdict(first, second, isFollowUpCompare),
                "female_fit" => BuildFemaleVerdict(first, second, isFollowUpCompare),
                "work_fit" => BuildWorkVerdict(first, second, isFollowUpCompare),
                "school_fit" => BuildSchoolVerdict(first, second, isFollowUpCompare),
                "design_fit" => BuildDesignVerdict(first, second, isFollowUpCompare),
                _ => BuildGeneralVerdict(first, second, isFollowUpCompare)
            };
            // Nếu là follow-up thì trả lời ngắn, không lặp lại giá/tồn kho
            if (isFollowUpCompare)
            {
                var lines = new List<string> { verdict };

                var shortHint = ExtractShortHintForFollowUp(ragContext, feature);
                if (!string.IsNullOrWhiteSpace(shortHint))
                {
                    lines.Add(shortHint);
                }

                return string.Join("\n", lines).Trim();
            }

            // Nếu là câu compare đầu tiên thì vẫn trả lời đầy đủ hơn
            var fullLines = new List<string>
    {
        $"Mình so sánh nhanh **{first.Ten}** và **{second.Ten}** cho bạn:",
        string.Empty,
        $"- **{first.Ten}**: giá {first.Gia:N0} VNĐ, còn {first.SoLuong} chiếc, hãng {first.ThuongHieu}, loại {first.Loai}, {FormatCc(first.CC)}.",
        $"- **{second.Ten}**: giá {second.Gia:N0} VNĐ, còn {second.SoLuong} chiếc, hãng {second.ThuongHieu}, loại {second.Loai}, {FormatCc(second.CC)}.",
        string.Empty,
        verdict
    };

            if (!string.IsNullOrWhiteSpace(budgetHint))
            {
                fullLines.Insert(1, $"Trong tầm **{budgetHint}**, mình lấy mỗi hãng một mẫu đại diện để so sánh.");
            }

            fullLines.Add(string.Empty);
            fullLines.Add("Ưu/Nhược nhanh:");
            fullLines.Add($"- **{first.Ten}**: {BuildProsConsSummary(first, second)}");
            fullLines.Add($"- **{second.Ten}**: {BuildProsConsSummary(second, first)}");

            var shortHintFull = ExtractShortHintForFollowUp(ragContext, feature);
            if (!string.IsNullOrWhiteSpace(shortHintFull))
            {
                fullLines.Add(string.Empty);
                fullLines.Add(shortHintFull);
            }

            return string.Join("\n", fullLines).Trim();
        }
        private static string? InferFeatureFromMessageOnly(string message)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            if (text.Contains("đẹp") || text.Contains("dep") ||
                text.Contains("thanh lịch") || text.Contains("thanh lich") ||
                text.Contains("mềm mại") || text.Contains("mem mai"))
                return "design_fit";

            return null;
        }

        private static string BuildStorageVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Freego", "Latte", "Lead", "Address") &&
                !ContainsAny(second.Ten, "Freego", "Latte", "Lead", "Address"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{first.Ten}** nhỉnh hơn, nên tiện mang đồ hơn."
                    : $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{first.Ten}**.";
            }

            if (ContainsAny(second.Ten, "Freego", "Latte", "Lead", "Address") &&
                !ContainsAny(first.Ten, "Freego", "Latte", "Lead", "Address"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{second.Ten}** nhỉnh hơn, nên tiện mang đồ hơn."
                    : $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{second.Ten}**.";
            }
            if (first.Ten.Contains("Burgman", StringComparison.OrdinalIgnoreCase) &&
    !second.Ten.Contains("Burgman", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{first.Ten}** nhỉnh hơn, nên tiện mang đồ hơn trong hai mẫu này."
                    : $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{first.Ten}**.";
            }

            if (second.Ten.Contains("Burgman", StringComparison.OrdinalIgnoreCase) &&
                !first.Ten.Contains("Burgman", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{second.Ten}** nhỉnh hơn, nên tiện mang đồ hơn trong hai mẫu này."
                    : $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{second.Ten}**.";
            }

            if (first.Ten.Contains("Air Blade", StringComparison.OrdinalIgnoreCase) &&
                (second.Ten.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                 second.Ten.Contains("Exciter", StringComparison.OrdinalIgnoreCase)))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{first.Ten}** sẽ tiện hơn, còn **{second.Ten}** thiên về kiểu dáng và cảm giác lái hơn."
                    : $"Nếu xét theo tiêu chí **cốp rộng** thì mình nghiêng về **{first.Ten}**, còn **{second.Ten}** sẽ thiên về kiểu dáng và cảm giác lái hơn.";
            }

            if (second.Ten.Contains("Air Blade", StringComparison.OrdinalIgnoreCase) &&
                (first.Ten.Contains("Winner", StringComparison.OrdinalIgnoreCase) ||
                 first.Ten.Contains("Exciter", StringComparison.OrdinalIgnoreCase)))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{second.Ten}** sẽ tiện hơn, còn **{first.Ten}** thiên về kiểu dáng và cảm giác lái hơn."
                    : $"Nếu xét theo tiêu chí **cốp rộng** thì mình nghiêng về **{second.Ten}**, còn **{first.Ten}** sẽ thiên về kiểu dáng và cảm giác lái hơn.";
            }
            if (first.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) &&
    second.Ten.Contains("Future", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "Nếu xét riêng về **cốp rộng** thì **Honda Vision** nhỉnh hơn, còn **Honda Future** sẽ thiên về xe số thực dụng hơn."
                    : "Nếu xét theo tiêu chí **cốp rộng** thì mình nghiêng về **Honda Vision**, còn **Honda Future** sẽ hợp hơn nếu bạn ưu tiên xe số thực dụng.";
            }

            if (first.Ten.Contains("Future", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "Nếu xét riêng về **cốp rộng** thì **Honda Vision** nhỉnh hơn, còn **Honda Future** sẽ thiên về xe số thực dụng hơn."
                    : "Nếu xét theo tiêu chí **cốp rộng** thì mình nghiêng về **Honda Vision**, còn **Honda Future** sẽ hợp hơn nếu bạn ưu tiên xe số thực dụng.";
            }
            return isFollowUpCompare
    ? "Nếu xét riêng về **cốp rộng** thì mình sẽ nghiêng về mẫu thiên về xe ga hoặc tính tiện dụng hơn trong hai xe này."
    : "Nếu xét theo tiêu chí **cốp rộng** thì mình sẽ nghiêng về mẫu thiên về xe ga hoặc tính tiện dụng hơn trong hai xe này.";
        }
        private static string BuildFuelSavingVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Vision", "Wave", "Sirius", "Future") &&
                !ContainsAny(second.Ten, "Vision", "Wave", "Sirius", "Future"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **tiết kiệm xăng** thì **{first.Ten}** nhỉnh hơn để cân nhắc."
                    : $"Nếu bạn ưu tiên **tiết kiệm xăng** thì **{first.Ten}** nhỉnh hơn để cân nhắc.";
            }

            if (ContainsAny(second.Ten, "Vision", "Wave", "Sirius", "Future") &&
                !ContainsAny(first.Ten, "Vision", "Wave", "Sirius", "Future"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **tiết kiệm xăng** thì **{second.Ten}** nhỉnh hơn để cân nhắc."
                    : $"Nếu bạn ưu tiên **tiết kiệm xăng** thì **{second.Ten}** nhỉnh hơn để cân nhắc.";
            }

            return isFollowUpCompare
                ? "Nếu xét riêng về **tiết kiệm xăng** thì hai mẫu này không chênh quá rõ trên dữ liệu hiện có."
                : "Nếu xét theo hướng **tiết kiệm xăng**, hai mẫu này không chênh quá rõ trên dữ liệu hiện có, nên bạn có thể chốt thêm theo kiểu dáng hoặc nhu cầu sử dụng.";
        }

        private static string BuildLowSeatVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Zip", "Vision", "Latte") &&
                !ContainsAny(second.Ten, "Zip", "Vision", "Latte"))
            {
                return isFollowUpCompare
                    ? $"Nếu ưu tiên **dễ chống chân** thì **{first.Ten}** sẽ hợp hơn."
                    : $"Nếu bạn ưu tiên **dễ chống chân / yên thấp** thì **{first.Ten}** hợp hơn.";
            }

            if (ContainsAny(second.Ten, "Zip", "Vision", "Latte") &&
                !ContainsAny(first.Ten, "Zip", "Vision", "Latte"))
            {
                return isFollowUpCompare
                    ? $"Nếu ưu tiên **dễ chống chân** thì **{second.Ten}** sẽ hợp hơn."
                    : $"Nếu bạn ưu tiên **dễ chống chân / yên thấp** thì **{second.Ten}** hợp hơn.";
            }

            // Case riêng rất hay gặp: Vision vs Latte
            if (first.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "**Honda Vision** sẽ dễ chống chân hơn một chút vì xe gọn và dễ kiểm soát hơn."
                    : "Nếu ưu tiên **dễ chống chân** thì mình sẽ nghiêng hơn về **Honda Vision**, vì mẫu này gọn và dễ kiểm soát hơn.";
            }

            if (first.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "**Honda Vision** sẽ dễ chống chân hơn một chút vì xe gọn và dễ kiểm soát hơn."
                    : "Nếu ưu tiên **dễ chống chân** thì mình sẽ nghiêng hơn về **Honda Vision**, vì mẫu này gọn và dễ kiểm soát hơn.";
            }

            return isFollowUpCompare
                ? "Nếu xét riêng về **dễ chống chân** thì mình sẽ ưu tiên mẫu có dáng gọn hơn trong hai xe này."
                : "Về tiêu chí **dễ chống chân**, mình sẽ ưu tiên mẫu có dáng gọn hơn trong hai xe này.";
        }

        private static string BuildPriceCompareReply(ProductSummaryDto first, ProductSummaryDto second, string? budgetHint)
        {
            var cheaper = first.Gia <= second.Gia ? first : second;
            var priceGap = Math.Abs(first.Gia - second.Gia);

            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(budgetHint))
            {
                sb.AppendLine($"Trong tầm **{budgetHint}**, hai mẫu đại diện này có mức giá như sau:");
            }
            else
            {
                sb.AppendLine("Hai mẫu này hiện có mức giá như sau:");
            }

            sb.AppendLine();
            sb.AppendLine($"- **{first.Ten}**: giá {first.Gia:N0} VNĐ, còn {first.SoLuong} chiếc, hãng {first.ThuongHieu}, loại {first.Loai}, {FormatCc(first.CC)}.");
            sb.AppendLine($"- **{second.Ten}**: giá {second.Gia:N0} VNĐ, còn {second.SoLuong} chiếc, hãng {second.ThuongHieu}, loại {second.Loai}, {FormatCc(second.CC)}.");
            sb.AppendLine();
            sb.AppendLine($"Chênh lệch giá khoảng **{priceGap:N0} VNĐ**.");
            sb.AppendLine($"Nếu bạn ưu tiên giá mềm hơn thì mình nghiêng về **{cheaper.Ten}**.");
            sb.AppendLine();
            sb.AppendLine("Ưu/Nhược nhanh:");
            sb.AppendLine($"- **{first.Ten}**: {BuildProsConsSummary(first, second)}");
            sb.AppendLine($"- **{second.Ten}**: {BuildProsConsSummary(second, first)}");

            return sb.ToString().Trim();
        }

        private static string BuildProsConsSummary(ProductSummaryDto candidate, ProductSummaryDto competitor)
        {
            var pros = new List<string>();
            var cons = new List<string>();

            if (candidate.Gia < competitor.Gia)
            {
                pros.Add("giá mềm hơn");
            }
            else if (candidate.Gia > competitor.Gia)
            {
                cons.Add("giá cao hơn");
            }

            if (candidate.SoLuong > competitor.SoLuong)
            {
                pros.Add("tồn kho tốt hơn");
            }
            else if (candidate.SoLuong < competitor.SoLuong)
            {
                cons.Add("tồn kho thấp hơn");
            }

            if (!string.IsNullOrWhiteSpace(candidate.Loai))
            {
                if (candidate.Loai.Contains("ga", StringComparison.OrdinalIgnoreCase))
                {
                    pros.Add("đi phố linh hoạt");
                }
                else if (candidate.Loai.Contains("số", StringComparison.OrdinalIgnoreCase))
                {
                    pros.Add("chi phí vận hành dễ chịu");
                }
                else if (candidate.Loai.Contains("côn", StringComparison.OrdinalIgnoreCase))
                {
                    pros.Add("cảm giác lái thể thao hơn");
                }
            }

            if (candidate.CC.HasValue && competitor.CC.HasValue)
            {
                if (candidate.CC.Value > competitor.CC.Value)
                {
                    pros.Add("động cơ mạnh hơn");
                }
                else if (candidate.CC.Value < competitor.CC.Value)
                {
                    cons.Add("động cơ thấp hơn");
                }
            }

            if (pros.Count == 0)
            {
                pros.Add("thông số cân bằng");
            }

            if (cons.Count == 0)
            {
                cons.Add("ít khác biệt lớn ở dữ liệu hiện tại");
            }

            return $"Ưu: {string.Join(", ", pros.Distinct(StringComparer.OrdinalIgnoreCase).Take(2))}. Nhược: {string.Join(", ", cons.Distinct(StringComparer.OrdinalIgnoreCase).Take(2))}.";
        }

        private static string FormatCc(short? cc)
        {
            return cc.HasValue ? $"{cc.Value}cc" : "-";
        }

        private static string? BuildBudgetHint(ParsedIntent intent, string normalizedMessage)
        {
            if (intent == null)
                return null;

            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                return $"dưới {FormatMillion(intent.PriceMax.Value)} triệu";
            }

            if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                return $"từ {FormatMillion(intent.PriceMin.Value)} triệu trở lên";
            }

            if (intent.FilterType == PriceFilterType.Range && intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return $"{FormatMillion(intent.PriceMin.Value)} - {FormatMillion(intent.PriceMax.Value)} triệu";
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                return $"quanh {FormatMillion(intent.TargetPrice.Value)} triệu";
            }

            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            if (text.Contains("dưới 40") || text.Contains("duoi 40"))
            {
                return "dưới 40 triệu";
            }

            return null;
        }

        private static string FormatMillion(decimal price)
        {
            var million = price / 1_000_000m;
            return million % 1 == 0
                ? decimal.Truncate(million).ToString("0")
                : million.ToString("0.#");
        }
        private static string BuildFemaleVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstIsFemaleFit = ContainsAny(first.Ten, "Latte", "Grande", "Vision", "Zip");
            bool secondIsFemaleFit = ContainsAny(second.Ten, "Latte", "Grande", "Vision", "Zip");

            if (first.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "Nếu ưu tiên **hợp nữ hơn** thì mình nghiêng nhẹ về **Yamaha Latte**, còn **Honda Vision** sẽ thiên về gọn và dễ đi hơn."
                    : "Nếu ưu tiên **gọn, dễ đi và dễ làm quen** thì mình nghiêng về **Honda Vision**. Còn nếu ưu tiên **dáng mềm hơn và cảm giác hợp nữ hơn** thì **Yamaha Latte** nổi bật hơn. Nếu chốt nhanh theo hướng hợp nữ, mình sẽ nghiêng nhẹ về **Yamaha Latte**.";
            }

            if (first.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "Nếu ưu tiên **hợp nữ hơn** thì mình nghiêng nhẹ về **Yamaha Latte**, còn **Honda Vision** sẽ thiên về gọn và dễ đi hơn."
                    : "Nếu ưu tiên **dáng mềm hơn và cảm giác hợp nữ hơn** thì mình nghiêng về **Yamaha Latte**. Còn nếu ưu tiên **gọn, dễ đi và dễ làm quen** thì **Honda Vision** thực dụng hơn. Nếu chốt nhanh theo hướng hợp nữ, mình sẽ nghiêng nhẹ về **Yamaha Latte**.";
            }

            if (firstIsFemaleFit && !secondIsFemaleFit)
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **hợp nữ** thì mình nghiêng hơn về **{first.Ten}**."
                    : $"Nếu xét theo hướng **hợp nữ, dễ đi** thì mình nghiêng hơn về **{first.Ten}**.";

            if (secondIsFemaleFit && !firstIsFemaleFit)
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **hợp nữ** thì mình nghiêng hơn về **{second.Ten}**."
                    : $"Nếu xét theo hướng **hợp nữ, dễ đi** thì mình nghiêng hơn về **{second.Ten}**.";

            return isFollowUpCompare
                ? "Nếu chỉ chốt nhanh theo hướng **hợp nữ** thì mình sẽ nghiêng về mẫu có dáng mềm và dễ làm quen hơn."
                : "Nếu chỉ chốt nhanh theo hướng **hợp nữ** thì mình nghiêng hơn về mẫu có dáng mềm và dễ làm quen hơn trong hai xe này.";
        }
        private static string BuildDesignVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstElegant = ContainsAny(first.Ten, "Latte", "Grande", "Zip", "Attila");
            bool secondElegant = ContainsAny(second.Ten, "Latte", "Grande", "Zip", "Attila");

            bool firstNeutralPractical = ContainsAny(first.Ten, "Vision", "Air Blade", "Future", "Freego");
            bool secondNeutralPractical = ContainsAny(second.Ten, "Vision", "Air Blade", "Future", "Freego");

            if (first.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "Nếu xét theo hướng **đẹp / thanh lịch hơn** thì mình nghiêng về **Yamaha Latte**; còn **Honda Vision** sẽ thiên về gọn và thực dụng hơn."
                    : "Nếu xét theo hướng **đẹp / thanh lịch hơn** thì mình nghiêng về **Yamaha Latte**, vì mẫu này có dáng mềm và thiên về cảm giác thanh lịch hơn. Còn **Honda Vision** sẽ mạnh hơn ở sự gọn gàng và thực dụng.";
            }

            if (first.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                return isFollowUpCompare
                    ? "Nếu xét theo hướng **đẹp / thanh lịch hơn** thì mình nghiêng về **Yamaha Latte**; còn **Honda Vision** sẽ thiên về gọn và thực dụng hơn."
                    : "Nếu xét theo hướng **đẹp / thanh lịch hơn** thì mình nghiêng về **Yamaha Latte**, vì mẫu này có dáng mềm và thiên về cảm giác thanh lịch hơn. Còn **Honda Vision** sẽ mạnh hơn ở sự gọn gàng và thực dụng.";
            }

            if (firstElegant && !secondElegant)
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{first.Ten}**."
                    : $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{first.Ten}**, vì mẫu này thiên về dáng mềm và cảm giác thanh lịch hơn.";
            }

            if (secondElegant && !firstElegant)
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{second.Ten}**."
                    : $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{second.Ten}**, vì mẫu này thiên về dáng mềm và cảm giác thanh lịch hơn.";
            }

            if (firstNeutralPractical && !secondNeutralPractical)
            {
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{first.Ten}** dễ hợp hơn."
                    : $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{first.Ten}** dễ hợp hơn.";
            }

            if (secondNeutralPractical && !firstNeutralPractical)
            {
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{second.Ten}** dễ hợp hơn."
                    : $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{second.Ten}** dễ hợp hơn.";
            }

            return isFollowUpCompare
                ? "Nếu xét riêng về **kiểu dáng** thì hai mẫu này khá gần nhau, chỉ khác ở việc một mẫu thiên thanh lịch hơn còn mẫu kia thiên thực dụng hơn."
                : "Nếu xét riêng về **kiểu dáng** thì hai mẫu này khá gần nhau, và thường sẽ khác nhau ở gu: một bên thiên thanh lịch hơn, một bên thiên thực dụng hơn.";
        }
        private static string BuildWorkVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Air Blade", "Freego", "Future") &&
                !ContainsAny(second.Ten, "Air Blade", "Freego", "Future"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **đi làm** thì **{first.Ten}** hợp hơn."
                    : $"Nếu ưu tiên **đi làm hằng ngày** thì **{first.Ten}** hợp hơn.";
            }

            if (ContainsAny(second.Ten, "Air Blade", "Freego", "Future") &&
                !ContainsAny(first.Ten, "Air Blade", "Freego", "Future"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **đi làm** thì **{second.Ten}** hợp hơn."
                    : $"Nếu ưu tiên **đi làm hằng ngày** thì **{second.Ten}** hợp hơn.";
            }

            return isFollowUpCompare
                ? "Nếu xét theo hướng **đi làm** thì mình sẽ nghiêng về mẫu thực dụng và ổn định hơn."
                : "Nếu xét theo hướng **đi làm**, mình sẽ nghiêng về mẫu thực dụng và ổn định hơn trong hai xe này.";
        }

        private static string BuildSchoolVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Vision", "Wave", "Sirius") &&
                !ContainsAny(second.Ten, "Vision", "Wave", "Sirius"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **đi học / sinh viên** thì **{first.Ten}** dễ cân nhắc hơn."
                    : $"Nếu ưu tiên **đi học / sinh viên** thì **{first.Ten}** dễ cân nhắc hơn.";
            }

            if (ContainsAny(second.Ten, "Vision", "Wave", "Sirius") &&
                !ContainsAny(first.Ten, "Vision", "Wave", "Sirius"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét theo hướng **đi học / sinh viên** thì **{second.Ten}** dễ cân nhắc hơn."
                    : $"Nếu ưu tiên **đi học / sinh viên** thì **{second.Ten}** dễ cân nhắc hơn.";
            }

            return isFollowUpCompare
                ? "Nếu xét theo hướng **đi học** thì mình sẽ ưu tiên mẫu dễ đi và chi phí dùng lâu dài hơn."
                : "Nếu xét theo hướng **đi học**, mình sẽ ưu tiên mẫu dễ đi và chi phí dùng lâu dài hơn.";
        }

        private static string BuildGeneralVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (first.Gia < second.Gia)
            {
                return isFollowUpCompare
                    ? $"Nếu ưu tiên **giá mềm hơn** thì **{first.Ten}** lợi thế hơn."
                    : $"Nếu bạn ưu tiên **giá mềm hơn** thì **{first.Ten}** lợi thế hơn; còn nếu muốn cân nhắc theo cảm giác xe hoặc tiện ích thì mình có thể lọc tiếp cho bạn.";
            }

            if (second.Gia < first.Gia)
            {
                return isFollowUpCompare
                    ? $"Nếu ưu tiên **giá mềm hơn** thì **{second.Ten}** lợi thế hơn."
                    : $"Nếu bạn ưu tiên **giá mềm hơn** thì **{second.Ten}** lợi thế hơn; còn nếu muốn cân nhắc theo cảm giác xe hoặc tiện ích thì mình có thể lọc tiếp cho bạn.";
            }

            return isFollowUpCompare
                ? "Hai mẫu này đang khá ngang nhau ở dữ liệu cơ bản."
                : "Hai mẫu này đang khá ngang nhau ở dữ liệu cơ bản, nên có thể chốt tiếp theo tiêu chí như cốp rộng, dễ chống chân, đi làm hay tiết kiệm xăng.";
        }

        private static string InferFeatureFromProfile(CustomerPreferenceProfile profile)
        {
            if (profile.WantsLargeStorage) return "storage";
            if (profile.WantsFuelSaving) return "fuel_saving";
            if (profile.NeedsLowSeat) return "low_seat";
            if (profile.ForWork) return "work_fit";
            if (profile.ForSchool) return "school_fit";
            if (profile.PrefersFemaleStyle) return "female_fit";
            return "general";
        }

        private static string? ExtractShortHintForFollowUp(string? ragContext, string feature)
        {
            if (string.IsNullOrWhiteSpace(ragContext))
                return null;

            var text = ragContext.Trim();

            if (feature == "low_seat")
            {
                return "Nếu bạn thấp người hoặc hay dừng đèn đỏ nhiều thì mẫu gọn và dễ kiểm soát sẽ lợi thế hơn.";
            }

            if (feature == "storage")
            {
                return "Nếu bạn hay mang đồ hoặc đi làm hằng ngày thì tiêu chí cốp sẽ đáng ưu tiên hơn.";
            }

            if (feature == "female_fit")
            {
                return "Nếu bạn thích dáng mềm và thiên nữ tính hơn thì nên ưu tiên mẫu có kiểu dáng thanh lịch hơn.";
            }

            return null;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
    }
}
