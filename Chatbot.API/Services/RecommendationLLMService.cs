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
                    ConversationId = profile?.ConversationId ?? Guid.NewGuid().ToString(),
                    Channel = "system",
                    UserId = "recommendation-rerank",
                    OriginalUserMessage = message,
                    EffectivePrompt = prompt,
                    RagContext = null
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var response = await _openAIService.AskAsync(aiContext, cts.Token);

                if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Reply))
                {
                    _logger.LogWarning("Recommendation LLM rerank returned empty result.");
                    return null;
                }

                var json = ExtractJson(response.Reply);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var parsed = JsonSerializer.Deserialize<LLMRecommendationResult>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    if (parsed != null)
                    {
                        parsed.Recommendations ??= new List<LLMRecommendedItem>();

                        // lọc lại chỉ giữ productId hợp lệ trong candidate
                        var validIds = candidates.Select(x => x.Id).ToHashSet();
                        parsed.Recommendations = parsed.Recommendations
                            .Where(x => validIds.Contains(x.ProductId))
                            .OrderByDescending(x => x.Score)
                            .Take(3)
                            .ToList();

                        if (parsed.Recommendations.Count > 0)
                        {
                            _logger.LogInformation(
                                "Recommendation LLM rerank success via JSON. RecommendationCount={RecommendationCount}",
                                parsed.Recommendations.Count);

                            return parsed;
                        }
                    }
                }

                _logger.LogWarning("Recommendation LLM rerank could not extract valid JSON. Raw: {Raw}", response.Reply);

                var fallback = TryBuildFallbackFromText(response.Reply, candidates);
                if (fallback != null)
                {
                    _logger.LogInformation(
                        "Recommendation LLM prose fallback success. RecommendationCount={RecommendationCount}",
                        fallback.Recommendations.Count);

                    return fallback;
                }
                _logger.LogWarning("Recommendation LLM rerank failed after JSON parse and prose fallback.");
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

            sb.AppendLine("Bạn là bộ xếp hạng xe máy cho chatbot.");
            sb.AppendLine("Nhiệm vụ: chọn tối đa 3 sản phẩm phù hợp nhất từ danh sách ứng viên đã cho.");
            sb.AppendLine("Không được bịa sản phẩm mới.");
            sb.AppendLine("Không được trả lời giải thích dạng văn xuôi.");
            sb.AppendLine("Không được thêm markdown.");
            sb.AppendLine("Không được thêm ký tự ```.");
            sb.AppendLine("BẮT BUỘC chỉ trả về đúng 1 JSON object hợp lệ.");
            sb.AppendLine("Output phải bắt đầu bằng ký tự { và kết thúc bằng ký tự }.");
            sb.AppendLine("Nếu không chắc, vẫn phải trả JSON theo đúng schema.");
            sb.AppendLine();

            sb.AppendLine("Ưu tiên xếp hạng theo:");
            sb.AppendLine("1. đúng nhu cầu người dùng");
            sb.AppendLine("2. đúng khoảng giá");
            sb.AppendLine("3. đúng hãng hoặc loại xe nếu có");
            sb.AppendLine("4. reason ngắn gọn, tự nhiên, không dài dòng");
            sb.AppendLine();

            sb.AppendLine($"User message: {message}");
            sb.AppendLine($"Brand: {intent.Brand}");
            sb.AppendLine($"Category: {intent.Category}");
            sb.AppendLine($"Target: {intent.Target}");
            sb.AppendLine($"PriceMin: {intent.PriceMin}");
            sb.AppendLine($"PriceMax: {intent.PriceMax}");
            sb.AppendLine($"TargetPrice: {intent.TargetPrice}");
            sb.AppendLine($"WantsFuelSaving: {intent.WantsFuelSaving}");
            sb.AppendLine($"WantsLargeStorage: {intent.WantsLargeStorage}");
            sb.AppendLine($"NeedsLowSeat: {intent.NeedsLowSeat}");
            sb.AppendLine($"WantsEasyControl: {intent.WantsEasyControl}");
            sb.AppendLine();

            sb.AppendLine("Danh sách candidate hợp lệ:");
            foreach (var item in candidates)
            {
                sb.AppendLine(
                    $"- productId={item.Id}; name={item.Ten}; brand={item.ThuongHieu}; category={item.Loai}; price={item.Gia}");
            }

            sb.AppendLine();
            sb.AppendLine("Schema JSON bắt buộc:");
            sb.AppendLine("""
{
  "confidence": 0.0,
  "recommendations": [
    {
      "productId": 0,
      "reason": "lý do ngắn gọn",
      "score": 0.0
    }
  ]
}
""");

            sb.AppendLine();
            sb.AppendLine("Ràng buộc bắt buộc:");
            sb.AppendLine("- Chỉ dùng productId có trong danh sách candidate.");
            sb.AppendLine("- Tối đa 3 recommendations.");
            sb.AppendLine("- Không viết thêm bất kỳ câu nào ngoài JSON.");
            sb.AppendLine("- Nếu không có lựa chọn hoàn hảo, hãy chọn các candidate gần đúng nhất.");
            sb.AppendLine("- Nếu chỉ chọn 1 hoặc 2 sản phẩm thì vẫn trả JSON hợp lệ.");

            return sb.ToString();
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

                if (normalizedRaw.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    if (seenIds.Add(item.Id))
                    {
                        matched.Add(new LLMRecommendedItem
                        {
                            ProductId = item.Id,
                            Reason = $"Phù hợp với nhu cầu từ gợi ý AI cho {item.Ten}",
                            Score = 1.0 - matched.Count * 0.1
                        });
                    }
                }
            }

            if (matched.Count == 0)
            {
                // fallback mềm hơn: match theo token chính
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
                            Reason = $"Được suy ra từ phản hồi AI cho {item.Ten}",
                            Score = 0.7 - matched.Count * 0.1
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
            text = Regex.Replace(text, @"\s+", " ");
            return text;
        }

        private static List<string> SplitKeywords(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            return input
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => x.Trim().ToLowerInvariant())
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
    }
}