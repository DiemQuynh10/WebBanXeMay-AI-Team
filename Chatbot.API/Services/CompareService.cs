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

        private static readonly Dictionary<string, string> ProductAliases =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["vision"] = "Honda Vision",
                ["vison"] = "Honda Vision",
                ["visison"] = "Honda Vision",
                ["vission"] = "Honda Vision",

                ["air blade"] = "Honda Air Blade",
                ["airblade"] = "Honda Air Blade",
                ["ab"] = "Honda Air Blade",

                ["freego"] = "Yamaha Freego",
                ["latte"] = "Yamaha Latte",
                ["grande"] = "Yamaha Grande",
                ["janus"] = "Yamaha Janus",

                ["zip"] = "Piaggio Zip 100",
                ["zip 100"] = "Piaggio Zip 100",

                ["future"] = "Honda Future",
                ["wave"] = "Honda Wave",
                ["lead"] = "Honda Lead",
                ["vario"] = "Honda Vario",
                ["winner"] = "Honda Winner X",
                ["winner x"] = "Honda Winner X",
                ["pcx"] = "Honda PCX",
                ["sh"] = "Honda SH 150i",

                ["sirius"] = "Yamaha Sirius",
                ["exciter"] = "Yamaha Exciter",

                ["address"] = "Suzuki Address 110",
                ["impulse"] = "Suzuki Impulse 125",

                ["attila"] = "SYM Attila Venus",
                ["attila venus"] = "SYM Attila Venus",
                ["shark"] = "SYM Shark Mini",
                ["shark mini"] = "SYM Shark Mini"
            };

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
            bool wantsCompareAll = LooksLikeCompareAllRequest(normalizedMessage);
            var targetNames = ResolveComparisonTargets(intent, profile, normalizedMessage);

            _logger.LogInformation(
                "Compare targets resolved. ConversationId={ConversationId}, CompareAll={CompareAll}, RawMentionedProducts={RawMentionedProducts}, FinalTargets={FinalTargets}",
                conversationId,
                wantsCompareAll,
                intent?.MentionedProducts != null ? string.Join(", ", intent.MentionedProducts) : "(null)",
                string.Join(", ", targetNames));

            bool isFollowUpCompare =
                profile != null &&
                profile.HasActiveCompareContext &&
                profile.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 2 &&
                (intent.MentionedProducts == null || intent.MentionedProducts.Count < 2);

            bool shouldForceFullCompareReply = LooksLikeFullCompareQuestion(normalizedMessage);

            if (targetNames.Count < 2)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Mình cần ít nhất 2 mẫu xe để so sánh. Bạn có thể nói rõ như \"Vision với Latte\" hoặc \"Freego với Air Blade\" nhé."
                };
            }

            var matchedProducts = new List<ProductSummaryDto>();
            foreach (var target in targetNames)
            {
                var match = await FindBestMatchAsync(target);
                if (match != null && !matchedProducts.Any(x => string.Equals(x.Ten, match.Ten, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedProducts.Add(match);
                }
            }

            if (matchedProducts.Count < 2)
            {
                var foundProducts = matchedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x.Ten))
                    .Select(x => x.Ten)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var missingTargets = targetNames
                    .Where(x => !foundProducts.Contains(x, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (matchedProducts.Count == 1)
                {
                    var found = matchedProducts[0];
                    var missingName = missingTargets.FirstOrDefault() ?? "mẫu xe còn lại";

                    return new ChatResponse
                    {
                        Success = true,
                        ConversationId = conversationId,
                        UsedAI = false,
                        Reply =
                            $"Hiện tại mình chưa tìm thấy thông tin về **{missingName}** trong hệ thống. " +
                            $"Tuy nhiên mình đã tìm thấy **{found.Ten}**. " +
                            $"Nếu bạn muốn, mình có thể:\n" +
                            $"- xem nhanh chi tiết **{found.Ten}**\n" +
                            $"- hoặc gợi ý một mẫu tương đương để so sánh tiếp."
                    };
                }

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Hiện tại mình chưa tìm thấy đủ thông tin về các mẫu bạn muốn so sánh trong hệ thống. Bạn có thể đổi sang mẫu khác hoặc để mình gợi ý xe tương đương nhé."
                };
            }

            var comparedTargets = matchedProducts
                .Select(x => x.Ten)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _conversationPreferenceService.SetComparedProductsAsync(conversationId, comparedTargets);

            var latestProfile = await _conversationPreferenceService.GetAsync(conversationId);
            latestProfile.LastComparedProducts.Clear();
            latestProfile.LastComparedProducts.AddRange(comparedTargets);
            latestProfile.HasActiveCompareContext = comparedTargets.Count >= 2;
            latestProfile.ActiveFlow = ChatFlowType.Compare;
            latestProfile.HasActiveRecommendationContext = false;
            latestProfile.UpdatedAtUtc = DateTime.UtcNow;

            if (matchedProducts.Count >= 3 && wantsCompareAll)
            {
                var compareAllReply = BuildCompareAllReply(matchedProducts, normalizedMessage);
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = compareAllReply,
                    Products = matchedProducts.Select(ChatProductCardMapper.Map).ToList()
                };
            }

            var first = matchedProducts[0];
            var second = matchedProducts[1];

            var questionKind = DetectCompareQuestionKind(normalizedMessage, intent);
            string? ragContext = null;
            try
            {
                var ragQuery = BuildRagCompareQuery(first, second, intent, profile, normalizedMessage);
                var ragResult = await _ragService.QueryAsync(ragQuery, topK: 4);
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
                "Compare feature resolved. ConversationId={ConversationId}, Message={Message}, RawComparisonFeature={RawComparisonFeature}, ResolvedFeature={ResolvedFeature}",
                conversationId,
                normalizedMessage,
                intent?.ComparisonFeature,
                resolvedFeature);

            if (!string.IsNullOrWhiteSpace(resolvedFeature))
            {
                latestProfile.LastComparisonFeature = resolvedFeature;
            }

            string reply = questionKind == CompareQuestionKind.Price
                ? BuildPriceCompareReply(first, second)
                : BuildDeterministicCompareReply(
                    first,
                    second,
                    intent,
                    profile,
                    normalizedMessage,
                    ragContext,
                    isFollowUpCompare && !shouldForceFullCompareReply);

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

        private static bool LooksLikeCompareAllRequest(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);
            return (text.Contains("tat ca") || text.Contains("het") || text.Contains("ben tren") || text.Contains("gan nhat") || text.Contains("vua roi"))
                   && (text.Contains("so sanh") || text.Contains("uu nhuoc") || text.Contains("tot hon") || text.Contains("khac nhau"));
        }

        private static bool LooksLikeFullCompareQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);
            return
                (text.Contains(" voi ") || text.Contains(" va ") || text.Contains(" hay ")) &&
                (text.Contains("cai nao") || text.Contains("xe nao") || text.Contains("mau nao") ||
                 text.Contains("on hon") || text.Contains("tot hon") || text.Contains("hop hon"));
        }

        private async Task<ProductSummaryDto?> FindBestMatchAsync(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return null;

            var normalizedCandidate = NormalizeProductCandidate(productName);
            var coreNormalized = ExtractCoreName(normalizedCandidate);
            var coreRaw = ExtractCoreName(productName);

            var searchTerms = new List<string?>
            {
                normalizedCandidate,
                productName,
                coreNormalized,
                coreRaw
            };

            foreach (var token in TokenizeForSearch(normalizedCandidate))
                searchTerms.Add(token);

            foreach (var token in TokenizeForSearch(productName))
                searchTerms.Add(token);

            var distinctTerms = searchTerms
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allItems = new List<ProductSummaryDto>();

            foreach (var term in distinctTerms)
            {
                var result = await _toolClient.SearchProductsAsync(term!, 12);
                if (result?.Items != null && result.Items.Any())
                {
                    allItems.AddRange(result.Items);
                }
            }

            var candidates = allItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.SoLuong).First())
                .ToList();

            if (!candidates.Any())
                return null;

            var ranked = candidates
                .Select(x => new { Product = x, Score = ScoreProductMatch(x, productName, normalizedCandidate) })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Product.SoLuong)
                .ToList();

            var best = ranked.FirstOrDefault();
            if (best == null || best.Score < 45)
                return null;

            return best.Product;
        }

        private static int ScoreProductMatch(ProductSummaryDto product, string rawName, string normalizedCandidate)
        {
            var productName = NormalizeText(product.Ten ?? string.Empty);
            var raw = NormalizeText(rawName);
            var normalized = NormalizeText(normalizedCandidate);
            var coreNormalized = ExtractCoreName(normalized);
            var coreRaw = ExtractCoreName(raw);

            int score = 0;

            if (string.Equals(productName, normalized, StringComparison.OrdinalIgnoreCase)) score += 140;
            if (string.Equals(productName, raw, StringComparison.OrdinalIgnoreCase)) score += 130;
            if (!string.IsNullOrWhiteSpace(coreNormalized) && string.Equals(productName, coreNormalized, StringComparison.OrdinalIgnoreCase)) score += 120;

            if (productName.Contains(normalized, StringComparison.OrdinalIgnoreCase)) score += 90;
            if (productName.Contains(raw, StringComparison.OrdinalIgnoreCase)) score += 80;
            if (!string.IsNullOrWhiteSpace(coreNormalized) && productName.Contains(coreNormalized, StringComparison.OrdinalIgnoreCase)) score += 70;
            if (!string.IsNullOrWhiteSpace(coreRaw) && productName.Contains(coreRaw, StringComparison.OrdinalIgnoreCase)) score += 60;

            var candidateTokens = TokenizeForSearch(normalized)
                .Concat(TokenizeForSearch(raw))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var token in candidateTokens)
            {
                if (productName.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score += 18;
            }

            var distance = LevenshteinDistance(productName, normalized);
            score += Math.Max(0, 24 - distance * 2);

            return score;
        }

        private static IEnumerable<string> TokenizeForSearch(string text)
        {
            var normalized = NormalizeText(text);
            return normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => x.Length >= 3 && x is not ("honda" or "yamaha" or "suzuki" or "piaggio" or "sym"));
        }

        private static List<string> ResolveComparisonTargets(ParsedIntent intent, CustomerPreferenceProfile profile, string message)
        {
            var candidates = new List<string>();
            bool wantsCompareAll = LooksLikeCompareAllRequest(message);

            if (intent?.MentionedProducts != null && intent.MentionedProducts.Count > 0)
            {
                candidates.AddRange(intent.MentionedProducts);
            }

            if (wantsCompareAll)
            {
                if (profile?.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count >= 2)
                    candidates.AddRange(profile.LastRecommendedProducts);

                if (profile?.LastMentionedProducts != null && profile.LastMentionedProducts.Count >= 2)
                    candidates.AddRange(profile.LastMentionedProducts);
            }

            if (candidates.Count < 2 &&
                profile?.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 2)
            {
                candidates.AddRange(profile.LastComparedProducts);
            }

            if (candidates.Count < 2 &&
                profile?.LastMentionedProducts != null &&
                profile.LastMentionedProducts.Count >= 2)
            {
                candidates.AddRange(profile.LastMentionedProducts);
            }

            if (candidates.Count < 2 &&
                profile?.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count >= 2)
            {
                candidates.AddRange(profile.LastRecommendedProducts);
            }

            var distinct = candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeProductCandidate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return wantsCompareAll ? distinct : distinct.Take(2).ToList();
        }

        private static CompareQuestionKind DetectCompareQuestionKind(string message, ParsedIntent intent)
        {
            var text = NormalizeText(message);

            bool isPriceQuestion =
                text.Contains("gia") ||
                text.Contains("bao nhieu") ||
                text.Contains("muc gia");

            bool isFeatureQuestion =
                !string.IsNullOrWhiteSpace(intent?.ComparisonFeature) ||
                text.Contains("cop") ||
                text.Contains("chong chan") ||
                text.Contains("tiet kiem") ||
                text.Contains("em") ||
                text.Contains("dep") ||
                text.Contains("hop nu") ||
                text.Contains("uu nhuoc");

            if (isPriceQuestion)
                return CompareQuestionKind.Price;

            if (isFeatureQuestion)
                return CompareQuestionKind.Feature;

            return CompareQuestionKind.General;
        }

        private static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text.Trim().ToLowerInvariant();
            var map = new Dictionary<char, char>
            {
                ['à'] = 'a',
                ['á'] = 'a',
                ['ạ'] = 'a',
                ['ả'] = 'a',
                ['ã'] = 'a',
                ['â'] = 'a',
                ['ầ'] = 'a',
                ['ấ'] = 'a',
                ['ậ'] = 'a',
                ['ẩ'] = 'a',
                ['ẫ'] = 'a',
                ['ă'] = 'a',
                ['ằ'] = 'a',
                ['ắ'] = 'a',
                ['ặ'] = 'a',
                ['ẳ'] = 'a',
                ['ẵ'] = 'a',
                ['è'] = 'e',
                ['é'] = 'e',
                ['ẹ'] = 'e',
                ['ẻ'] = 'e',
                ['ẽ'] = 'e',
                ['ê'] = 'e',
                ['ề'] = 'e',
                ['ế'] = 'e',
                ['ệ'] = 'e',
                ['ể'] = 'e',
                ['ễ'] = 'e',
                ['ì'] = 'i',
                ['í'] = 'i',
                ['ị'] = 'i',
                ['ỉ'] = 'i',
                ['ĩ'] = 'i',
                ['ò'] = 'o',
                ['ó'] = 'o',
                ['ọ'] = 'o',
                ['ỏ'] = 'o',
                ['õ'] = 'o',
                ['ô'] = 'o',
                ['ồ'] = 'o',
                ['ố'] = 'o',
                ['ộ'] = 'o',
                ['ổ'] = 'o',
                ['ỗ'] = 'o',
                ['ơ'] = 'o',
                ['ờ'] = 'o',
                ['ớ'] = 'o',
                ['ợ'] = 'o',
                ['ở'] = 'o',
                ['ỡ'] = 'o',
                ['ù'] = 'u',
                ['ú'] = 'u',
                ['ụ'] = 'u',
                ['ủ'] = 'u',
                ['ũ'] = 'u',
                ['ư'] = 'u',
                ['ừ'] = 'u',
                ['ứ'] = 'u',
                ['ự'] = 'u',
                ['ử'] = 'u',
                ['ữ'] = 'u',
                ['ỳ'] = 'y',
                ['ý'] = 'y',
                ['ỵ'] = 'y',
                ['ỷ'] = 'y',
                ['ỹ'] = 'y',
                ['đ'] = 'd'
            };

            var chars = value.Select(c => map.TryGetValue(c, out var mapped) ? mapped : c).ToArray();
            value = new string(chars);
            value = value.Replace("_", " ");
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
            return value;
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
                    dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
                }
            }

            return dp[a.Length, b.Length];
        }

        private static string NormalizeProductCandidate(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return rawName;

            var normalized = NormalizeText(rawName);
            if (ProductAliases.TryGetValue(normalized, out var exactAlias))
                return exactAlias;

            string? bestKey = null;
            int bestDistance = int.MaxValue;

            foreach (var key in ProductAliases.Keys)
            {
                var distance = LevenshteinDistance(normalized, key);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestKey = key;
                }
            }

            if (bestKey != null && bestDistance <= 2)
                return ProductAliases[bestKey];

            return rawName;
        }

        private static string ExtractCoreName(string name)
        {
            var text = NormalizeText(name);
            foreach (var brand in new[] { "honda", "yamaha", "suzuki", "piaggio", "sym" })
            {
                if (text.StartsWith(brand + " "))
                    return text.Substring(brand.Length).Trim();
            }
            return text;
        }

        private static string? ResolveComparisonFeature(ParsedIntent intent, string message)
        {
            var text = NormalizeText(message);
            var rawFeature = intent?.ComparisonFeature?.Trim().ToLowerInvariant();

            if (text.Contains("dep hon") || text.Contains("thanh lich hon") || text.Contains("mem mai hon") || text.Contains("kieu dang"))
                return "design_fit";
            if (text.Contains("cop rong"))
                return "storage";
            if (text.Contains("de chong chan") || text.Contains("yen thap"))
                return "low_seat";
            if (text.Contains("tiet kiem xang"))
                return "fuel_saving";
            if (text.Contains("hop nu") || text.Contains("nu tinh hon"))
                return "female_fit";
            if (text.Contains("di lam"))
                return "work_fit";
            if (text.Contains("di hoc") || text.Contains("sinh vien"))
                return "school_fit";

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
            if (profile.ForWork) sb.AppendLine("Nhu cầu: đi làm");
            if (profile.ForSchool) sb.AppendLine("Nhu cầu: đi học");
            if (profile.NeedsLowSeat) sb.AppendLine("Ưu tiên: dễ chống chân");
            if (profile.WantsLargeStorage) sb.AppendLine("Ưu tiên: cốp rộng");
            if (profile.WantsFuelSaving) sb.AppendLine("Ưu tiên: tiết kiệm xăng");

            sb.AppendLine("Hãy nêu ngắn gọn mẫu nào hợp hơn theo từng trường hợp.");
            return sb.ToString().Trim();
        }

        private static string BuildCompareAllReply(List<ProductSummaryDto> products, string normalizedMessage)
        {
            var lines = new List<string>();

            lines.Add("Mình tóm lại nhanh để bạn dễ chọn giữa các mẫu này:");
            lines.Add("");

            // 1. Phân nhóm theo cảm nhận thực tế
            var easyRide = products.Where(x => ContainsAny(x.Ten, "Vision", "Janus", "Latte", "Shark", "Attila")).ToList();
            var balanced = products.Where(x => ContainsAny(x.Ten, "Air Blade", "Freego")).ToList();

            if (easyRide.Any())
            {
                lines.Add($"- Nhóm **dễ đi, nhẹ, hợp đi phố**: {string.Join(", ", easyRide.Select(x => x.Ten))}");
            }

            if (balanced.Any())
            {
                lines.Add($"- Nhóm **đầm hơn, máy khỏe hơn**: {string.Join(", ", balanced.Select(x => x.Ten))}");
            }

            var cheapest = products.OrderBy(x => x.Gia).First();
            lines.Add("");
            lines.Add($"- Nếu ưu tiên **giá mềm nhất** thì **{cheapest.Ten}** ({cheapest.Gia:N0} VNĐ) là lựa chọn dễ cân nhắc.");

            var bestDaily = products.FirstOrDefault(x => ContainsAny(x.Ten, "Vision", "Freego", "Air Blade")) ?? cheapest;
            lines.Add($"- Nếu cần **đi hằng ngày ổn định, ít phải nghĩ** thì **{bestDaily.Ten}** là mẫu dễ chọn nhất.");

            lines.Add("");
            lines.Add("Ưu nhược điểm nhanh từng mẫu:");

            foreach (var p in products)
            {
                lines.Add($"- **{p.Ten}**: {BuildRealProsCons(p)}");
            }
            
            lines.Add("");
            lines.Add(BuildFinalMultiSuggestion(products));

            return string.Join("\n", lines).Trim();
        }
        private static string BuildFinalMultiSuggestion(List<ProductSummaryDto> products)
        {
            var cheapest = products.OrderBy(x => x.Gia).First();
            var bestDaily = products.FirstOrDefault(x => ContainsAny(x.Ten, "Vision", "Freego")) ?? cheapest;

            return $"Nếu bạn muốn **rẻ nhất** thì chọn **{cheapest.Ten}**.\n" +
                   $"Nếu bạn muốn **dễ đi, ổn định lâu dài** thì chọn **{bestDaily.Ten}**.\n" +
                   $"Nếu bạn cần mình chọn giúp theo nhu cầu cụ thể (đi làm, đi học, chiều cao...) thì mình lọc kỹ hơn cho bạn.";
        }
        private static string BuildRealProsCons(ProductSummaryDto p)
        {
            if (ContainsAny(p.Ten, "Vision"))
                return "dễ đi, ít hao xăng; nhược điểm là máy không mạnh";

            if (ContainsAny(p.Ten, "Attila", "Shark"))
                return "giá mềm, đi phố ổn; nhược điểm là thương hiệu không mạnh bằng Honda";

            if (ContainsAny(p.Ten, "Air Blade"))
                return "máy khỏe, đi đầm; nhược điểm là giá cao hơn";

            return "đi ổn trong tầm giá; nhược điểm là cần cân nhắc thêm theo nhu cầu cụ thể";
        }
        private static string BuildShortProsCons(ProductSummaryDto product)
        {
            if (ContainsAny(product.Ten, "Wave", "Sirius"))
                return "ưu điểm là giá mềm, dễ dùng lâu dài; nhược điểm là tiện ích và độ thời trang không bằng xe ga.";
            if (ContainsAny(product.Ten, "Future"))
                return "ưu điểm là thực dụng, đi làm ổn; nhược điểm là không tiện và gọn kiểu xe ga.";
            if (ContainsAny(product.Ten, "Vision", "Janus", "Latte", "Grande", "Zip"))
                return "ưu điểm là dáng dễ đi, hợp đi phố; nhược điểm là giá và chi phí có thể nhỉnh hơn xe số cùng tầm.";
            if (ContainsAny(product.Ten, "Air Blade", "Freego", "Lead"))
                return "ưu điểm là cân bằng giữa tiện ích và đi hằng ngày; nhược điểm là sẽ to hoặc đầm xe hơn nhóm gọn nhẹ.";
            return "ưu điểm là có nét riêng trong tầm giá; nhược điểm là cần chốt thêm theo nhu cầu cụ thể như đi làm, tiết kiệm xăng hay cốp rộng.";
        }

        private static string BuildDeterministicCompareReply(
     ProductSummaryDto first,
     ProductSummaryDto second,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage,
     string? ragContext,
     bool isFollowUpCompare)
        {
            var lines = new List<string>();

            if (!isFollowUpCompare)
            {
                lines.Add($"Mình so sánh nhanh **{first.Ten}** và **{second.Ten}** cho bạn:");
                lines.Add("");
            }

            // 1. Giá
            if (first.Gia != second.Gia)
            {
                var cheaper = first.Gia < second.Gia ? first : second;
                lines.Add($"- Về giá: **{cheaper.Ten}** mềm hơn ({cheaper.Gia:N0} VNĐ).");
            }
            else
            {
                lines.Add($"- Về giá: hai mẫu đang ngang nhau ({first.Gia:N0} VNĐ).");
            }

            // 2. Cảm nhận thực tế (QUAN TRỌNG NHẤT)
            lines.AddRange(BuildRealWorldComparison(first, second));

            // 3. KẾT LUẬN (điểm ăn tiền)
            lines.Add(""); 
            lines.Add(BuildFinalSuggestion(first, second, intent, profile, normalizedMessage));

            return string.Join("\n", lines).Trim();
        }
        private static List<string> BuildRealWorldComparison(ProductSummaryDto a, ProductSummaryDto b)
        {
            var result = new List<string>();

            bool aLight = ContainsAny(a.Ten, "Vision", "Janus", "Latte");
            bool bLight = ContainsAny(b.Ten, "Vision", "Janus", "Latte");

            bool aStrong = ContainsAny(a.Ten, "Air Blade", "Freego", "Winner");
            bool bStrong = ContainsAny(b.Ten, "Air Blade", "Freego", "Winner");

            if (aLight && bStrong)
            {
                result.Add($"- **{a.Ten}**: nhẹ, dễ chạy trong phố, hợp đi hằng ngày.");
                result.Add($"- **{b.Ten}**: đầm hơn, máy khỏe hơn, chạy sướng hơn nếu đi xa.");
                return result;
            }

            if (bLight && aStrong)
            {
                result.Add($"- **{b.Ten}**: nhẹ, dễ chạy trong phố.");
                result.Add($"- **{a.Ten}**: đầm hơn, máy khỏe hơn.");
                return result;
            }

            // fallback
            result.Add($"- **{a.Ten}**: đi ổn, phù hợp nhu cầu cơ bản.");
            result.Add($"- **{b.Ten}**: cũng ổn, tùy gu và cách sử dụng.");

            return result;
        }
        private static string BuildFinalSuggestion(
     ProductSummaryDto a,
     ProductSummaryDto b,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage)
        {
            var text = NormalizeText(normalizedMessage);

            bool wantsFemale =
                intent.PrefersFemaleStyle ||
                profile.PrefersFemaleStyle ||
                ContainsAny(text, "cho nu", "xe nu", "hop nu", "nu tinh");

            bool wantsMale =
                intent.PrefersMaleStyle ||
                profile.PrefersMaleStyle ||
                ContainsAny(text, "cho nam", "xe nam", "hop nam", "nam tinh");

            bool forWork =
                intent.ForWork || profile.ForWork || ContainsAny(text, "di lam", "di pho");

            bool forSchool =
                intent.ForSchool || profile.ForSchool || ContainsAny(text, "di hoc", "sinh vien", "hoc sinh");

            bool wantsFuelSaving =
                intent.WantsFuelSaving || profile.WantsFuelSaving || ContainsAny(text, "tiet kiem xang", "it hao xang");

            bool wantsEasy =
                intent.WantsEasyControl || profile.WantsEasyControl || profile.NeedsLowSeat ||
                ContainsAny(text, "de di", "de chong chan", "yen thap", "thap nguoi");

            bool wantsStorage =
                intent.WantsLargeStorage || profile.WantsLargeStorage || ContainsAny(text, "cop rong", "dung do", "nhieu do");

            var preferred = PickBetterByProfile(a, b, wantsFemale, wantsMale, forWork, forSchool, wantsFuelSaving, wantsEasy, wantsStorage);

            if (wantsFemale)
                return $"Nếu chọn theo hướng **dễ đi và hợp nữ hơn**, mình nghiêng về **{preferred.Ten}**.";

            if (wantsMale)
                return $"Nếu chọn theo hướng **chắc xe và nam tính hơn**, mình nghiêng về **{preferred.Ten}**.";

            if (forWork)
                return $"Nếu dùng để **đi làm hằng ngày**, mình nghiêng về **{preferred.Ten}** vì dễ dùng và thực tế hơn.";

            if (forSchool)
                return $"Nếu dùng để **đi học / sinh viên**, mình nghiêng về **{preferred.Ten}** vì dễ đi và chi phí dùng lâu dài dễ chịu hơn.";

            if (wantsFuelSaving)
                return $"Nếu ưu tiên **tiết kiệm xăng**, mình nghiêng về **{preferred.Ten}**.";

            if (wantsEasy)
                return $"Nếu ưu tiên **dễ điều khiển / dễ chống chân**, mình nghiêng về **{preferred.Ten}**.";

            if (wantsStorage)
                return $"Nếu ưu tiên **cốp rộng và tiện mang đồ**, mình nghiêng về **{preferred.Ten}**.";

            var cheaper = a.Gia <= b.Gia ? a : b;
            var higher = a.Gia <= b.Gia ? b : a;

            return $"Nếu bạn cần **tiết kiệm và dễ đi hằng ngày** thì nên chọn **{cheaper.Ten}**.\n" +
                   $"Nếu bạn muốn **xe khỏe và đầm hơn** thì **{higher.Ten}** sẽ hợp hơn.";
        }
        private static ProductSummaryDto PickBetterByProfile(
    ProductSummaryDto a,
    ProductSummaryDto b,
    bool wantsFemale,
    bool wantsMale,
    bool forWork,
    bool forSchool,
    bool wantsFuelSaving,
    bool wantsEasy,
    bool wantsStorage)
        {
            var scoreA = ScoreProductForProfile(a, wantsFemale, wantsMale, forWork, forSchool, wantsFuelSaving, wantsEasy, wantsStorage);
            var scoreB = ScoreProductForProfile(b, wantsFemale, wantsMale, forWork, forSchool, wantsFuelSaving, wantsEasy, wantsStorage);

            if (scoreA == scoreB)
                return a.Gia <= b.Gia ? a : b;

            return scoreA > scoreB ? a : b;
        }
        private static int ScoreProductForProfile(
    ProductSummaryDto p,
    bool wantsFemale,
    bool wantsMale,
    bool forWork,
    bool forSchool,
    bool wantsFuelSaving,
    bool wantsEasy,
    bool wantsStorage)
        {
            var name = p.Ten ?? string.Empty;
            var category = NormalizeText(p.Loai ?? string.Empty);

            int score = 0;

            if (wantsFemale)
            {
                if (ContainsAny(name, "Vision", "Latte", "Grande", "Janus", "Zip", "Attila", "Shark")) score += 35;
                if (ContainsAny(name, "Winner", "Exciter", "Raider", "Sonic", "CBR", "Rebel")) score -= 40;
                if (category.Contains("ga")) score += 12;
            }

            if (wantsMale)
            {
                if (ContainsAny(name, "Air Blade", "Winner", "Future", "Exciter", "PCX", "SH")) score += 28;
                if (ContainsAny(name, "Latte", "Grande", "Zip")) score -= 10;
            }

            if (forWork)
            {
                if (ContainsAny(name, "Air Blade", "Vision", "Future", "Freego", "Lead", "Wave")) score += 28;
                if (ContainsAny(name, "SH", "PCX", "CBR", "Rebel")) score -= 12;
            }

            if (forSchool)
            {
                if (ContainsAny(name, "Vision", "Wave", "Sirius", "Janus", "Zip")) score += 30;
                if (p.Gia <= 35_000_000m) score += 12;
                if (p.Gia >= 50_000_000m) score -= 20;
            }

            if (wantsFuelSaving)
            {
                if (ContainsAny(name, "Vision", "Wave", "Sirius", "Future", "Janus")) score += 28;
            }

            if (wantsEasy)
            {
                if (ContainsAny(name, "Vision", "Janus", "Latte", "Zip", "Attila", "Shark")) score += 30;
                if (ContainsAny(name, "Winner", "Exciter", "Raider", "SH", "PCX")) score -= 16;
            }

            if (wantsStorage)
            {
                if (ContainsAny(name, "Lead", "Freego", "Air Blade", "Latte", "Grande")) score += 26;
                if (category.Contains("ga")) score += 8;
            }

            if (p.Gia <= 35_000_000m)
                score += 6;

            return score;
        }
        private static string? InferFeatureFromMessageOnly(string message)
        {
            var text = NormalizeText(message);
            if (text.Contains("dep") || text.Contains("thanh lich") || text.Contains("mem mai"))
                return "design_fit";
            return null;
        }

        private static string BuildPriceCompareReply(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (first.Gia == second.Gia)
                return $"Về **giá** thì **{first.Ten}** và **{second.Ten}** hiện đang ngang nhau, cùng ở mức {first.Gia:N0} VNĐ.";

            var cheaper = first.Gia < second.Gia ? first : second;
            var higher = first.Gia < second.Gia ? second : first;
            return $"Nếu xét riêng về **giá** thì **{cheaper.Ten}** mềm hơn với mức {cheaper.Gia:N0} VNĐ; còn **{higher.Ten}** đang ở mức {higher.Gia:N0} VNĐ.";
        }

        private static string BuildStorageVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Freego", "Latte", "Lead", "Address", "Air Blade") &&
                !ContainsAny(second.Ten, "Freego", "Latte", "Lead", "Address", "Air Blade"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{first.Ten}** nhỉnh hơn, nên tiện mang đồ hơn."
                    : $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{first.Ten}**.";
            }

            if (ContainsAny(second.Ten, "Freego", "Latte", "Lead", "Address", "Air Blade") &&
                !ContainsAny(first.Ten, "Freego", "Latte", "Lead", "Address", "Air Blade"))
            {
                return isFollowUpCompare
                    ? $"Nếu xét riêng về **cốp rộng** thì **{second.Ten}** nhỉnh hơn, nên tiện mang đồ hơn."
                    : $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{second.Ten}**.";
            }

            return isFollowUpCompare
                ? "Nếu xét riêng về **cốp rộng** thì mình sẽ nghiêng về mẫu thiên về xe ga hoặc tính tiện dụng hơn trong hai xe này."
                : "Nếu xét theo tiêu chí **cốp rộng** thì mình sẽ nghiêng về mẫu thiên về xe ga hoặc tính tiện dụng hơn trong hai xe này.";
        }

        private static string BuildFuelSavingVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Vision", "Wave", "Sirius", "Future") && !ContainsAny(second.Ten, "Vision", "Wave", "Sirius", "Future"))
                return isFollowUpCompare ? $"Nếu xét riêng về **tiết kiệm xăng** thì **{first.Ten}** nhỉnh hơn để cân nhắc." : $"Nếu bạn ưu tiên **tiết kiệm xăng** thì **{first.Ten}** nhỉnh hơn để cân nhắc.";
            if (ContainsAny(second.Ten, "Vision", "Wave", "Sirius", "Future") && !ContainsAny(first.Ten, "Vision", "Wave", "Sirius", "Future"))
                return isFollowUpCompare ? $"Nếu xét riêng về **tiết kiệm xăng** thì **{second.Ten}** nhỉnh hơn để cân nhắc." : $"Nếu bạn ưu tiên **tiết kiệm xăng** thì **{second.Ten}** nhỉnh hơn để cân nhắc.";
            return isFollowUpCompare ? "Nếu xét riêng về **tiết kiệm xăng** thì hai mẫu này khá gần nhau, nhưng mình sẽ nghiêng nhẹ về mẫu thực dụng hơn." : "Nếu xét riêng về **tiết kiệm xăng** thì hai mẫu này khá gần nhau, nhưng mình sẽ nghiêng nhẹ về mẫu thực dụng hơn.";
        }

        private static string BuildLowSeatVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Vision", "Janus", "Latte", "Zip") && !ContainsAny(second.Ten, "Vision", "Janus", "Latte", "Zip"))
                return isFollowUpCompare ? $"Nếu ưu tiên **dễ chống chân / dễ kiểm soát** thì **{first.Ten}** hợp hơn." : $"Nếu bạn ưu tiên **dễ chống chân / dễ kiểm soát** thì **{first.Ten}** hợp hơn.";
            if (ContainsAny(second.Ten, "Vision", "Janus", "Latte", "Zip") && !ContainsAny(first.Ten, "Vision", "Janus", "Latte", "Zip"))
                return isFollowUpCompare ? $"Nếu ưu tiên **dễ chống chân / dễ kiểm soát** thì **{second.Ten}** hợp hơn." : $"Nếu bạn ưu tiên **dễ chống chân / dễ kiểm soát** thì **{second.Ten}** hợp hơn.";
            return isFollowUpCompare ? "Nếu xét theo hướng **dễ chống chân** thì mình sẽ nghiêng về mẫu gọn và dễ làm quen hơn." : "Nếu xét theo hướng **dễ chống chân** thì mình sẽ nghiêng về mẫu gọn và dễ làm quen hơn.";
        }

        private static string BuildFemaleVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Latte", "Grande", "Janus", "Zip", "Vision") && !ContainsAny(second.Ten, "Latte", "Grande", "Janus", "Zip", "Vision"))
                return isFollowUpCompare ? $"Nếu xét theo hướng **hợp nữ / nữ tính hơn** thì **{first.Ten}** nhỉnh hơn." : $"Nếu xét theo hướng **hợp nữ / nữ tính hơn** thì mình nghiêng hơn về **{first.Ten}**.";
            if (ContainsAny(second.Ten, "Latte", "Grande", "Janus", "Zip", "Vision") && !ContainsAny(first.Ten, "Latte", "Grande", "Janus", "Zip", "Vision"))
                return isFollowUpCompare ? $"Nếu xét theo hướng **hợp nữ / nữ tính hơn** thì **{second.Ten}** nhỉnh hơn." : $"Nếu xét theo hướng **hợp nữ / nữ tính hơn** thì mình nghiêng hơn về **{second.Ten}**.";
            return isFollowUpCompare ? "Nếu chỉ chốt nhanh theo hướng **hợp nữ** thì mình nghiêng về mẫu có dáng mềm và dễ làm quen hơn." : "Nếu chỉ chốt nhanh theo hướng **hợp nữ** thì mình nghiêng hơn về mẫu có dáng mềm và dễ làm quen hơn trong hai xe này.";
        }

        private static string BuildDesignVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstElegant = ContainsAny(first.Ten, "Latte", "Grande", "Zip", "Attila", "Janus");
            bool secondElegant = ContainsAny(second.Ten, "Latte", "Grande", "Zip", "Attila", "Janus");
            bool firstNeutralPractical = ContainsAny(first.Ten, "Vision", "Air Blade", "Future", "Freego");
            bool secondNeutralPractical = ContainsAny(second.Ten, "Vision", "Air Blade", "Future", "Freego");

            if (firstElegant && !secondElegant)
                return isFollowUpCompare ? $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{first.Ten}**." : $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{first.Ten}**, vì mẫu này thiên về dáng mềm và cảm giác thanh lịch hơn.";
            if (secondElegant && !firstElegant)
                return isFollowUpCompare ? $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{second.Ten}**." : $"Nếu xét riêng về **kiểu dáng / độ thanh lịch** thì mình nghiêng hơn về **{second.Ten}**, vì mẫu này thiên về dáng mềm và cảm giác thanh lịch hơn.";
            if (firstNeutralPractical && !secondNeutralPractical)
                return isFollowUpCompare ? $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{first.Ten}** dễ hợp hơn." : $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{first.Ten}** dễ hợp hơn.";
            if (secondNeutralPractical && !firstNeutralPractical)
                return isFollowUpCompare ? $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{second.Ten}** dễ hợp hơn." : $"Nếu xét theo hướng **đẹp kiểu gọn gàng, trung tính** thì **{second.Ten}** dễ hợp hơn.";
            return isFollowUpCompare ? "Nếu xét riêng về **kiểu dáng** thì hai mẫu này khá gần nhau, chỉ khác ở việc một mẫu thiên thanh lịch hơn còn mẫu kia thiên thực dụng hơn." : "Nếu xét riêng về **kiểu dáng** thì hai mẫu này khá gần nhau, và thường sẽ khác nhau ở gu: một bên thiên thanh lịch hơn, một bên thiên thực dụng hơn.";
        }

        private static string BuildWorkVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Air Blade", "Freego", "Future", "Vision") && !ContainsAny(second.Ten, "Air Blade", "Freego", "Future", "Vision"))
                return isFollowUpCompare ? $"Nếu xét theo hướng **đi làm** thì **{first.Ten}** hợp hơn." : $"Nếu ưu tiên **đi làm hằng ngày** thì **{first.Ten}** hợp hơn.";
            if (ContainsAny(second.Ten, "Air Blade", "Freego", "Future", "Vision") && !ContainsAny(first.Ten, "Air Blade", "Freego", "Future", "Vision"))
                return isFollowUpCompare ? $"Nếu xét theo hướng **đi làm** thì **{second.Ten}** hợp hơn." : $"Nếu ưu tiên **đi làm hằng ngày** thì **{second.Ten}** hợp hơn.";
            return isFollowUpCompare ? "Nếu xét theo hướng **đi làm** thì mình sẽ nghiêng về mẫu thực dụng và ổn định hơn." : "Nếu xét theo hướng **đi làm**, mình sẽ nghiêng về mẫu thực dụng và ổn định hơn trong hai xe này.";
        }

        private static string BuildSchoolVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (ContainsAny(first.Ten, "Vision", "Wave", "Sirius") && !ContainsAny(second.Ten, "Vision", "Wave", "Sirius"))
                return isFollowUpCompare ? $"Nếu xét theo hướng **đi học / sinh viên** thì **{first.Ten}** dễ cân nhắc hơn." : $"Nếu ưu tiên **đi học / sinh viên** thì **{first.Ten}** dễ cân nhắc hơn.";
            if (ContainsAny(second.Ten, "Vision", "Wave", "Sirius") && !ContainsAny(first.Ten, "Vision", "Wave", "Sirius"))
                return isFollowUpCompare ? $"Nếu xét theo hướng **đi học / sinh viên** thì **{second.Ten}** dễ cân nhắc hơn." : $"Nếu ưu tiên **đi học / sinh viên** thì **{second.Ten}** dễ cân nhắc hơn.";
            return isFollowUpCompare ? "Nếu xét theo hướng **đi học** thì mình sẽ ưu tiên mẫu dễ đi và chi phí dùng lâu dài hơn." : "Nếu xét theo hướng **đi học**, mình sẽ ưu tiên mẫu dễ đi và chi phí dùng lâu dài hơn.";
        }

        private static string BuildGeneralVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            if (first.Gia < second.Gia)
                return isFollowUpCompare ? $"Nếu ưu tiên **giá mềm hơn** thì **{first.Ten}** lợi thế hơn." : $"Nếu bạn ưu tiên **giá mềm hơn** thì **{first.Ten}** lợi thế hơn; còn nếu muốn cân nhắc theo cảm giác xe hoặc tiện ích thì mình có thể lọc tiếp cho bạn.";
            if (second.Gia < first.Gia)
                return isFollowUpCompare ? $"Nếu ưu tiên **giá mềm hơn** thì **{second.Ten}** lợi thế hơn." : $"Nếu bạn ưu tiên **giá mềm hơn** thì **{second.Ten}** lợi thế hơn; còn nếu muốn cân nhắc theo cảm giác xe hoặc tiện ích thì mình có thể lọc tiếp cho bạn.";
            return isFollowUpCompare ? "Hai mẫu này đang khá ngang nhau ở dữ liệu cơ bản." : "Hai mẫu này đang khá ngang nhau ở dữ liệu cơ bản, nên có thể chốt tiếp theo tiêu chí như cốp rộng, dễ chống chân, đi làm hay tiết kiệm xăng.";
        }

        private static string? ExtractShortHintForFollowUp(string? ragContext, string feature)
        {
            if (string.IsNullOrWhiteSpace(ragContext))
                return null;
            if (feature == "low_seat") return "Nếu bạn thấp người hoặc hay dừng đèn đỏ nhiều thì mẫu gọn và dễ kiểm soát sẽ lợi thế hơn.";
            if (feature == "storage") return "Nếu bạn hay mang đồ hoặc đi làm hằng ngày thì tiêu chí cốp sẽ đáng ưu tiên hơn.";
            if (feature == "female_fit") return "Nếu bạn thích dáng mềm và thiên nữ tính hơn thì nên ưu tiên mẫu có kiểu dáng thanh lịch hơn.";
            return null;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
    }
}
