using System.Text;
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

            var reply = BuildDeterministicCompareReply(first, second, intent, profile, ragContext);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply
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
                return intent.MentionedProducts.Take(2).ToList();
            }

            if (profile.LastMentionedProducts.Count >= 2)
            {
                return profile.LastMentionedProducts.Take(2).ToList();
            }

            if (profile.LastRecommendedProducts.Count >= 2)
            {
                return profile.LastRecommendedProducts.Take(2).ToList();
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
            string? ragContext)
        {
            var lines = new List<string>
            {
                $"Mình so sánh nhanh **{first.Ten}** và **{second.Ten}** cho bạn:",
                string.Empty,
                $"- **{first.Ten}**: giá {first.Gia:N0} VNĐ, còn {first.SoLuong} chiếc, thuộc nhóm {first.Loai}.",
                $"- **{second.Ten}**: giá {second.Gia:N0} VNĐ, còn {second.SoLuong} chiếc, thuộc nhóm {second.Loai}.",
                string.Empty
            };

            var feature = intent.ComparisonFeature ?? InferFeatureFromProfile(profile);

            var verdict = feature switch
            {
                "storage" => BuildStorageVerdict(first, second),
                "fuel_saving" => BuildFuelSavingVerdict(first, second),
                "low_seat" => BuildLowSeatVerdict(first, second),
                "female_fit" => BuildFemaleVerdict(first, second),
                "work_fit" => BuildWorkVerdict(first, second),
                "school_fit" => BuildSchoolVerdict(first, second),
                _ => BuildGeneralVerdict(first, second)
            };

            lines.Add(verdict);

            if (!string.IsNullOrWhiteSpace(ragContext))
            {
                var shortHint = ExtractShortHint(ragContext);
                if (!string.IsNullOrWhiteSpace(shortHint))
                {
                    lines.Add(string.Empty);
                    lines.Add(shortHint);
                }
            }

            return string.Join("\n", lines).Trim();
        }

        private static string BuildStorageVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (ContainsAny(first.Ten, "Freego", "Latte", "Lead", "Address") &&
                !ContainsAny(second.Ten, "Freego", "Latte", "Lead", "Address"))
            {
                return $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{first.Ten}**.";
            }

            if (ContainsAny(second.Ten, "Freego", "Latte", "Lead", "Address") &&
                !ContainsAny(first.Ten, "Freego", "Latte", "Lead", "Address"))
            {
                return $"Nếu bạn ưu tiên **cốp rộng / tiện mang đồ** thì mình nghiêng hơn về **{second.Ten}**.";
            }

            return "Về tiêu chí **cốp rộng**, hai mẫu này cần cân nhắc thêm theo nhóm tiện ích thực tế, nhưng mình sẽ ưu tiên mẫu thiên về tính thực dụng hơn.";
        }

        private static string BuildFuelSavingVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (ContainsAny(first.Ten, "Vision", "Wave", "Sirius", "Future") &&
                !ContainsAny(second.Ten, "Vision", "Wave", "Sirius", "Future"))
            {
                return $"Nếu bạn ưu tiên **tiết kiệm xăng** thì **{first.Ten}** nhỉnh hơn để cân nhắc.";
            }

            if (ContainsAny(second.Ten, "Vision", "Wave", "Sirius", "Future") &&
                !ContainsAny(first.Ten, "Vision", "Wave", "Sirius", "Future"))
            {
                return $"Nếu bạn ưu tiên **tiết kiệm xăng** thì **{second.Ten}** nhỉnh hơn để cân nhắc.";
            }

            return "Nếu xét theo hướng **tiết kiệm xăng**, hai mẫu này không chênh quá rõ trên dữ liệu hiện có, nên bạn có thể chốt thêm theo kiểu dáng hoặc nhu cầu sử dụng.";
        }

        private static string BuildLowSeatVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (ContainsAny(first.Ten, "Zip", "Vision", "Latte") &&
                !ContainsAny(second.Ten, "Zip", "Vision", "Latte"))
            {
                return $"Nếu bạn ưu tiên **dễ chống chân / yên thấp** thì **{first.Ten}** hợp hơn.";
            }

            if (ContainsAny(second.Ten, "Zip", "Vision", "Latte") &&
                !ContainsAny(first.Ten, "Zip", "Vision", "Latte"))
            {
                return $"Nếu bạn ưu tiên **dễ chống chân / yên thấp** thì **{second.Ten}** hợp hơn.";
            }

            return "Về tiêu chí **dễ chống chân**, mình sẽ ưu tiên mẫu có dáng gọn hơn trong hai xe này.";
        }

        private static string BuildFemaleVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            bool firstIsFemaleFit = ContainsAny(first.Ten, "Latte", "Grande", "Vision", "Zip");
            bool secondIsFemaleFit = ContainsAny(second.Ten, "Latte", "Grande", "Vision", "Zip");

            if (first.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase))
            {
                return "Nếu ưu tiên **gọn, dễ đi và dễ làm quen** thì mình nghiêng về **Honda Vision**. " +
                       "Còn nếu ưu tiên **dáng mềm hơn và cảm giác hợp nữ hơn** thì **Yamaha Latte** nổi bật hơn. " +
                       "Nếu phải chốt nhanh theo hướng hợp nữ, mình sẽ nghiêng nhẹ về **Yamaha Latte**.";
            }

            if (first.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase) &&
                second.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                return "Nếu ưu tiên **dáng mềm hơn và cảm giác hợp nữ hơn** thì mình nghiêng về **Yamaha Latte**. " +
                       "Còn nếu ưu tiên **gọn, dễ đi và dễ làm quen** thì **Honda Vision** thực dụng hơn. " +
                       "Nếu phải chốt nhanh theo hướng hợp nữ, mình sẽ nghiêng nhẹ về **Yamaha Latte**.";
            }

            if (firstIsFemaleFit && !secondIsFemaleFit)
                return $"Nếu xét theo hướng **hợp nữ, dễ đi** thì mình nghiêng hơn về **{first.Ten}**.";

            if (secondIsFemaleFit && !firstIsFemaleFit)
                return $"Nếu xét theo hướng **hợp nữ, dễ đi** thì mình nghiêng hơn về **{second.Ten}**.";

            return $"Nếu chỉ chốt nhanh theo hướng **hợp nữ** thì mình nghiêng hơn về mẫu có dáng mềm và dễ làm quen hơn trong hai xe này.";
        }

        private static string BuildWorkVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (ContainsAny(first.Ten, "Air Blade", "Freego", "Future") &&
                !ContainsAny(second.Ten, "Air Blade", "Freego", "Future"))
            {
                return $"Nếu ưu tiên **đi làm hằng ngày** thì **{first.Ten}** hợp hơn.";
            }

            if (ContainsAny(second.Ten, "Air Blade", "Freego", "Future") &&
                !ContainsAny(first.Ten, "Air Blade", "Freego", "Future"))
            {
                return $"Nếu ưu tiên **đi làm hằng ngày** thì **{second.Ten}** hợp hơn.";
            }

            return "Nếu xét theo hướng **đi làm**, mình sẽ nghiêng về mẫu thực dụng và ổn định hơn trong hai xe này.";
        }

        private static string BuildSchoolVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (ContainsAny(first.Ten, "Vision", "Wave", "Sirius") &&
                !ContainsAny(second.Ten, "Vision", "Wave", "Sirius"))
            {
                return $"Nếu ưu tiên **đi học / sinh viên** thì **{first.Ten}** dễ cân nhắc hơn.";
            }

            if (ContainsAny(second.Ten, "Vision", "Wave", "Sirius") &&
                !ContainsAny(first.Ten, "Vision", "Wave", "Sirius"))
            {
                return $"Nếu ưu tiên **đi học / sinh viên** thì **{second.Ten}** dễ cân nhắc hơn.";
            }

            return "Nếu xét theo hướng **đi học**, mình sẽ ưu tiên mẫu dễ đi và chi phí dùng lâu dài hơn.";
        }

        private static string BuildGeneralVerdict(ProductSummaryDto first, ProductSummaryDto second)
        {
            if (first.Gia < second.Gia)
            {
                return $"Nếu bạn ưu tiên **giá mềm hơn** thì **{first.Ten}** lợi thế hơn; còn nếu muốn cân nhắc theo cảm giác xe hoặc tiện ích thì mình có thể lọc tiếp cho bạn.";
            }

            if (second.Gia < first.Gia)
            {
                return $"Nếu bạn ưu tiên **giá mềm hơn** thì **{second.Ten}** lợi thế hơn; còn nếu muốn cân nhắc theo cảm giác xe hoặc tiện ích thì mình có thể lọc tiếp cho bạn.";
            }

            return "Hai mẫu này đang khá ngang nhau ở dữ liệu cơ bản, nên nên chốt tiếp theo tiêu chí như cốp rộng, dễ chống chân, đi làm hay tiết kiệm xăng.";
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

        private static string? ExtractShortHint(string ragContext)
        {
            if (string.IsNullOrWhiteSpace(ragContext))
                return null;

            var lines = ragContext
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => x.Length > 20)
                .Take(1)
                .ToList();

            return lines.FirstOrDefault();
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
    }
}