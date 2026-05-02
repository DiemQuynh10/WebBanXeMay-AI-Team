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
            if (LooksLikePickOneRequest(normalizedMessage))
            {
                return null;
            }
            bool wantsCompareAll = LooksLikeCompareAllRequest(normalizedMessage);
            if (LooksLikeAlternativeRecommendationRequest(normalizedMessage) &&
     !LooksLikeComparePriceFollowUp(normalizedMessage, intent, profile))
            {
                return null;
            }
            var targetNames = ResolveComparisonTargets(intent, profile, normalizedMessage);

            _logger.LogWarning(
    "[COMPARE DEBUG] Message={Message} | Feature={Feature} | Targets={Targets} | LastRecommended={LastRecommended} | LastCompared={LastCompared}",
    normalizedMessage,
    DetectFeature(normalizedMessage),
    string.Join(", ", targetNames),
    profile?.LastRecommendedProducts == null ? "(null)" : string.Join(", ", profile.LastRecommendedProducts),
    profile?.LastComparedProducts == null ? "(null)" : string.Join(", ", profile.LastComparedProducts));

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

            var resolvedFeatureForMulti =
     ResolveComparisonFeature(intent, normalizedMessage)
     ?? DetectFeature(normalizedMessage)
     ?? InferFeatureFromMessageOnly(normalizedMessage);

            if (matchedProducts.Count >= 3 &&
                !string.IsNullOrWhiteSpace(resolvedFeatureForMulti))
            {
                var ranked = matchedProducts
                    .Select(p => ScoreForCompare(p, resolvedFeatureForMulti, intent, profile, normalizedMessage))
                    .OrderByDescending(x => x.Score)
                    .ToList();
                profile.LastComparisonFeature = resolvedFeatureForMulti;
                intent.ComparisonFeature = resolvedFeatureForMulti;
                await _conversationPreferenceService.SetComparedProductsAsync(
                    conversationId,
                    ranked.Select(x => x.Product.Ten!).ToList());

                var winner = ranked.First();

                var lines = new List<string>
    {
        $"Mình xét nhanh {matchedProducts.Count} mẫu vừa tư vấn theo tiêu chí **{BuildFeatureLabel(resolvedFeatureForMulti)}**:",
        ""
    };

                foreach (var item in ranked)
                {
                    lines.Add($"- **{item.Product.Ten}**: {item.Score:0.0}/10" +
                              (item.Reasons.Any() ? $" — {string.Join(", ", item.Reasons)}." : "."));
                }

                lines.Add("");
                lines.Add($"→ Nếu chọn theo tiêu chí này, mình nghiêng về **{winner.Product.Ten}** nhất.");

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = string.Join("\n", lines),
                    Products = ranked.Select(x => ChatProductCardMapper.Map(x.Product)).ToList()
                };
            }

            var first = matchedProducts[0];
            var second = matchedProducts[1];
            var resolvedFeatureEarly = ResolveComparisonFeature(intent, normalizedMessage)
                           ?? DetectFeature(normalizedMessage)
                           ?? InferFeatureFromMessageOnly(normalizedMessage);

            if (LooksLikeRemoveWeakRequest(normalizedMessage))
            {
                var stronger = PickStrongerProduct(first, second);
                var weaker = stronger.Id == first.Id ? second : first;

                await _conversationPreferenceService.SetComparedProductsAsync(
                    conversationId,
                    new List<string> { stronger.Ten! });

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply =
                        $"Mình sẽ **loại {weaker.Ten}** vì mẫu này thiên về đi phố nhẹ nhàng hơn, không nổi bật bằng **{stronger.Ten}** về độ khỏe/máy bốc.\n\n" +
                        $"Nếu giữ lại 1 mẫu trong nhóm này thì mình nghiêng về **{stronger.Ten}**.",
                    Products = new List<ChatProductCard>
        {
            ChatProductCardMapper.Map(stronger)
        }
                };
            }

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

            var resolvedFeature = resolvedFeatureEarly;

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
            if (!string.IsNullOrWhiteSpace(resolvedFeature))
            {
                intent.ComparisonFeature = resolvedFeature;
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
    isFollowUpCompare && !shouldForceFullCompareReply,
    resolvedFeature);

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

            bool hasCompareSignal =
                text.Contains("so sanh") ||
                text.Contains("uu nhuoc") ||
                text.Contains("khac nhau") ||
                text.Contains("tot hon");

            bool hasAllSignal =
                text.Contains("tat ca") ||
                text.Contains("het") ||
                text.Contains("cac mau tren") ||
                text.Contains("cac mau xe tren") ||
                text.Contains("cac xe tren") ||
                text.Contains("nhung mau tren") ||
                text.Contains("may mau tren") ||
                text.Contains("ben tren") ||
                text.Contains("vua dua") ||
                text.Contains("vua goi y") ||
                text.Contains("vua roi");

            return hasCompareSignal && hasAllSignal;
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
            bool explicitProductMention = HasExplicitProductNameInMessage(message);
            bool featureFollowUp = DetectFeature(message) != null;

            if (!wantsCompareAll &&
                featureFollowUp &&
                !explicitProductMention &&
                profile?.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count >= 2)
            {
                return profile.LastRecommendedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeProductCandidate)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToList();
            }

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

            if (wantsCompareAll)
            {
                return distinct.Take(6).ToList();
            }


            if (featureFollowUp && !HasExplicitProductNameInMessage(message))
            {
                return distinct.Take(5).ToList();
            }

            return distinct.Take(2).ToList();
        }

        private static CompareQuestionKind DetectCompareQuestionKind(string message, ParsedIntent intent)
        {
            var text = NormalizeText(message);

            bool isPriceQuestion =
     text.Contains("gia") ||
     text.Contains("bao nhieu") ||
     text.Contains("muc gia") ||
     text.Contains("re hon") ||
     text.Contains("mem hon") ||
     text.Contains("gia thap hon") ||
     text.Contains("xe nao re hon") ||
     text.Contains("mau nao re hon") ||
     text.Contains("con nao re hon");

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
            if (text.Contains("boc") ||
    text.Contains("may khoe") ||
    text.Contains("manh hon") ||
    text.Contains("khoe hon") ||
    text.Contains("tang toc"))
            {
                return "power";
            }
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
        private sealed class CompareScoreResult
        {
            public ProductSummaryDto Product { get; set; } = default!;
            public double Score { get; set; }
            public List<string> Reasons { get; set; } = new();
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

            lines.Add("Mình so sánh nhanh các mẫu bên trên để bạn dễ chọn:");
            lines.Add("");

            var easyRide = products
                .Where(x => ContainsAny(x.Ten, "Vision", "Janus", "Latte", "Attila", "Shark", "Zip"))
                .ToList();

            var balanced = products
                .Where(x => ContainsAny(x.Ten, "Freego", "Lead", "Impulse", "Address"))
                .ToList();

            var strong = products
                .Where(x => ContainsAny(x.Ten, "Air Blade", "Winner", "Exciter", "Raider", "Vario", "PCX", "SH"))
                .ToList();

            var groupedIds = easyRide
                .Concat(balanced)
                .Concat(strong)
                .Select(x => x.Id)
                .ToHashSet();

            var others = products
                .Where(x => !groupedIds.Contains(x.Id))
                .ToList();

            if (easyRide.Any())
                lines.Add($"- Nhóm **dễ đi, nhẹ, hợp đi phố**: {string.Join(", ", easyRide.Select(x => x.Ten))}.");

            if (balanced.Any())
                lines.Add($"- Nhóm **cân bằng, tiện dụng hằng ngày**: {string.Join(", ", balanced.Select(x => x.Ten))}.");

            if (strong.Any())
                lines.Add($"- Nhóm **đầm hơn, máy khỏe hơn**: {string.Join(", ", strong.Select(x => x.Ten))}.");

            if (others.Any())
                lines.Add($"- Nhóm **trung tính / cần cân nhắc thêm**: {string.Join(", ", others.Select(x => x.Ten))}.");

            lines.Add("");
            lines.Add("Ưu nhược điểm nhanh từng mẫu:");

            foreach (var p in products)
            {
                lines.Add($"- **{p.Ten}** ({p.Gia:N0} VNĐ): {BuildRealProsCons(p)}");
            }

            lines.Add("");
            lines.Add(BuildFinalMultiSuggestion(products));

            return string.Join("\n", lines).Trim();
        }
        private static string BuildFinalMultiSuggestion(List<ProductSummaryDto> products)
        {
            var cheapest = products.OrderBy(x => x.Gia).FirstOrDefault();

            var bestDaily =
                products.FirstOrDefault(x => ContainsAny(x.Ten, "Vision")) ??
                products.FirstOrDefault(x => ContainsAny(x.Ten, "Freego")) ??
                products.FirstOrDefault(x => ContainsAny(x.Ten, "Latte")) ??
                cheapest;

            var strongest =
                products.FirstOrDefault(x => ContainsAny(x.Ten, "Air Blade")) ??
                products.FirstOrDefault(x => ContainsAny(x.Ten, "Winner", "Exciter", "Raider"));

            var lines = new List<string>();

            if (cheapest != null && bestDaily != null && cheapest.Id == bestDaily.Id)
            {
                lines.Add($"Nếu muốn **tiết kiệm và dễ dùng hằng ngày**, mình nghiêng về **{cheapest.Ten}**.");
            }
            else
            {
                if (cheapest != null)
                    lines.Add($"Nếu muốn **rẻ nhất**, bạn nên xem **{cheapest.Ten}**.");

                if (bestDaily != null)
                    lines.Add($"Nếu muốn **dễ đi, ổn định lâu dài**, mình nghiêng về **{bestDaily.Ten}**.");
            }

            if (strongest != null && bestDaily != null && strongest.Id != bestDaily.Id)
            {
                lines.Add($"Nếu muốn **xe khỏe và đầm hơn**, **{strongest.Ten}** sẽ hợp hơn.");
            }

            lines.Add("Nếu bạn nói thêm nhu cầu chính như đi làm, đi học, chở đồ hay chiều cao, mình có thể chốt giúp 1 mẫu phù hợp nhất.");

            return string.Join("\n", lines);
        }
        private static string BuildRealProsCons(ProductSummaryDto p)
        {
            var name = p.Ten ?? string.Empty;

            if (ContainsAny(name, "Vision"))
                return "ưu điểm là nhẹ, dễ đi, tiết kiệm xăng, hợp đi phố; nhược điểm là máy không mạnh bằng nhóm Air Blade.";

            if (ContainsAny(name, "Latte"))
                return "ưu điểm là dáng thanh lịch, dễ điều khiển, hợp đi hằng ngày; nhược điểm là giá nhỉnh hơn một chút so với vài mẫu phổ thông.";

            if (ContainsAny(name, "Freego"))
                return "ưu điểm là cân bằng, tiện dụng, giá mềm hơn nhóm 40 triệu; nhược điểm là kiểu dáng không thanh lịch bằng Latte/Vision.";

            if (ContainsAny(name, "Air Blade"))
                return "ưu điểm là máy khỏe, đi đầm, hợp người thích cảm giác chắc xe; nhược điểm là giá cao hơn và xe không gọn bằng Vision/Latte.";

            if (ContainsAny(name, "Impulse", "Address"))
                return "ưu điểm là giá mềm, xe ga dễ dùng; nhược điểm là thương hiệu và độ phổ biến không mạnh bằng Honda/Yamaha.";

            if (ContainsAny(name, "Attila", "Shark"))
                return "ưu điểm là giá dễ tiếp cận, đi phố ổn; nhược điểm là thương hiệu không mạnh bằng Honda/Yamaha.";

            if (ContainsAny(name, "Future"))
                return "ưu điểm là bền, thực dụng, tiết kiệm; nhược điểm là không tiện bằng xe ga.";

            if (ContainsAny(name, "Wave", "Sirius", "Jupiter"))
                return "ưu điểm là giá mềm, dễ nuôi; nhược điểm là tiện ích và kiểu dáng không bằng xe ga.";

            if (ContainsAny(name, "Winner", "Exciter", "Raider"))
                return "ưu điểm là khỏe, thể thao; nhược điểm là không hợp nếu bạn ưu tiên xe nhẹ, dễ đi.";

            return "ưu điểm là có mức giá đáng cân nhắc; nhược điểm là cần đối chiếu thêm theo nhu cầu cụ thể.";
        }
        private static string BuildShortProsCons(ProductSummaryDto product)
        {
            var name = product.Ten ?? string.Empty;

            if (ContainsAny(name, "Vision"))
                return "ưu điểm là nhẹ, dễ điều khiển, hợp nữ và đi phố; nhược điểm là máy không mạnh, đi xa nhiều sẽ không đầm bằng Air Blade.";

            if (ContainsAny(name, "Freego"))
                return "ưu điểm là giá mềm, cốp tiện và dùng hằng ngày khá thực dụng; nhược điểm là cảm giác xe không đầm và mạnh bằng Air Blade.";

            if (ContainsAny(name, "Air Blade"))
                return "ưu điểm là máy khỏe hơn, chạy đầm và hợp đi xa hơn; nhược điểm là giá cao hơn, xe to và nặng hơn nhóm gọn nhẹ.";

            if (ContainsAny(name, "Lead"))
                return "ưu điểm là cốp rộng, tiện chở đồ và đi làm; nhược điểm là thân xe khá to, không linh hoạt bằng Vision.";

            if (ContainsAny(name, "Latte", "Grande"))
                return "ưu điểm là dáng thanh lịch, hợp nữ và đi phố; nhược điểm là giá nhỉnh hơn vài mẫu phổ thông.";

            if (ContainsAny(name, "Janus", "Zip"))
                return "ưu điểm là nhỏ gọn, dễ xoay xở trong phố; nhược điểm là tiện ích và độ đầm xe ở mức vừa phải.";

            if (ContainsAny(name, "Attila", "Shark"))
                return "ưu điểm là giá dễ tiếp cận, dáng mềm và dễ đi; nhược điểm là thương hiệu không phổ biến bằng Honda/Yamaha.";

            if (ContainsAny(name, "Impulse"))
                return "ưu điểm là giá mềm, xe ga thực dụng; nhược điểm là độ phổ biến và nhận diện thương hiệu chưa mạnh.";

            if (ContainsAny(name, "Address"))
                return "ưu điểm là nhỏ gọn, tiết kiệm và dễ dùng; nhược điểm là tiện ích không nổi bật bằng nhóm xe ga phổ biến.";

            if (ContainsAny(name, "Future"))
                return "ưu điểm là bền, tiết kiệm xăng và hợp đi làm; nhược điểm là không tiện bằng xe ga vì không có cốp rộng.";

            if (ContainsAny(name, "Wave"))
                return "ưu điểm là rẻ, bền và tiết kiệm; nhược điểm là thiết kế và tiện ích khá cơ bản.";

            if (ContainsAny(name, "Sirius", "Jupiter"))
                return "ưu điểm là xe số tiết kiệm, dễ bảo dưỡng; nhược điểm là không tiện và không nữ tính bằng xe ga.";

            if (ContainsAny(name, "Winner", "Exciter", "Raider"))
                return "ưu điểm là máy khỏe, dáng thể thao; nhược điểm là không hợp nếu ưu tiên xe nhẹ, dễ đi hoặc nữ tính.";

            if (ContainsAny(name, "SH", "PCX", "Vario"))
                return "ưu điểm là đầm xe, hiện đại và có cảm giác cao cấp hơn; nhược điểm là giá cao và xe khá to.";

            return "ưu điểm là có mức giá đáng cân nhắc; nhược điểm là cần đối chiếu thêm theo nhu cầu cụ thể.";
        }
        private static string BuildDeterministicCompareReply(
      ProductSummaryDto first,
      ProductSummaryDto second,
      ParsedIntent intent,
      CustomerPreferenceProfile profile,
      string normalizedMessage,
      string? ragContext,
      bool isFollowUpCompare,
      string? resolvedFeature)
        {
            var detectedFeature = resolvedFeature ?? DetectFeature(normalizedMessage);
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

            lines.AddRange(BuildRealWorldComparison(first, second));

            if (!string.IsNullOrWhiteSpace(detectedFeature))
            {
                lines.Add("");
                lines.Add(BuildFeatureVerdict(first, second, detectedFeature, isFollowUpCompare));
            }

            lines.Add("");
            lines.Add(BuildCompareScoreText(first, second, detectedFeature, intent, profile, normalizedMessage));
            return string.Join("\n", lines).Trim();
        }
        private static List<string> BuildRealWorldComparison(ProductSummaryDto a, ProductSummaryDto b)
        {
            return new List<string>
    {
        $"- **{a.Ten}**: {BuildShortProsCons(a)}",
        $"- **{b.Ten}**: {BuildShortProsCons(b)}"
    };
        }
        private static CompareScoreResult ScoreForCompare(
    ProductSummaryDto product,
    string? feature,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string message)
        {
            double score = 5.0;
            var reasons = new List<string>();
            var text = NormalizeText(message);

            bool wantsFemale = feature == "female_fit" ||
                               intent.PrefersFemaleStyle ||
                               profile.PrefersFemaleStyle ||
                               text.Contains("hop nu") ||
                               text.Contains("cho nu");

            bool wantsStorage = feature == "storage";
            bool wantsFuel = feature == "fuel_saving";
            bool wantsEasy = feature == "low_seat";
            bool wantsPower = feature == "power";
            bool wantsCity = feature == "city_fit";
            bool wantsDistance = feature == "distance_fit";
            bool wantsDurability = feature == "durability";
            bool wantsValue = feature == "value";

            if (wantsFemale)
            {
                if (ContainsAny(product.Ten, "Vision", "Latte", "Grande", "Janus", "Zip", "Attila", "Shark"))
                {
                    score += 2.2;
                    reasons.Add("dáng xe gọn, dễ hợp nữ");
                }

                if (ContainsAny(product.Ten, "Winner", "Exciter", "Raider"))
                {
                    score -= 2.0;
                    reasons.Add("kiểu xe khá thể thao, không ưu tiên cho nữ phổ thông");
                }
            }

            if (wantsStorage)
            {
                if (ContainsAny(product.Ten, "Freego", "Lead", "Latte", "Grande", "Air Blade"))
                {
                    score += 2.0;
                    reasons.Add("tiện hơn nếu ưu tiên cốp rộng");
                }
                else
                {
                    score -= 0.8;
                    reasons.Add("không nổi bật về cốp");
                }
            }

            if (wantsFuel)
            {
                if (ContainsAny(product.Ten, "Vision", "Wave", "Future", "Sirius", "Janus"))
                {
                    score += 2.0;
                    reasons.Add("phù hợp nếu ưu tiên tiết kiệm xăng");
                }
            }

            if (wantsEasy)
            {
                if (ContainsAny(product.Ten, "Vision", "Janus", "Latte", "Zip", "Attila", "Shark"))
                {
                    score += 1.8;
                    reasons.Add("dễ điều khiển, dễ làm quen");
                }
            }

            if (wantsPower)
            {
                if (ContainsAny(product.Ten, "Air Blade", "Winner", "Exciter", "Raider", "PCX", "SH"))
                {
                    score += 2.0;
                    reasons.Add("máy khỏe và cảm giác đầm hơn");
                }
                else if (ContainsAny(product.Ten, "PCX", "SH"))
                    score += 1.5;
            }

            if (wantsCity)
            {
                if (ContainsAny(product.Ten, "Vision", "Janus", "Latte", "Zip", "Attila", "Shark", "Freego"))
                {
                    score += 1.6;
                    reasons.Add("gọn và hợp đi phố");
                }
            }

            if (wantsDistance)
            {
                if (ContainsAny(product.Ten, "Air Blade", "Winner", "Exciter", "PCX", "SH"))
                {
                    score += 2.2;
                    reasons.Add("máy khỏe và cảm giác xe đầm hơn khi đi xa");
                }
                else if (ContainsAny(product.Ten, "Future", "Freego"))
                {
                    score += 1.2;
                    reasons.Add("vẫn dùng ổn nếu thỉnh thoảng đi xa");
                }
            }

            if (wantsDurability)
            {
                if (ContainsAny(product.Ten, "Honda"))
                {
                    score += 1.8;
                    reasons.Add("lợi thế về độ phổ biến và dùng lâu dài");
                }
            }

            if (wantsValue)
            {
                if (product.Gia <= 35_000_000m)
                {
                    score += 1.4;
                    reasons.Add("giá dễ cân nhắc trong tầm tiền");
                }
            }

            if (!wantsDistance && product.Gia <= 35_000_000m)
            {
                score += 0.4;
                reasons.Add("giá khá dễ tiếp cận");
            }

            score = Math.Clamp(score, 1.0, 10.0);

            return new CompareScoreResult
            {
                Product = product,
                Score = Math.Round(score, 1),
                Reasons = reasons.Distinct().Take(2).ToList()
            };
        }
        private static string BuildCompareScoreText(
    ProductSummaryDto first,
    ProductSummaryDto second,
    string? feature,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string message)
        {
            var effectiveFeature = feature ?? profile.LastComparisonFeature;

            var s1 = ScoreForCompare(first, effectiveFeature, intent, profile, message);
            var s2 = ScoreForCompare(second, effectiveFeature, intent, profile, message);

            var winner = s1.Score >= s2.Score ? s1 : s2;

            var lines = new List<string>
    {
        "Điểm phù hợp theo tiêu chí hiện tại:",
        $"- **{s1.Product.Ten}**: {s1.Score:0.0}/10" +
            (s1.Reasons.Any() ? $" — {string.Join(", ", s1.Reasons)}." : "."),
        $"- **{s2.Product.Ten}**: {s2.Score:0.0}/10" +
            (s2.Reasons.Any() ? $" — {string.Join(", ", s2.Reasons)}." : "."),
        $"→ Mình nghiêng về **{winner.Product.Ten}** hơn theo tiêu chí này."
    };

            return string.Join("\n", lines);
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
        private static string BuildFeatureVerdict(
     ProductSummaryDto first,
     ProductSummaryDto second,
     string feature,
     bool isFollowUpCompare)
        {
            return feature switch
            {
                "storage" => BuildStorageVerdict(first, second, isFollowUpCompare),
                "fuel_saving" => BuildFuelSavingVerdict(first, second, isFollowUpCompare),
                "female_fit" => BuildFemaleVerdict(first, second, isFollowUpCompare),
                "low_seat" => BuildLowSeatVerdict(first, second, isFollowUpCompare),
                "design_fit" => BuildDesignVerdict(first, second, isFollowUpCompare),
                "work_fit" => BuildWorkVerdict(first, second, isFollowUpCompare),
                "school_fit" => BuildSchoolVerdict(first, second, isFollowUpCompare),
                "power" => BuildPowerVerdict(first, second, isFollowUpCompare),
                "city_fit" => BuildCityVerdict(first, second, isFollowUpCompare),
                "distance_fit" => BuildDistanceVerdict(first, second, isFollowUpCompare),
                "durability" => BuildDurabilityVerdict(first, second, isFollowUpCompare),
                "value" => BuildValueVerdict(first, second, isFollowUpCompare),
                _ => BuildGeneralVerdict(first, second, isFollowUpCompare)
            };
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

        private static bool LooksLikeAlternativeRecommendationRequest(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);

            bool asksOtherOption =
                text.Contains("con xe nao") ||
                text.Contains("xe nao") ||
                text.Contains("mau nao") ||
                text.Contains("con nao") ||
                text.Contains("xe khac") ||
                text.Contains("mau khac") ||
                text.Contains("con khac");

            bool asksCheaper =
                text.Contains("re hon") ||
                text.Contains("mem hon") ||
                text.Contains("thap hon") ||
                text.Contains("it tien hon");

            return asksOtherOption && asksCheaper;
        }
        private static readonly Dictionary<string, string[]> FeatureKeywordMap = new()
        {
            ["female_fit"] = new[] { "nu", "nhe", "de di", "thap", "phu hop nu", "hop nu" },
            ["storage"] = new[] { "cop", "dung do", "mang do" },
            ["fuel_saving"] = new[] { "tiet kiem xang", "hao xang", "an xang", "it hao xang" },
            ["power"] = new[] { "manh", "yeu", "boc", "tang toc", "may khoe" },
            ["city_fit"] = new[] { "di pho", "di trong pho", "do thi" },
            ["distance_fit"] = new[] { "di xa", "phuot", "duong dai" },
            ["durability"] = new[] { "ben", "lau hong", "it hong", "ben hon" },
            ["value"] = new[] { "dang mua", "gia tri", "nen mua", "tot hon", "on hon" }
        };
        private static bool LooksLikeComparePriceFollowUp(
    string message,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            var text = NormalizeText(message);

            bool hasCompareContext =
                profile?.HasActiveCompareContext == true &&
                profile.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 2;

            bool asksPriceCompare =
                string.Equals(intent?.ComparisonFeature, "price", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intent?.FollowUpType, "compare_feature", StringComparison.OrdinalIgnoreCase) ||
                ContainsAny(text,
                    "gia xe nao re hon",
                    "xe nao re hon",
                    "mau nao re hon",
                    "con nao re hon",
                    "cai nao re hon");

            return hasCompareContext && asksPriceCompare;
        }
        private static string? DetectFeature(string message)
        {
            var text = NormalizeText(message);

            foreach (var kvp in FeatureKeywordMap)
            {
                if (kvp.Value.Any(k => text.Contains(k)))
                    return kvp.Key;
            }

            return null;
        }
        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        private static string BuildPowerVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstStrong = ContainsAny(first.Ten, "Air Blade", "Winner", "Exciter", "Raider", "Vario", "PCX", "SH");
            bool secondStrong = ContainsAny(second.Ten, "Air Blade", "Winner", "Exciter", "Raider", "Vario", "PCX", "SH");

            if (firstStrong && !secondStrong)
                return $"Nếu ưu tiên **máy khỏe / cảm giác bốc hơn** thì **{first.Ten}** nhỉnh hơn.";
            if (secondStrong && !firstStrong)
                return $"Nếu ưu tiên **máy khỏe / cảm giác bốc hơn** thì **{second.Ten}** nhỉnh hơn.";

            return "Về độ mạnh, hai mẫu này không chênh quá nhiều trong nhu cầu đi hằng ngày.";
        }

        private static string BuildCityVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstCity = ContainsAny(first.Ten, "Vision", "Janus", "Latte", "Zip", "Attila", "Shark", "Freego");
            bool secondCity = ContainsAny(second.Ten, "Vision", "Janus", "Latte", "Zip", "Attila", "Shark", "Freego");

            if (firstCity && !secondCity)
                return $"Nếu chủ yếu **đi phố**, mình nghiêng về **{first.Ten}** vì gọn và dễ xoay xở hơn.";
            if (secondCity && !firstCity)
                return $"Nếu chủ yếu **đi phố**, mình nghiêng về **{second.Ten}** vì gọn và dễ xoay xở hơn.";

            return "Nếu đi phố hằng ngày, cả hai đều dùng ổn; nên chốt thêm theo giá, độ nhẹ hoặc cốp xe.";
        }

        private static string BuildDistanceVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstDistance = ContainsAny(first.Ten, "Air Blade", "Future", "Winner", "Exciter", "PCX", "SH", "Freego");
            bool secondDistance = ContainsAny(second.Ten, "Air Blade", "Future", "Winner", "Exciter", "PCX", "SH", "Freego");

            if (firstDistance && !secondDistance)
                return $"Nếu hay **đi xa / đi đường dài**, **{first.Ten}** sẽ hợp hơn.";
            if (secondDistance && !firstDistance)
                return $"Nếu hay **đi xa / đi đường dài**, **{second.Ten}** sẽ hợp hơn.";

            return "Nếu chỉ thỉnh thoảng đi xa, hai mẫu này đều cân nhắc được; còn đi xa thường xuyên thì nên ưu tiên mẫu đầm và máy khỏe hơn.";
        }

        private static string BuildDurabilityVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            bool firstHonda = ContainsAny(first.Ten, "Honda");
            bool secondHonda = ContainsAny(second.Ten, "Honda");

            if (firstHonda && !secondHonda)
                return $"Nếu ưu tiên **độ bền / dễ dùng lâu dài**, mình nghiêng về **{first.Ten}**.";
            if (secondHonda && !firstHonda)
                return $"Nếu ưu tiên **độ bền / dễ dùng lâu dài**, mình nghiêng về **{second.Ten}**.";

            return "Về độ bền, hai mẫu này đều dùng ổn; nên cân nhắc thêm chi phí bảo dưỡng và độ phổ biến phụ tùng.";
        }

        private static string BuildValueVerdict(ProductSummaryDto first, ProductSummaryDto second, bool isFollowUpCompare)
        {
            var betterValue = first.Gia <= second.Gia ? first : second;
            return $"Nếu xét **đáng mua trong tầm giá**, mình nghiêng về **{betterValue.Ten}** vì giá mềm hơn và dễ cân nhắc hơn.";
        }
        private static bool LooksLikeRemoveWeakRequest(string message)
        {
            var text = NormalizeText(message);

            return
                (text.Contains("yeu") || text.Contains("khong manh") || text.Contains("may yeu")) &&
                (text.Contains("bo") || text.Contains("loai") || text.Contains("ne") || text.Contains("khong chon"));
        }

        private static ProductSummaryDto PickStrongerProduct(ProductSummaryDto first, ProductSummaryDto second)
        {
            var firstScore = GetPowerScore(first);
            var secondScore = GetPowerScore(second);

            if (firstScore == secondScore)
            {
                return first.CC.GetValueOrDefault() >= second.CC.GetValueOrDefault()
                    ? first
                    : second;
            }

            return firstScore > secondScore ? first : second;
        }

        private static int GetPowerScore(ProductSummaryDto product)
        {
            var name = product.Ten ?? string.Empty;
            var score = 0;

            if (product.CC.HasValue)
                score += product.CC.Value;

            if (ContainsAny(name, "PCX", "Air Blade", "SH", "Vario"))
                score += 40;

            if (ContainsAny(name, "Winner", "Exciter", "Raider", "CBR", "Rebel"))
                score += 50;

            if (ContainsAny(name, "Vision", "Janus", "Zip", "Liberty", "Latte", "Grande"))
                score -= 20;

            return score;
        }
        private static bool HasExplicitProductNameInMessage(string message)
        {
            var text = NormalizeText(message);

            return ProductAliases.Keys.Any(alias =>
                !string.IsNullOrWhiteSpace(alias) &&
                text.Contains(alias, StringComparison.OrdinalIgnoreCase));
        }
        private static bool LooksLikePickOneRequest(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var text = NormalizeText(message);

            return
                text.Contains("chon 1") ||
                text.Contains("chon mot") ||
                text.Contains("lay 1") ||
                text.Contains("chon xe") ||
                text.Contains("chot xe") ||
                text.Contains("chon giup");
        }
        private static string BuildFeatureLabel(string feature)
        {
            return feature switch
            {
                "power" => "máy khỏe / bốc hơn",
                "fuel_saving" => "tiết kiệm xăng",
                "storage" => "cốp rộng",
                "female_fit" => "hợp nữ",
                "low_seat" => "dễ chống chân",
                "work_fit" => "đi làm",
                "school_fit" => "đi học",
                _ => "tiêu chí hiện tại"
            };
        }
    }
}
