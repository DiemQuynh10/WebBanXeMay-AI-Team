using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Requests;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ReplyRewriteService : IReplyRewriteService
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<ReplyRewriteService> _logger;
        private static readonly Regex PriceRegex =
    new(@"\d{1,3}(,\d{3})*\s*VNĐ", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public ReplyRewriteService(
            IOpenAIService openAIService,
            ILogger<ReplyRewriteService> logger)
        {
            _openAIService = openAIService;
            _logger = logger;
        }

        public async Task<string> RewriteAsync(
            string userMessage,
            string draftReply,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(draftReply))
                return draftReply;

            try
            {
                var prompt = BuildPrompt(userMessage, draftReply);

                var aiContext = new AIRequestContext
                {
                    ConversationId = Guid.NewGuid().ToString(),
                    Channel = "system",
                    UserId = "reply-rewriter",
                    OriginalUserMessage = userMessage,
                    EffectivePrompt = prompt,
                    RagContext = null
                };

                using var rewriteCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                rewriteCts.CancelAfter(TimeSpan.FromSeconds(10));

                var response = await _openAIService.AskAsync(aiContext, rewriteCts.Token);

                if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Reply))
                    return draftReply;

                var rewritten = Cleanup(response.Reply);

                if (!LooksSafe(draftReply, rewritten))
                    return draftReply;

                return rewritten;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reply rewrite failed. Fallback to draft reply.");
                return draftReply;
            }
        }

        private static string BuildPrompt(string userMessage, string draftReply)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Bạn là lớp viết lại câu trả lời cho chatbot tư vấn xe máy.");
            sb.AppendLine("Mục tiêu: viết lại câu trả lời tự nhiên như người tư vấn thật, nói chuyện mượt, bớt máy móc.");
            sb.AppendLine();

            sb.AppendLine("QUY TẮC BẮT BUỘC:");
            sb.AppendLine("- KHÔNG được thay đổi dữ kiện (tên xe, giá, số lượng).");
            sb.AppendLine("- KHÔNG thêm sản phẩm mới.");
            sb.AppendLine("- KHÔNG bỏ sản phẩm có sẵn.");
            sb.AppendLine("- KHÔNG đổi giá.");
            sb.AppendLine("- KHÔNG thêm thông tin không có trong bản nháp.");
            sb.AppendLine("- KHÔNG làm sai ý nghĩa gợi ý.");
            sb.AppendLine("- Không được cường điệu, quảng cáo quá mức, hoặc dùng giọng bán hàng.");
            sb.AppendLine("- Không tự thêm các nhận định mạnh như 'tuyệt vời', 'rất phù hợp', 'được nhiều người tin dùng' nếu bản nháp không có.");
            sb.AppendLine("- Giữ nguyên số lượng dòng sản phẩm đang liệt kê.");
            sb.AppendLine();

            sb.AppendLine("CÁCH VIẾT LẠI:");
            sb.AppendLine("- Viết như người thật đang tư vấn (tự nhiên, mềm hơn).");
            sb.AppendLine("- Tránh lặp cấu trúc 'mẫu này...', 'lựa chọn này...'");
            sb.AppendLine("- Có thể gộp câu, đổi cách diễn đạt.");
            sb.AppendLine("- Giữ nội dung ngắn gọn, dễ đọc.");
            sb.AppendLine("- Nếu bản nháp có danh sách sản phẩm, hãy giữ dạng danh sách rõ ràng.");
            sb.AppendLine("- Không được cường điệu, quảng cáo quá mức, hoặc dùng giọng bán hàng.");
            sb.AppendLine("- Không tự thêm các nhận định mạnh như 'tuyệt vời', 'rất phù hợp', 'được nhiều người tin dùng' nếu bản nháp không có.");
            sb.AppendLine();

            sb.AppendLine($"User: {userMessage}");
            sb.AppendLine();
            sb.AppendLine("Bản nháp:");
            sb.AppendLine(draftReply);
            sb.AppendLine();
            sb.AppendLine("Viết lại câu trả lời:");

            return sb.ToString();
        }
        private static string Cleanup(string text)
        {
            return (text ?? string.Empty)
                .Replace("```", "")
                .Trim();
        }

        private static bool LooksSafe(string draftReply, string rewritten)
        {
            if (string.IsNullOrWhiteSpace(rewritten))
                return false;

            if (rewritten.Length > draftReply.Length * 1.8)
                return false;

            if (rewritten.Length < Math.Max(20, draftReply.Length * 0.35))
                return false;

            if (!PreservesImportantPrices(draftReply, rewritten))
                return false;

            return true;
        }
        private static bool PreservesImportantPrices(string draftReply, string rewritten)
        {
            var draftPrices = PriceRegex.Matches(draftReply)
                .Select(m => m.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rewrittenPrices = PriceRegex.Matches(rewritten)
                .Select(m => m.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return draftPrices.All(p => rewrittenPrices.Contains(p));
        }
    }
}