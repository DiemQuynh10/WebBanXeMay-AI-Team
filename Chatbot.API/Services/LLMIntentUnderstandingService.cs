using System.Text;
using System.Text.Json;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class LLMIntentUnderstandingService : ILLMIntentUnderstandingService
    {
        private static readonly HashSet<string> AllowedIntentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "product_lookup",
            "product_search",
            "recommend",
            "refine",
            "compare",
            "order_lookup",
            "greeting",
            "out_of_scope",
            "unknown"
        };

        private static readonly HashSet<string> AllowedFollowUpTypes = new(StringComparer.OrdinalIgnoreCase)
{
    "restart",
    "refine",
    "expand",
    "compare",
    "lookup_followup",
    "change_product",
    "none"
};
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

                Sanitize(parsed, message, profile);

                _logger.LogInformation(
                    "LLM intent parsed. IntentType={IntentType}, FollowUp={IsFollowUp}, FollowUpType={FollowUpType}, Brand={Brand}, Category={Category}, Confidence={Confidence}",
                    parsed.IntentType,
                    parsed.IsFollowUp,
                    parsed.FollowUpType,
                    parsed.Brand,
                    parsed.Category,
                    parsed.Confidence);

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
            sb.AppendLine("Nhiệm vụ của bạn là hiểu CÂU HIỆN TẠI dựa trên NGỮ CẢNH HỘI THOẠI trước đó.");
            sb.AppendLine("Bạn CHỈ được phân tích ý định, mức độ nối tiếp ngữ cảnh và các ràng buộc.");
            sb.AppendLine("LLM chỉ được phân loại action/intent, KHÔNG được tự chọn xe.");
            sb.AppendLine("KHÔNG được tư vấn xe.");
            sb.AppendLine("KHÔNG được bịa giá, tồn kho, thông tin sản phẩm, đơn hàng hoặc chính sách.");
            sb.AppendLine("CHỈ trả về đúng 1 JSON hợp lệ. Không markdown. Không giải thích. Không văn bản ngoài JSON.");
            sb.AppendLine();

            sb.AppendLine("Ưu tiên số 1: hiểu câu nói tự nhiên, nói lửng, nói tiếp theo ngữ cảnh.");
            sb.AppendLine("Ví dụ follow-up tự nhiên gồm:");
            sb.AppendLine("- còn honda thì sao");
            sb.AppendLine("- con nào cốp rộng hơn");
            sb.AppendLine("- loại đỡ hao xăng hơn");
            sb.AppendLine("- rẻ hơn chút đi");
            sb.AppendLine("- đừng xe số");
            sb.AppendLine("- mẫu kia hơi nữ quá");
            sb.AppendLine("- so với vision thì sao");
            sb.AppendLine("- giờ ưu tiên đi làm hằng ngày");
            sb.AppendLine();

            sb.AppendLine("Các intentType hợp lệ:");
            sb.AppendLine("- greeting: chào hỏi đơn thuần, chưa có mục tiêu xe rõ ràng");
            sb.AppendLine("- product_lookup: hỏi giá, còn hàng, tồn kho, chi tiết của mẫu xe cụ thể");
            sb.AppendLine("- product_search: tìm/lọc xe theo hãng, giá, loại nhưng chưa hẳn là tư vấn chọn xe");
            sb.AppendLine("- recommend: hỏi tư vấn xe phù hợp theo nhu cầu");
            sb.AppendLine("- refine: đang lọc tiếp, đổi tiêu chí, thu hẹp hoặc mở rộng từ nhóm xe vừa gợi ý");
            sb.AppendLine("- compare: so sánh 2 mẫu xe, hoặc hỏi tiêu chí so sánh tiếp trên cặp xe đang so sánh");
            sb.AppendLine("- order_lookup: hỏi đơn hàng / trạng thái đơn / mã đơn");
            sb.AppendLine("- out_of_scope: ngoài phạm vi xe máy / đơn hàng / tư vấn mua xe");
            sb.AppendLine("- unknown: vẫn chưa đủ chắc để kết luận");
            sb.AppendLine();

            sb.AppendLine("Quy tắc hiểu hội thoại:");
            sb.AppendLine("- Nếu người dùng đổi ý rõ ràng như 'đổi ý', 'không phải ... nữa', 'giờ muốn ...', 'ý là muốn xem ...' => resetContext=true.");
            sb.AppendLine("- Nếu người dùng đang nói tiếp trên nhóm cũ hoặc đang sửa tiêu chí của nhóm cũ => isFollowUp=true.");
            sb.AppendLine("- Nếu ngữ cảnh trước đó là so sánh 2 mẫu và câu hiện tại hỏi thêm một tiêu chí so sánh => intentType=compare, isFollowUp=true.");
            sb.AppendLine("- Nếu ngữ cảnh trước đó là recommendation và câu hiện tại chỉ thêm/bớt điều kiện => intentType=refine, isFollowUp=true.");
            sb.AppendLine("- Nếu câu hiện tại hỏi rõ một mẫu xe cụ thể như giá, tồn kho, chi tiết => ưu tiên product_lookup.");
            sb.AppendLine("- Nếu người dùng chỉ chào hỏi đơn giản như 'chào shop', 'hello' => greeting.");
            sb.AppendLine("- Nếu câu quá mơ hồ, thiếu dữ kiện quan trọng thì shouldAskClarification=true và clarificationQuestion phải ngắn, tự nhiên, đúng trọng tâm.");
            sb.AppendLine("- Nếu không chắc, đặt confidence thấp thay vì đoán bừa.");
            sb.AppendLine();

            sb.AppendLine("Cách phân biệt recommend và product_search:");
            sb.AppendLine("- recommend: người dùng muốn được gợi ý xe phù hợp theo nhu cầu / hoàn cảnh / đối tượng / phong cách / mục đích sử dụng.");
            sb.AppendLine("- product_search: người dùng chỉ muốn lọc/tìm danh sách theo tiêu chí tương đối kỹ thuật như hãng, loại xe, tầm giá mà chưa có ý 'nhờ tư vấn chọn'.");
            sb.AppendLine();

            sb.AppendLine("Hãy trích xuất tối đa các ràng buộc nếu có:");
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

            sb.AppendLine("Giá trị priceFilterType hợp lệ nếu có: around | under | over | range | exact | null.");
            sb.AppendLine("Giá trị followUpType hợp lệ nếu có: restart | refine | expand | compare | lookup_followup | change_product | none.");
            sb.AppendLine("Giá trị action hợp lệ: None | FreshRecommendation | RefineRecommendation | ChangeProduct | ChangeBrand | ProductLookup | Compare | OrderLookup.");
            sb.AppendLine();
            sb.AppendLine("Quy tắc action:");
            sb.AppendLine("- Nếu người dùng nói kiểu 'đổi mẫu khác', 'mẫu khác xem', 'còn mẫu nào khác không', 'khác đi' trong ngữ cảnh tư vấn => action=ChangeProduct.");
            sb.AppendLine("- Với action=ChangeProduct: keepConstraints=true, excludePreviousProducts=true, excludePreviousBrands=false.");
            sb.AppendLine("- Không tự chọn xe mới trong JSON. Code phía sau sẽ tự loại xe cũ và gọi API lấy xe thật.");
            sb.AppendLine();

            sb.AppendLine("Schema JSON bắt buộc:");
            sb.AppendLine(@"
{
  ""intentType"": ""greeting | product_lookup | product_search | recommend | refine | compare | order_lookup | out_of_scope | unknown"",
  ""isFollowUp"": true,
  ""resetContext"": false,
  ""followUpType"": ""restart | refine | expand | compare | lookup_followup | none"",
""action"": ""None | FreshRecommendation | RefineRecommendation | ChangeProduct | ChangeBrand | ProductLookup | Compare | OrderLookup"",
""keepConstraints"": false,
""excludePreviousProducts"": false,
""excludePreviousBrands"": false,
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
            sb.AppendLine("Ví dụ 6:");
            sb.AppendLine(@"Input: ""cốp con nào rộng hơn""");
            sb.AppendLine(@"Ngữ cảnh: trước đó đang so sánh Honda Vision và Yamaha Latte.");
            sb.AppendLine(@"Output:");
            sb.AppendLine(@"{
  ""intentType"": ""compare"",
  ""isFollowUp"": true,
  ""resetContext"": false,
  ""followUpType"": ""compare"",
  ""reason"": ""đang hỏi thêm tiêu chí trên cặp xe đang so sánh"",
  ""isDirectLookup"": false,
  ""isFreshSearch"": false,
  ""confidence"": 0.96,
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
  ""wantsLargeStorage"": true,
  ""wantsEasyControl"": false,
  ""needsLowSeat"": false,
  ""heightCm"": null,
  ""mentionedProducts"": [],
  ""excludedBrands"": [],
  ""excludedCategories"": [],
  ""requestedStyles"": [],
  ""rejectedStyles"": [],
  ""comparisonFeature"": ""cốp rộng""
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
                sb.AppendLine($"- LastRecommendedProducts: {string.Join(", ", profile.LastRecommendedProducts ?? new List<string>())}");
                sb.AppendLine($"- LastComparedProducts: {string.Join(", ", profile.LastComparedProducts ?? new List<string>())}");
                sb.AppendLine($"- PriceMin: {profile.PriceMin}");
                sb.AppendLine($"- PriceMax: {profile.PriceMax}");
                sb.AppendLine($"- TargetPrice: {profile.TargetPrice}");
                sb.AppendLine($"- Target: {profile.Target}");
                sb.AppendLine($"- PreferredBrand: {profile.PreferredBrand}");
                sb.AppendLine($"- PreferredCategory: {profile.PreferredCategory}");
                sb.AppendLine($"- ForWork: {profile.ForWork}");
                sb.AppendLine($"- ForSchool: {profile.ForSchool}");
                sb.AppendLine($"- ForCity: {profile.ForCity}");
                sb.AppendLine($"- ForTour: {profile.ForTour}");
                sb.AppendLine($"- WantsFuelSaving: {profile.WantsFuelSaving}");
                sb.AppendLine($"- WantsLargeStorage: {profile.WantsLargeStorage}");
                sb.AppendLine($"- WantsEasyControl: {profile.WantsEasyControl}");
                sb.AppendLine($"- NeedsLowSeat: {profile.NeedsLowSeat}");
                sb.AppendLine($"- HeightCm: {profile.HeightCm}");
            }

            sb.AppendLine();
            sb.AppendLine($"Câu hiện tại của người dùng: \"{message}\"");

            return sb.ToString();
        }

        private static void Sanitize(LLMIntentResult parsed, string originalMessage, CustomerPreferenceProfile? profile)
        {
            parsed.IntentType = NormalizeIntentType(parsed.IntentType);
            parsed.FollowUpType = NormalizeFollowUpType(parsed.FollowUpType);
            parsed.Action = NormalizeConversationAction(parsed.Action);
            parsed.Reason = NormalizeText(parsed.Reason);
            parsed.ClarificationQuestion = NormalizeText(parsed.ClarificationQuestion);
            parsed.Brand = NormalizeText(parsed.Brand);
            parsed.Category = NormalizeText(parsed.Category);
            parsed.Target = NormalizeText(parsed.Target);
            parsed.PriceFilterType = NormalizePriceFilterType(parsed.PriceFilterType);
            parsed.ComparisonFeature = NormalizeText(parsed.ComparisonFeature);

            parsed.MentionedProducts = NormalizeList(parsed.MentionedProducts);
            parsed.ExcludedBrands = NormalizeList(parsed.ExcludedBrands);
            parsed.ExcludedCategories = NormalizeList(parsed.ExcludedCategories);
            parsed.RequestedStyles = NormalizeList(parsed.RequestedStyles);
            parsed.RejectedStyles = NormalizeList(parsed.RejectedStyles);

            if (parsed.Confidence < 0)
                parsed.Confidence = 0;

            if (parsed.Confidence > 1)
                parsed.Confidence = 1;

            if (parsed.HeightCm.HasValue && parsed.HeightCm.Value <= 0)
                parsed.HeightCm = null;

            if (parsed.PriceMin.HasValue && parsed.PriceMin.Value < 0)
                parsed.PriceMin = null;

            if (parsed.PriceMax.HasValue && parsed.PriceMax.Value < 0)
                parsed.PriceMax = null;

            if (parsed.TargetPrice.HasValue && parsed.TargetPrice.Value < 0)
                parsed.TargetPrice = null;

            if (parsed.PriceMin.HasValue && parsed.PriceMax.HasValue && parsed.PriceMin > parsed.PriceMax)
            {
                var temp = parsed.PriceMin;
                parsed.PriceMin = parsed.PriceMax;
                parsed.PriceMax = temp;
            }

            if (string.IsNullOrWhiteSpace(parsed.ClarificationQuestion))
                parsed.ShouldAskClarification = false;

            if (parsed.ShouldAskClarification && string.IsNullOrWhiteSpace(parsed.ClarificationQuestion))
            {
                parsed.ClarificationQuestion = "Bạn muốn mình ưu tiên thêm tiêu chí nào để lọc sát hơn?";
            }

            if (!parsed.IsFollowUp)
                parsed.FollowUpType = "none";

            if (parsed.ResetContext)
            {
                parsed.IsFollowUp = true;
                parsed.FollowUpType = "restart";
                parsed.IsFreshSearch = true;
            }

            if (parsed.IntentType.Equals("greeting", StringComparison.OrdinalIgnoreCase))
            {
                parsed.IsDirectLookup = false;
                parsed.IsFreshSearch = false;
                parsed.ShouldAskClarification = false;
                parsed.ClarificationQuestion = null;
            }

            if (parsed.IntentType.Equals("product_lookup", StringComparison.OrdinalIgnoreCase) &&
                parsed.MentionedProducts.Count == 0 &&
                string.IsNullOrWhiteSpace(parsed.Brand) &&
                string.IsNullOrWhiteSpace(parsed.Category))
            {
                parsed.Confidence = Math.Min(parsed.Confidence, 0.75);
            }

            var lowerMessage = originalMessage.Trim().ToLowerInvariant();
            if (LooksLikeBrandOnlyOrBrandAvailability(lowerMessage))
            {
                parsed.IntentType = "refine";
                parsed.IsFollowUp = true;
                parsed.FollowUpType = "refine";
                parsed.IsDirectLookup = false;
                parsed.IsFreshSearch = false;
                parsed.MentionedProducts.Clear();
                parsed.ComparisonFeature = null;
                parsed.Brand ??= ExtractBrandFromText(lowerMessage);
                parsed.Confidence = Math.Max(parsed.Confidence, 0.90);
            }
            if (LooksLikeGreeting(lowerMessage) &&
                parsed.IntentType.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                parsed.IntentType = "greeting";
                parsed.Confidence = Math.Max(parsed.Confidence, 0.85);
                parsed.IsFollowUp = false;
                parsed.FollowUpType = "none";
            }

            if (profile?.HasActiveCompareContext == true &&
                parsed.IntentType.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                LooksLikeCompareFollowUp(lowerMessage))
            {
                parsed.IntentType = "compare";
                parsed.IsFollowUp = true;
                parsed.FollowUpType = "compare";
                parsed.Confidence = Math.Max(parsed.Confidence, 0.82);
            }

            if (profile?.HasActiveRecommendationContext == true &&
                parsed.IntentType.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                LooksLikeRecommendationFollowUp(lowerMessage))
            {
                parsed.IntentType = "refine";
                parsed.IsFollowUp = true;
                parsed.FollowUpType = "refine";
                parsed.Confidence = Math.Max(parsed.Confidence, 0.80);
            }
            if (string.Equals(parsed.Action, "ChangeProduct", StringComparison.OrdinalIgnoreCase))
            {
                parsed.IntentType = "refine";
                parsed.IsFollowUp = true;
                parsed.FollowUpType = "change_product";
                parsed.IsDirectLookup = false;
                parsed.IsFreshSearch = false;
                parsed.KeepConstraints = true;
                parsed.ExcludePreviousProducts = true;
                parsed.ExcludePreviousBrands = false;
                parsed.ShouldAskClarification = false;
                parsed.ClarificationQuestion = null;
                parsed.Confidence = Math.Max(parsed.Confidence, 0.86);
            }
        }

        private static string NormalizeIntentType(string? intentType)
        {
            var value = NormalizeText(intentType)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            return AllowedIntentTypes.Contains(value) ? value : "unknown";
        }

        private static string NormalizeFollowUpType(string? followUpType)
        {
            var value = NormalizeText(followUpType)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return "none";

            return AllowedFollowUpTypes.Contains(value) ? value : "none";
        }
        private static string NormalizeConversationAction(string? action)
        {
            var value = NormalizeText(action);

            if (string.IsNullOrWhiteSpace(value))
                return "None";

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "None",
        "FreshRecommendation",
        "RefineRecommendation",
        "ChangeProduct",
        "ChangeBrand",
        "ProductLookup",
        "Compare",
        "OrderLookup"
    };

            return allowed.Contains(value) ? value : "None";
        }
        private static string? NormalizePriceFilterType(string? priceFilterType)
        {
            var value = NormalizeText(priceFilterType)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value switch
            {
                "around" or "under" or "over" or "range" or "exact" => value,
                "less_than" => "under",
                "greater_than" => "over",
                "between" => "range",
                _ => null
            };
        }

        private static string? NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim();
        }

        private static List<string> NormalizeList(List<string>? values)
        {
            if (values == null || values.Count == 0)
                return new List<string>();

            return values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool LooksLikeGreeting(string text)
        {
            return text == "chào" ||
                   text == "xin chào" ||
                   text == "hello" ||
                   text == "hi" ||
                   text == "chào shop" ||
                   text == "alo";
        }

        private static bool LooksLikeCompareFollowUp(string text)
        {
            return text.Contains("hơn") ||
                   text.Contains("so với") ||
                   text.Contains("so voi") ||
                   text.Contains("con nào") ||
                   text.Contains("loại nào") ||
                   text.Contains("mẫu nào") ||
                   text.Contains("cốp") ||
                   text.Contains("hao xăng") ||
                   text.Contains("tiết kiệm xăng") ||
                   text.Contains("ngồi") ||
                   text.Contains("động cơ") ||
                   text.Contains("máy") ||
                   text.Contains("êm") ||
                   text.Contains("mạnh");
        }

        private static bool LooksLikeRecommendationFollowUp(string text)
        {
            return text.Contains("còn") ||
                   text.Contains("rẻ hơn") ||
                   text.Contains("đắt hơn") ||
                   text.Contains("hãng") ||
                   text.Contains("honda") ||
                   text.Contains("yamaha") ||
                   text.Contains("suzuki") ||
                   text.Contains("sym") ||
                   text.Contains("piaggio") ||
                   text.Contains("xe ga") ||
                   text.Contains("xe số") ||
                   text.Contains("xe so") ||
                   text.Contains("đi làm") ||
                   text.Contains("đi học") ||
                   text.Contains("đi phố") ||
                   text.Contains("đi tour") ||
                   text.Contains("cốp rộng") ||
                   text.Contains("tiết kiệm xăng") ||
                   text.Contains("dễ đi") ||
                   text.Contains("thấp người") ||
                   text.Contains("đừng") ||
                   text.Contains("không thích");
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
        private static bool LooksLikeBrandOnlyOrBrandAvailability(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().ToLowerInvariant();

            string[] brands = { "honda", "yamaha", "suzuki", "sym", "piaggio" };

            if (brands.Contains(text))
                return true;

            return brands.Any(brand =>
                text == $"có {brand} không" ||
                text == $"co {brand} khong" ||
                text == $"còn {brand} không" ||
                text == $"con {brand} khong" ||
                text == $"{brand} thì sao" ||
                text == $"{brand} thi sao");
        }
        private static string? ExtractBrandFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            text = text.Trim().ToLowerInvariant();

            if (text.Contains("honda")) return "Honda";
            if (text.Contains("yamaha")) return "Yamaha";
            if (text.Contains("suzuki")) return "Suzuki";
            if (text.Contains("sym")) return "SYM";
            if (text.Contains("piaggio")) return "Piaggio";

            return null;
        }
    }
}
