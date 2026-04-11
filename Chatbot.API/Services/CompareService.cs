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
        private readonly ILogger<CompareService> _logger;

        public CompareService(
            IWebBanXeMayToolClient toolClient,
            IRagService ragService,
            ILogger<CompareService> logger)
        {
            _toolClient = toolClient;
            _ragService = ragService;
            _logger = logger;
        }

        public async Task<ChatResponse?> CompareAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var productNames = ResolveComparisonTargets(intent, profile);
            bool isFollowUpCompare =
    profile != null &&
    profile.HasActiveCompareContext &&
    profile.LastComparedProducts != null &&
    profile.LastComparedProducts.Count >= 2 &&
    (intent.MentionedProducts == null || intent.MentionedProducts.Count < 2);
            if (productNames.Count < 2)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Mình cần ít nhất 2 mẫu xe để so sánh. Bạn có thể nói rõ như \"Vision với Latte\" hoặc \"Freego với Air Blade\" nhé."
                };
            }

            var first = await FindBestMatchAsync(productNames[0]);
            var second = await FindBestMatchAsync(productNames[1]);

            if (first == null || second == null)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = "Mình chưa tìm đủ 2 mẫu xe phù hợp để so sánh từ dữ liệu hiện tại. Bạn thử ghi rõ tên mẫu xe hơn giúp mình nhé."
                };
            }

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

            var reply = BuildDeterministicCompareReply(
    first,
    second,
    intent,
    profile,
    ragContext,
    isFollowUpCompare);

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

        private static List<string> ResolveComparisonTargets(ParsedIntent intent, CustomerPreferenceProfile profile)
        {
            if (intent.MentionedProducts.Count >= 2)
            {
                return intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
            }

            // Ưu tiên context compare trước
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
    string? ragContext,
    bool isFollowUpCompare)
        {
            var feature = intent.ComparisonFeature ?? InferFeatureFromProfile(profile);

            var verdict = feature switch
            {
                "storage" => BuildStorageVerdict(first, second, isFollowUpCompare),
                "fuel_saving" => BuildFuelSavingVerdict(first, second, isFollowUpCompare),
                "low_seat" => BuildLowSeatVerdict(first, second, isFollowUpCompare),
                "female_fit" => BuildFemaleVerdict(first, second, isFollowUpCompare),
                "work_fit" => BuildWorkVerdict(first, second, isFollowUpCompare),
                "school_fit" => BuildSchoolVerdict(first, second, isFollowUpCompare),
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
        $"- **{first.Ten}**: giá {first.Gia:N0} VNĐ, còn {first.SoLuong} chiếc, thuộc nhóm {first.Loai}.",
        $"- **{second.Ten}**: giá {second.Gia:N0} VNĐ, còn {second.SoLuong} chiếc, thuộc nhóm {second.Loai}.",
        string.Empty,
        verdict
    };

            var shortHintFull = ExtractShortHintForFollowUp(ragContext, feature);
            if (!string.IsNullOrWhiteSpace(shortHintFull))
            {
                fullLines.Add(string.Empty);
                fullLines.Add(shortHintFull);
            }

            return string.Join("\n", fullLines).Trim();
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