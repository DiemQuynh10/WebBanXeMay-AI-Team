using System.Text;
using System.Text.Json;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class LLMIntentUnderstandingService : ILLMIntentUnderstandingService
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<LLMIntentUnderstandingService> _logger;

        public LLMIntentUnderstandingService(
            IOpenAIService openAIService,
            ILogger<LLMIntentUnderstandingService> logger)
        {
            _openAIService = openAIService;
            _logger = logger;
        }

        public async Task<LLMIntentResult?> UnderstandAsync(
            string message,
            CustomerPreferenceProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            try
            {
                var prompt = BuildPrompt(message, profile);

                var aiContext = new AIRequestContext
                {
                    ConversationId = profile?.ConversationId ?? Guid.NewGuid().ToString(),
                    Channel = "system",
                    UserId = "intent-understanding",
                    OriginalUserMessage = message,
                    EffectivePrompt = prompt,
                    RagContext = null
                };

                var result = await _openAIService.AskAsync(aiContext);

                if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.Reply))
                {
                    _logger.LogWarning("LLM intent understanding returned empty result.");
                    return null;
                }

                var json = ExtractJson(result.Reply);
                if (string.IsNullOrWhiteSpace(json))
                {
                    _logger.LogWarning("LLM intent understanding could not extract JSON. Raw: {Raw}", result.Reply);
                    return null;
                }

                var parsed = JsonSerializer.Deserialize<LLMIntentResult>(
    json,
    new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

                if (parsed == null)
                    return null;

                parsed.MentionedProducts ??= new List<string>();
                parsed.ExcludedBrands ??= new List<string>();
                parsed.ExcludedCategories ??= new List<string>();
                parsed.RequestedStyles ??= new List<string>();
                parsed.RejectedStyles ??= new List<string>();

                if (parsed.Confidence < 0)
                    parsed.Confidence = 0;

                if (parsed.Confidence > 1)
                    parsed.Confidence = 1;

                return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM intent understanding failed.");
                return null;
            }
        }

        private static string BuildPrompt(string message, CustomerPreferenceProfile? profile)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Bạn là bộ phân tích ý định cho chatbot bán xe máy.");
            sb.AppendLine("Nhiệm vụ của bạn là hiểu câu người dùng theo ngữ cảnh hội thoại trước đó.");
            sb.AppendLine("Bạn CHỈ được phân tích ý định và ràng buộc.");
            sb.AppendLine("KHÔNG được tư vấn xe.");
            sb.AppendLine("KHÔNG được bịa giá, tồn kho, thông tin sản phẩm.");
            sb.AppendLine("CHỈ trả về đúng 1 JSON hợp lệ, không thêm markdown, không thêm giải thích.");
            sb.AppendLine();

            sb.AppendLine("Các intentType hợp lệ:");
            sb.AppendLine("- product_lookup: hỏi giá, còn hàng, tồn kho, chi tiết của mẫu xe cụ thể");
            sb.AppendLine("- product_search: tìm/lọc xe theo hãng, giá, loại nhưng chưa hẳn là tư vấn");
            sb.AppendLine("- recommend: hỏi tư vấn xe phù hợp theo nhu cầu");
            sb.AppendLine("- refine: đang lọc tiếp từ nhóm xe vừa gợi ý");
            sb.AppendLine("- compare: đang so sánh 2 mẫu xe hoặc hỏi tiêu chí so sánh tiếp");
            sb.AppendLine("- out_of_scope: ngoài phạm vi xe máy / đơn hàng / tư vấn mua xe");
            sb.AppendLine("- unknown: chưa đủ chắc để kết luận");
            sb.AppendLine();

            sb.AppendLine("Quy tắc hiểu hội thoại:");
            sb.AppendLine("- Nếu người dùng đổi ý rõ như: 'đổi ý', 'không phải ... nữa', 'giờ muốn ...', 'ý là muốn xem ...' => resetContext=true.");
            sb.AppendLine("- Nếu người dùng đang nói tiếp trên nhóm cũ như: 'còn honda thì sao', 'rẻ hơn chút', 'cốp rộng hơn', 'đừng xe số' => isFollowUp=true.");
            sb.AppendLine("- Nếu câu đang hỏi tiếp trên 2 mẫu đã so sánh trước đó như 'con nào cốp rộng hơn' => intentType=compare.");
            sb.AppendLine("- Nếu người dùng nói mơ hồ, thiếu dữ kiện quan trọng thì shouldAskClarification=true và clarificationQuestion phải ngắn, tự nhiên.");
            sb.AppendLine("- Nếu không chắc, đặt confidence thấp.");
            sb.AppendLine();

            sb.AppendLine("Hãy trích xuất thêm các ràng buộc nếu có:");
            sb.AppendLine("- brand");
            sb.AppendLine("- category");
            sb.AppendLine("- target");
            sb.AppendLine("- priceMin / priceMax / targetPrice / priceFilterType");
            sb.AppendLine("- forWork / forSchool / forCity / forTour");
            sb.AppendLine("- wantsFuelSaving / wantsLargeStorage / wantsEasyControl / needsLowSeat");
            sb.AppendLine("- heightCm");
            sb.AppendLine("- mentionedProducts");
            sb.AppendLine("- excludedBrands / excludedCategories");
            sb.AppendLine("- requestedStyles / rejectedStyles");
            sb.AppendLine("- comparisonFeature");
            sb.AppendLine();

            sb.AppendLine("Schema JSON bắt buộc:");
            sb.AppendLine(@"
{
  ""intentType"": ""product_lookup | product_search | recommend | refine | compare | out_of_scope | unknown"",
  ""isFollowUp"": true,
  ""resetContext"": false,
  ""followUpType"": ""restart | refine | expand | compare | lookup_followup | none"",
  ""reason"": ""lý do ngắn gọn"",
  ""isDirectLookup"": false,
  ""isFreshSearch"": false,
  ""confidence"": 0.0,
  ""shouldAskClarification"": false,
  ""clarificationQuestion"": null,
  ""brand"": null,
  ""category"": null,
  ""target"": null,
  ""priceMin"": null,
  ""priceMax"": null,
  ""targetPrice"": null,
  ""priceFilterType"": null,
  ""forWork"": false,
  ""forSchool"": false,
  ""forCity"": false,
  ""forTour"": false,
  ""wantsFuelSaving"": false,
  ""wantsLargeStorage"": false,
  ""wantsEasyControl"": false,
  ""needsLowSeat"": false,
  ""heightCm"": null,
  ""mentionedProducts"": [],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": null
}");

            sb.AppendLine();
            sb.AppendLine("Ví dụ 1:");
            sb.AppendLine(@"Input: ""vision giá bao nhiêu""");
            sb.AppendLine(@"Output:");
            sb.AppendLine(@"{
  ""intentType"": ""product_lookup"",
  ""isFollowUp"": false,
  ""resetContext"": false,
  ""followUpType"": ""none"",
  ""reason"": ""hỏi giá của mẫu xe cụ thể"",
  ""isDirectLookup"": true,
  ""isFreshSearch"": false,
  ""confidence"": 0.97,
  ""shouldAskClarification"": false,
  ""clarificationQuestion"": null,
  ""brand"": ""Honda"",
  ""category"": null,
  ""target"": null,
  ""priceMin"": null,
  ""priceMax"": null,
  ""targetPrice"": null,
  ""priceFilterType"": null,
  ""forWork"": false,
  ""forSchool"": false,
  ""forCity"": false,
  ""forTour"": false,
  ""wantsFuelSaving"": false,
  ""wantsLargeStorage"": false,
  ""wantsEasyControl"": false,
  ""needsLowSeat"": false,
  ""heightCm"": null,
  ""mentionedProducts"": [""Honda Vision""],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": null
}");

            sb.AppendLine();
            sb.AppendLine("Ví dụ 2:");
            sb.AppendLine(@"Input: ""xe cho nữ khoảng 40 triệu""");
            sb.AppendLine(@"Output:");
            sb.AppendLine(@"{
  ""intentType"": ""recommend"",
  ""isFollowUp"": false,
  ""resetContext"": false,
  ""followUpType"": ""none"",
  ""reason"": ""đang hỏi tư vấn xe phù hợp theo nhu cầu và ngân sách"",
  ""isDirectLookup"": false,
  ""isFreshSearch"": true,
  ""confidence"": 0.94,
  ""shouldAskClarification"": false,
  ""clarificationQuestion"": null,
  ""brand"": null,
  ""category"": null,
  ""target"": ""nữ"",
  ""priceMin"": null,
  ""priceMax"": null,
  ""targetPrice"": 40000000,
  ""priceFilterType"": ""around"",
  ""forWork"": false,
  ""forSchool"": false,
  ""forCity"": true,
  ""forTour"": false,
  ""wantsFuelSaving"": false,
  ""wantsLargeStorage"": false,
  ""wantsEasyControl"": true,
  ""needsLowSeat"": false,
  ""heightCm"": null,
  ""mentionedProducts"": [],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": null
}");

            sb.AppendLine();
            sb.AppendLine("Ví dụ 3:");
            sb.AppendLine(@"Input: ""còn honda thì sao""");
            sb.AppendLine(@"Ngữ cảnh: trước đó bot vừa gợi ý 4 mẫu xe.");
            sb.AppendLine(@"Output:");
            sb.AppendLine(@"{
  ""intentType"": ""refine"",
  ""isFollowUp"": true,
  ""resetContext"": false,
  ""followUpType"": ""expand"",
  ""reason"": ""đang thu hẹp hoặc đổi hướng trong nhóm gợi ý trước đó"",
  ""isDirectLookup"": false,
  ""isFreshSearch"": false,
  ""confidence"": 0.90,
  ""shouldAskClarification"": false,
  ""clarificationQuestion"": null,
  ""brand"": ""Honda"",
  ""category"": null,
  ""target"": null,
  ""priceMin"": null,
  ""priceMax"": null,
  ""targetPrice"": null,
  ""priceFilterType"": null,
  ""forWork"": false,
  ""forSchool"": false,
  ""forCity"": false,
  ""forTour"": false,
  ""wantsFuelSaving"": false,
  ""wantsLargeStorage"": false,
  ""wantsEasyControl"": false,
  ""needsLowSeat"": false,
  ""heightCm"": null,
  ""mentionedProducts"": [],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": null
}");

            sb.AppendLine();
            sb.AppendLine("Ví dụ 4:");
            sb.AppendLine(@"Input: ""không phải 45 triệu nữa, giờ muốn quanh 30 thôi""");
            sb.AppendLine(@"Output:");
            sb.AppendLine(@"{
  ""intentType"": ""recommend"",
  ""isFollowUp"": true,
  ""resetContext"": true,
  ""followUpType"": ""restart"",
  ""reason"": ""người dùng đổi ý và bắt đầu lại theo mức giá mới"",
  ""isDirectLookup"": false,
  ""isFreshSearch"": true,
  ""confidence"": 0.96,
  ""shouldAskClarification"": false,
  ""clarificationQuestion"": null,
  ""brand"": null,
  ""category"": null,
  ""target"": null,
  ""priceMin"": null,
  ""priceMax"": null,
  ""targetPrice"": 30000000,
  ""priceFilterType"": ""around"",
  ""forWork"": false,
  ""forSchool"": false,
  ""forCity"": false,
  ""forTour"": false,
  ""wantsFuelSaving"": false,
  ""wantsLargeStorage"": false,
  ""wantsEasyControl"": false,
  ""needsLowSeat"": false,
  ""heightCm"": null,
  ""mentionedProducts"": [],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": null
}");

            sb.AppendLine();
            sb.AppendLine("Ví dụ 5:");
            sb.AppendLine(@"Input: ""mình nữ, thấp người, cần cốp rộng đi làm""");
            sb.AppendLine(@"Output:");
            sb.AppendLine(@"{
  ""intentType"": ""recommend"",
  ""isFollowUp"": false,
  ""resetContext"": false,
  ""followUpType"": ""none"",
  ""reason"": ""đang hỏi tư vấn với nhiều tiêu chí nhu cầu tự nhiên"",
  ""isDirectLookup"": false,
  ""isFreshSearch"": true,
  ""confidence"": 0.95,
  ""shouldAskClarification"": true,
  ""clarificationQuestion"": ""Bạn muốn mình ưu tiên thêm tầm giá nào để lọc sát hơn?"",
  ""brand"": null,
  ""category"": null,
  ""target"": ""nữ"",
  ""priceMin"": null,
  ""priceMax"": null,
  ""targetPrice"": null,
  ""priceFilterType"": null,
  ""forWork"": true,
  ""forSchool"": false,
  ""forCity"": true,
  ""forTour"": false,
  ""wantsFuelSaving"": false,
  ""wantsLargeStorage"": true,
  ""wantsEasyControl"": true,
  ""needsLowSeat"": true,
  ""heightCm"": null,
  ""mentionedProducts"": [],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": null
}");

            sb.AppendLine();
            sb.AppendLine("Ngữ cảnh trước đó:");

            if (profile == null)
            {
                sb.AppendLine("- Không có profile.");
            }
            else
            {
                sb.AppendLine($"- ActiveFlow: {profile.ActiveFlow}");
                sb.AppendLine($"- HasActiveRecommendationContext: {profile.HasActiveRecommendationContext}");
                sb.AppendLine($"- HasActiveCompareContext: {profile.HasActiveCompareContext}");
                sb.AppendLine($"- LastRecommendedProducts: {string.Join(", ", profile.LastRecommendedProducts)}");
                sb.AppendLine($"- LastComparedProducts: {string.Join(", ", profile.LastComparedProducts)}");
                sb.AppendLine($"- PriceMin: {profile.PriceMin}");
                sb.AppendLine($"- PriceMax: {profile.PriceMax}");
                sb.AppendLine($"- TargetPrice: {profile.TargetPrice}");
                sb.AppendLine($"- Target: {profile.Target}");
                sb.AppendLine($"- PreferredBrand: {profile.PreferredBrand}");
                sb.AppendLine($"- PreferredCategory: {profile.PreferredCategory}");
                sb.AppendLine($"- ForWork: {profile.ForWork}");
                sb.AppendLine($"- ForSchool: {profile.ForSchool}");
                sb.AppendLine($"- WantsFuelSaving: {profile.WantsFuelSaving}");
                sb.AppendLine($"- WantsLargeStorage: {profile.WantsLargeStorage}");
                sb.AppendLine($"- NeedsLowSeat: {profile.NeedsLowSeat}");
                sb.AppendLine($"- HeightCm: {profile.HeightCm}");
            }

            sb.AppendLine();
            sb.AppendLine($"Câu hiện tại của người dùng: \"{message}\"");

            return sb.ToString();
        }

        private static string? ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();

            var firstBrace = raw.IndexOf('{');
            var lastBrace = raw.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return raw.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return null;
        }
    }
}