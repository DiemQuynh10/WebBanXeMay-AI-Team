using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class RecommendationLLMService : IRecommendationLLMService
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<RecommendationLLMService> _logger;

        public RecommendationLLMService(
            IOpenAIService openAIService,
            ILogger<RecommendationLLMService> logger)
        {
            _openAIService = openAIService;
            _logger = logger;
        }

        public async Task<LLMRecommendationResult?> RerankAsync(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            IReadOnlyList<ProductSummaryDto> candidates)
        {
            if (string.IsNullOrWhiteSpace(message) || candidates == null || candidates.Count == 0)
                return null;

            try
            {
                var prompt = BuildPrompt(message, intent, profile, candidates);

                _logger.LogInformation(
                    "Recommendation LLM rerank request. CandidateCount={CandidateCount}, PromptLength={PromptLength}, Message={Message}",
                    candidates.Count,
                    prompt.Length,
                    message);

                var aiContext = new AIRequestContext
                {
                    ConversationId = !string.IsNullOrWhiteSpace(profile?.ConversationId)
                        ? profile.ConversationId
                        : Guid.NewGuid().ToString(),
                    Channel = "system",
                    UserId = "recommendation-rerank",
                    OriginalUserMessage = message,
                    EffectivePrompt = prompt,
                    RagContext = null
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _openAIService.AskAsync(aiContext, cts.Token);

                if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Reply))
                {
                    _logger.LogWarning("Recommendation LLM rerank returned empty result.");
                    return null;
                }

                var parsed = TryParseJsonResult(response.Reply, candidates);
                if (parsed != null)
                {
                    _logger.LogInformation(
                        "Recommendation LLM rerank success. RecommendationCount={RecommendationCount}, Confidence={Confidence}",
                        parsed.Recommendations.Count,
                        parsed.Confidence);
                    return parsed;
                }

                _logger.LogWarning("Recommendation LLM rerank could not extract valid JSON. Raw: {Raw}", response.Reply);

                var proseFallback = TryBuildFallbackFromText(response.Reply, candidates);
                if (proseFallback != null)
                {
                    _logger.LogInformation(
                        "Recommendation LLM prose fallback success. RecommendationCount={RecommendationCount}",
                        proseFallback.Recommendations.Count);
                    return proseFallback;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Recommendation LLM rerank cancelled due to local timeout.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recommendation LLM rerank failed.");
                return null;
            }
        }

        private static string BuildPrompt(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            IReadOnlyList<ProductSummaryDto> candidates)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Bạn là chuyên viên tư vấn xe máy cho website bán xe.");
            sb.AppendLine("Nhiệm vụ của bạn là chọn tối đa 3 xe phù hợp nhất từ danh sách candidate đã được hệ thống lọc sẵn.");
            sb.AppendLine("Bạn chỉ được chọn trong candidate. Tuyệt đối không bịa thêm xe ngoài danh sách.");
            sb.AppendLine("Hãy tư duy như người tư vấn thật: hiểu nhu cầu, giữ ngữ cảnh hội thoại, cân đối giá - loại xe - hãng - mục đích sử dụng.");
            sb.AppendLine("Không chọn xe chỉ vì trùng từ khóa. Phải chọn vì thật sự hợp với nhu cầu hiện tại.");
            sb.AppendLine("Không trả lời văn xuôi, không markdown, không thêm giải thích ngoài JSON.");
            sb.AppendLine("BẮT BUỘC chỉ trả về đúng 1 JSON object hợp lệ.");
            sb.AppendLine();

            sb.AppendLine("Nguyên tắc tư vấn:");
            sb.AppendLine("1. Nếu người dùng đang lọc tiếp, hãy giữ các ràng buộc cũ còn hợp lý như hãng, loại xe, ngân sách, đối tượng sử dụng.");
            sb.AppendLine("2. Nếu người dùng đưa tiêu chí mới, hãy coi đó là tiêu chí ưu tiên ở lượt hiện tại.");
            sb.AppendLine("3. Nếu có target price, ưu tiên xe gần mức đó. Không chọn xe vượt quá xa ngân sách nếu còn xe gần hơn.");
            sb.AppendLine("4. Nếu user nói khoảng/tầm giá, có thể linh hoạt nhẹ, nhưng không nên vượt quá xa.");
            sb.AppendLine("5. Nếu user nói cho nữ, ưu tiên xe dễ đi, dáng gọn, xe ga tiện dụng, yên vừa phải, hợp đi phố hằng ngày.");
            sb.AppendLine("6. Nếu user nói cho nam, ưu tiên xe chắc, thực dụng, thể thao hoặc mạnh mẽ hơn nếu phù hợp ngân sách.");
            sb.AppendLine("7. Nếu user nhắc hãng, không chọn sai hãng trừ khi candidate cùng hãng không còn lựa chọn hợp lý.");
            sb.AppendLine("8. Nếu user nhắc loại xe, không chọn sai loại xe trừ khi không có lựa chọn nào phù hợp.");
            sb.AppendLine("9. Nếu candidate có xe quá đắt so với nhu cầu hiện tại, chỉ chọn khi user có dấu hiệu muốn nâng cấp/cao cấp.");
            sb.AppendLine("10. Reason phải là lý do tư vấn tự nhiên, cụ thể theo nhu cầu, không nói chung chung.");
            sb.AppendLine("11. Reason không được bịa thông số không có trong candidate.");
            sb.AppendLine("12. Nếu chỉ có 1 xe thật sự hợp, trả 1 recommendation. Không cần cố đủ 3.");
            sb.AppendLine("13. Tuyệt đối không chọn lại các hãng hoặc loại xe mà người dùng vừa loại bỏ (ExcludedBrands, ExcludedCategories).");
            sb.AppendLine("14. Nếu danh sách candidate còn chứa các xe bị loại, phải bỏ qua khi chọn recommendation.");

            sb.AppendLine("Tóm tắt ngữ cảnh cần ưu tiên:");
            sb.AppendLine($"- Tin nhắn hiện tại: {message}");
            sb.AppendLine($"- Hãng ưu tiên: {FirstNonEmpty(intent.Brand, profile?.PreferredBrand) ?? "không rõ"}");
            sb.AppendLine($"- Loại xe ưu tiên: {FirstNonEmpty(intent.Category, profile?.PreferredCategory) ?? "không rõ"}");
            sb.AppendLine($"- Đối tượng sử dụng: {FirstNonEmpty(intent.Target, profile?.Target) ?? "không rõ"}");
            sb.AppendLine($"- Giá thấp nhất: {(intent.PriceMin ?? profile?.PriceMin)?.ToString("N0") ?? "không rõ"}");
            sb.AppendLine($"- Giá cao nhất: {(intent.PriceMax ?? profile?.PriceMax)?.ToString("N0") ?? "không rõ"}");
            sb.AppendLine($"- Giá mục tiêu: {(intent.TargetPrice ?? profile?.TargetPrice)?.ToString("N0") ?? "không rõ"}");
            sb.AppendLine($"- Ưu tiên tiết kiệm xăng: {(intent.WantsFuelSaving || profile?.WantsFuelSaving == true)}");
            sb.AppendLine($"- Ưu tiên cốp rộng: {(intent.WantsLargeStorage || profile?.WantsLargeStorage == true)}");
            sb.AppendLine($"- Ưu tiên dễ điều khiển/yên thấp: {(intent.WantsEasyControl || intent.NeedsLowSeat || profile?.WantsEasyControl == true || profile?.NeedsLowSeat == true)}");
            sb.AppendLine();
            sb.AppendLine($"User message: {message}");
            sb.AppendLine($"IntentType: {intent.IntentType}");
            sb.AppendLine($"Brand: {intent.Brand}");
            sb.AppendLine($"Category: {intent.Category}");
            sb.AppendLine($"Target: {intent.Target}");
            sb.AppendLine($"PriceMin: {intent.PriceMin}");
            sb.AppendLine($"PriceMax: {intent.PriceMax}");
            sb.AppendLine($"TargetPrice: {intent.TargetPrice}");
            sb.AppendLine($"FilterType: {intent.FilterType}");
            sb.AppendLine($"ForWork: {intent.ForWork}");
            sb.AppendLine($"ForSchool: {intent.ForSchool}");
            sb.AppendLine($"ForCity: {intent.ForCity}");
            sb.AppendLine($"ForTour: {intent.ForTour}");
            sb.AppendLine($"WantsFuelSaving: {intent.WantsFuelSaving}");
            sb.AppendLine($"WantsLargeStorage: {intent.WantsLargeStorage}");
            sb.AppendLine($"NeedsLowSeat: {intent.NeedsLowSeat}");
            sb.AppendLine($"WantsEasyControl: {intent.WantsEasyControl}");
            sb.AppendLine($"PrefersMaleStyle: {intent.PrefersMaleStyle}");
            sb.AppendLine($"PrefersFemaleStyle: {intent.PrefersFemaleStyle}");
            sb.AppendLine($"ComparisonFeature: {intent.ComparisonFeature}");
            sb.AppendLine();

            sb.AppendLine($"Profile PreferredBrand: {profile?.PreferredBrand}");
            sb.AppendLine($"Profile PreferredCategory: {profile?.PreferredCategory}");
            sb.AppendLine($"Profile Target: {profile?.Target}");
            sb.AppendLine($"Profile WantsFuelSaving: {profile?.WantsFuelSaving}");
            sb.AppendLine($"Profile WantsLargeStorage: {profile?.WantsLargeStorage}");
            sb.AppendLine($"Profile NeedsLowSeat: {profile?.NeedsLowSeat}");
            sb.AppendLine($"Profile WantsEasyControl: {profile?.WantsEasyControl}");
            sb.AppendLine();

            if (intent.MentionedProducts?.Count > 0)
                sb.AppendLine($"MentionedProducts: {string.Join(", ", intent.MentionedProducts)}");
            if (profile?.LastRecommendedProducts?.Count > 0)
                sb.AppendLine($"LastRecommendedProducts: {string.Join(", ", profile.LastRecommendedProducts)}");
            if (profile?.BaseRecommendedProducts?.Count > 0)
                sb.AppendLine($"BaseRecommendedProducts: {string.Join(", ", profile.BaseRecommendedProducts)}");
            if (profile?.ExcludedBrands?.Count > 0)
                sb.AppendLine($"ExcludedBrands: {string.Join(", ", profile.ExcludedBrands)}");
            if (profile?.ExcludedCategories?.Count > 0)
                sb.AppendLine($"ExcludedCategories: {string.Join(", ", profile.ExcludedCategories)}");
            if (profile?.RequestedStyles?.Count > 0)
                sb.AppendLine($"RequestedStyles: {string.Join(", ", profile.RequestedStyles)}");
            if (profile?.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0)
            {
                var validNames = profile.CurrentRecommendedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (validNames.Count > 0)
                {
                    sb.AppendLine($"CurrentRecommendedProducts: {string.Join(", ", validNames)}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("Danh sách candidate hợp lệ:");
            foreach (var item in candidates)
            {
                sb.AppendLine($"- productId={item.Id}; name={item.Ten}; brand={item.ThuongHieu}; category={item.Loai}; price={item.Gia}; stock={item.SoLuong}; cc={item.CC}; tags={item.Tags}");
            }

            sb.AppendLine();
            sb.AppendLine("Schema JSON bắt buộc:");
            sb.AppendLine("""
{
  "confidence": 0.0,
  "recommendations": [
    {
      "productId": 0,
     "reason": "lý do tư vấn ngắn, gắn với nhu cầu hiện tại",
      "score": 0.0
    }
  ]
}
""");
            sb.AppendLine();
            sb.AppendLine("Ràng buộc bắt buộc:");
            sb.AppendLine("- Chỉ dùng productId có trong danh sách candidate.");
            sb.AppendLine("- Tối đa 3 recommendations.");
            sb.AppendLine("- Score trong khoảng 0 đến 1.");
            sb.AppendLine("- Không viết bất kỳ câu nào ngoài JSON.");
            sb.AppendLine("- Nếu không có lựa chọn hoàn hảo, hãy chọn candidate gần đúng nhất và nêu reason trung thực.");
            sb.AppendLine("- Không chọn xe quá xa ngân sách chỉ để đủ số lượng.");
            sb.AppendLine("- Không chọn xe sai hãng/sai loại nếu còn candidate đúng hãng/đúng loại.");
            sb.AppendLine("- Không lặp lại reason giống nhau cho nhiều xe.");
            sb.AppendLine("- Nếu xe được chọn có giá cao hơn ngân sách, reason phải nói rõ là phương án nâng nhẹ ngân sách.");

            return sb.ToString();
        }

        private static LLMRecommendationResult? TryParseJsonResult(
            string raw,
            IReadOnlyList<ProductSummaryDto> candidates)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var parsed = JsonSerializer.Deserialize<LLMRecommendationResult>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return SanitizeResult(parsed, candidates);
            }
            catch
            {
                return null;
            }
        }

        private static LLMRecommendationResult? SanitizeResult(
            LLMRecommendationResult? parsed,
            IReadOnlyList<ProductSummaryDto> candidates)
        {
            if (parsed == null || candidates == null || candidates.Count == 0)
                return null;

            parsed.Recommendations ??= new List<LLMRecommendedItem>();

            var validIds = candidates.Select(x => x.Id).ToHashSet();
            var seenIds = new HashSet<int>();

            var sanitizedItems = parsed.Recommendations
                .Where(x => x != null && validIds.Contains(x.ProductId) && seenIds.Add(x.ProductId))
                .Select(x => new LLMRecommendedItem
                {
                    ProductId = x.ProductId,
                    Reason = NormalizeReason(x.Reason),
                    Score = ClampScore(x.Score)
                })
                .OrderByDescending(x => x.Score)
                .Take(3)
                .ToList();

            if (sanitizedItems.Count == 0)
                return null;

            return new LLMRecommendationResult
            {
                Confidence = Math.Clamp(parsed.Confidence, 0.0, 1.0),
                Recommendations = sanitizedItems
            };
        }

        private static string NormalizeReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return "Phù hợp với nhu cầu hiện tại.";

            var normalized = Regex.Replace(reason.Trim(), @"\s+", " ");

            if (normalized.Length > 140)
                normalized = normalized.Substring(0, 140).Trim().TrimEnd(',', ';', ':');

            if (!normalized.EndsWith("."))
                normalized += ".";

            return normalized;
        }
        private static double ClampScore(double score)
        {
            if (double.IsNaN(score) || double.IsInfinity(score))
                return 0.5;

            return Math.Clamp(score, 0.0, 1.0);
        }

        private static LLMRecommendationResult? TryBuildFallbackFromText(
            string raw,
            IReadOnlyList<ProductSummaryDto> candidates)
        {
            if (string.IsNullOrWhiteSpace(raw) || candidates == null || candidates.Count == 0)
                return null;

            var normalizedRaw = NormalizeText(raw);
            var matched = new List<LLMRecommendedItem>();
            var seenIds = new HashSet<int>();

            foreach (var item in candidates)
            {
                var productName = item.Ten ?? string.Empty;
                if (string.IsNullOrWhiteSpace(productName))
                    continue;

                var normalizedName = NormalizeText(productName);
                if (!string.IsNullOrWhiteSpace(normalizedName) &&
                    normalizedRaw.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) &&
                    seenIds.Add(item.Id))
                {
                    matched.Add(new LLMRecommendedItem
                    {
                        ProductId = item.Id,
                        Reason = $"Được AI ưu tiên vì khá hợp với nhu cầu hiện tại",
                        Score = Math.Max(0.4, 1.0 - matched.Count * 0.1)
                    });
                }
            }

            if (matched.Count == 0)
            {
                foreach (var item in candidates)
                {
                    var tokens = SplitKeywords(item.Ten);
                    if (tokens.Count == 0)
                        continue;

                    var tokenHit = tokens.Count(t => normalizedRaw.Contains(t, StringComparison.OrdinalIgnoreCase));
                    if (tokenHit >= 2 && seenIds.Add(item.Id))
                    {
                        matched.Add(new LLMRecommendedItem
                        {
                            ProductId = item.Id,
                            Reason = "Là một phương án khá đáng cân nhắc theo tiêu chí hiện tại",
                            Score = Math.Max(0.3, 0.75 - matched.Count * 0.1)
                        });
                    }
                }
            }

            if (matched.Count == 0)
                return null;

            return new LLMRecommendationResult
            {
                Confidence = 0.55,
                Recommendations = matched
                    .OrderByDescending(x => x.Score)
                    .Take(3)
                    .ToList()
            };
        }

        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = input.Trim().ToLowerInvariant();

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

            text = Regex.Replace(text, @"\s+", " ");
            return text;
        }

        private static List<string> SplitKeywords(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => NormalizeText(x))
                .Where(x => x.Length >= 3)
                .Distinct()
                .ToList();
        }

        private static string? ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var cleaned = raw.Trim();
            cleaned = cleaned.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase);
            cleaned = cleaned.Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            return cleaned.Substring(start, end - start + 1).Trim();
        }
        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }
    }
}
