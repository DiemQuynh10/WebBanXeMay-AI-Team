using System.Text;
using System.Text.Json;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ContextualIntentClassifierService : IContextualIntentClassifierService
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<ContextualIntentClassifierService> _logger;

        public ContextualIntentClassifierService(
            IOpenAIService openAIService,
            ILogger<ContextualIntentClassifierService> logger)
        {
            _openAIService = openAIService;
            _logger = logger;
        }

        public async Task<ContextualIntentDecision?> ClassifyAsync(
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return null;

            try
            {
                var prompt = BuildPrompt(normalizedMessage, parsedIntent, profile);

                var aiContext = new AIRequestContext
                {
                    ConversationId = profile?.ConversationId ?? Guid.NewGuid().ToString(),
                    Channel = "system",
                    UserId = "contextual-intent-classifier",
                    OriginalUserMessage = normalizedMessage,
                    EffectivePrompt = prompt,
                    RagContext = null
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(6));

                var response = await _openAIService.AskAsync(aiContext, cts.Token);

                if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Reply))
                    return null;

                return TryParse(response.Reply);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Contextual intent classification failed.");
                return null;
            }
        }

        private static string BuildPrompt(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Bạn là bộ phân loại ý định theo ngữ cảnh cho chatbot bán xe máy.");
            sb.AppendLine("Không trả lời người dùng. Chỉ trả về JSON hợp lệ.");
            sb.AppendLine();
            sb.AppendLine("Flow hợp lệ:");
            sb.AppendLine("- recommendation");
            sb.AppendLine("- refinement");
            sb.AppendLine("- compare");
            sb.AppendLine("- product_lookup");
            sb.AppendLine("- order_lookup");
            sb.AppendLine("- out_of_scope");
            sb.AppendLine("- clarification");
            sb.AppendLine("- unknown");
            sb.AppendLine();
            sb.AppendLine("Action hợp lệ:");
            sb.AppendLine("- start_new");
            sb.AppendLine("- refine_feature");
            sb.AppendLine("- refine_price");
            sb.AppendLine("- switch_brand");
            sb.AppendLine("- switch_category");
            sb.AppendLine("- alternative");
            sb.AppendLine("- decide_best");
            sb.AppendLine("- compare_products");
            sb.AppendLine("- out_of_scope");
            sb.AppendLine("- unknown");
            sb.AppendLine();
            sb.AppendLine("Quy tắc:");
            sb.AppendLine("- Nếu user thêm tiêu chí như cốp rộng, tiết kiệm xăng, dễ chống chân, đi làm, đi học => refinement/refine_feature.");
            sb.AppendLine("- Nếu user nói mẫu khác, xe khác, đổi mẫu khác => refinement/alternative.");
            sb.AppendLine("- Nếu user nói honda đi, yamaha đi, còn honda thì sao => refinement/switch_brand.");
            sb.AppendLine("- Nếu user nói xe nào ổn, nên chọn xe nào, con nào hợp hơn => refinement/decide_best.");
            sb.AppendLine("- Chỉ chọn compare khi user thật sự muốn so sánh 2 xe, dùng từ so sánh, khác nhau, so với, hoặc nhắc rõ 2 mẫu xe.");
            sb.AppendLine("- Nếu ngoài phạm vi xe máy / sản phẩm / đơn hàng => out_of_scope/out_of_scope.");
            sb.AppendLine("- Nếu không chắc, trả confidence thấp.");
            sb.AppendLine("- shouldKeepPreviousFeature=true nếu user đang tiếp tục tiêu chí cũ, ví dụ: 'còn honda có cốp rộng không', 'mẫu khác nhưng vẫn cốp rộng'.");
            sb.AppendLine("- shouldKeepPreviousFeature=false nếu user đổi hướng rõ, ví dụ: 'honda đi', 'yamaha đi', 'mẫu khác đi' mà không nhắc lại tiêu chí cũ.");
            sb.AppendLine("- Nếu không chắc, để shouldKeepPreviousFeature=null.");
            sb.AppendLine();
            sb.AppendLine($"Message: {message}");
            sb.AppendLine();
            sb.AppendLine("Parsed intent:");
            sb.AppendLine($"IntentType: {intent?.IntentType}");
            sb.AppendLine($"Brand: {intent?.Brand}");
            sb.AppendLine($"Category: {intent?.Category}");
            sb.AppendLine($"Target: {intent?.Target}");
            sb.AppendLine($"ComparisonFeature: {intent?.ComparisonFeature}");
            sb.AppendLine($"MentionedProducts: {string.Join(", ", intent?.MentionedProducts ?? new List<string>())}");
            sb.AppendLine($"IsDirectCompare: {intent?.IsDirectCompare}");
            sb.AppendLine($"IsOutOfScope: {intent?.IsOutOfScope}");
            sb.AppendLine();
            sb.AppendLine("Conversation context:");
            sb.AppendLine($"ActiveFlow: {profile?.ActiveFlow}");
            sb.AppendLine($"HasActiveRecommendationContext: {profile?.HasActiveRecommendationContext}");
            sb.AppendLine($"HasActiveCompareContext: {profile?.HasActiveCompareContext}");
            sb.AppendLine($"CurrentRecommendedProducts: {string.Join(", ", profile?.CurrentRecommendedProducts ?? new List<string>())}");
            sb.AppendLine($"LastRecommendedProducts: {string.Join(", ", profile?.LastRecommendedProducts ?? new List<string>())}");
            sb.AppendLine($"LastComparedProducts: {string.Join(", ", profile?.LastComparedProducts ?? new List<string>())}");
            sb.AppendLine();
            sb.AppendLine("JSON schema:");
            sb.AppendLine("""
{
  "flow": "refinement",
  "action": "refine_feature",
  "confidence": 0.0,
  "shouldKeepPreviousFeature": true,
  "reason": "ngắn gọn"
}
""");

            return sb.ToString();
        }

        private static ContextualIntentDecision? TryParse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var cleaned = raw.Trim()
                .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');

            if (start < 0 || end <= start)
                return null;

            var json = cleaned.Substring(start, end - start + 1);

            try
            {
                return JsonSerializer.Deserialize<ContextualIntentDecision>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch
            {
                return null;
            }
        }
    }
}