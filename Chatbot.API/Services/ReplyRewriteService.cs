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

        private static readonly Regex BulletLineRegex =
            new(@"^\s*[-•]\s+", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ProductNameRegex =
    new(@"\b(Honda|Yamaha|Suzuki|Piaggio|SYM)\s+[A-Za-z0-9\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

            if (ShouldSkipRewrite(draftReply))
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
                rewriteCts.CancelAfter(TimeSpan.FromSeconds(8));

                var response = await _openAIService.AskAsync(aiContext, rewriteCts.Token);

                if (response == null || !response.Success || string.IsNullOrWhiteSpace(response.Reply))
                    return draftReply;

                var rewritten = Cleanup(response.Reply);
                if (string.IsNullOrWhiteSpace(rewritten) || rewritten.Length < Math.Min(20, draftReply.Length / 3))
                    return draftReply;
                if (!LooksSafe(draftReply, rewritten))
                    return draftReply;

                return rewritten;
            }
            catch (OperationCanceledException)
            {
                return draftReply;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reply rewrite failed. Fallback to draft reply.");
                return draftReply;
            }
        }

        private static bool ShouldSkipRewrite(string draftReply)
        {
            if (string.IsNullOrWhiteSpace(draftReply))
                return true;

            var text = draftReply.Trim();
            if (text.Contains("hiện có giá khoảng", StringComparison.OrdinalIgnoreCase) ||
    text.Contains("hiện vẫn còn hàng", StringComparison.OrdinalIgnoreCase) ||
    text.Contains("hiện đang hết hàng", StringComparison.OrdinalIgnoreCase) ||
    text.Contains("Số lượng trong hệ thống", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (text.Length < 60)
                return true;

            // Nếu là câu rất ngắn gọn, no-match hoặc ngoài miền, thường không cần rewrite
            if (text.StartsWith("Mình hiện chỉ hỗ trợ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Mình chưa hiểu ý bạn", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Hiện chưa có mẫu nào", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Trong nhóm mình vừa gợi ý, hiện chưa", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Nếu chỉ có 1-2 dòng rất ngắn, rewrite thường không mang lại nhiều giá trị
            var lineCount = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (lineCount <= 2 && text.Length < 140)
                return true;

            return false;
        }
        private static string BuildPrompt(string userMessage, string draftReply)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Bạn là lớp viết lại câu trả lời cho chatbot tư vấn xe máy.");
            sb.AppendLine("Nhiệm vụ của bạn chỉ là làm cho câu trả lời tự nhiên, gọn, mềm và giống người thật hơn.");
            sb.AppendLine();
            var replyType = DetectReplyType(draftReply);

            sb.AppendLine($"Loại bản nháp hiện tại: {replyType}");
            sb.AppendLine("Hãy giữ đúng kiểu trả lời của bản nháp, không tự đổi sang kiểu khác.");
            sb.AppendLine();

            sb.AppendLine("QUY TẮC BẮT BUỘC:");
            sb.AppendLine("- Chỉ viết lại câu chữ, KHÔNG thay đổi nội dung thực tế.");
            sb.AppendLine("- KHÔNG đổi tên xe, giá, số lượng, hãng, loại xe hoặc bất kỳ dữ kiện nào.");
            sb.AppendLine("- KHÔNG thêm sản phẩm mới, KHÔNG bỏ bớt sản phẩm đang có.");
            sb.AppendLine("- Nếu bản nháp có danh sách gạch đầu dòng, giữ nguyên đúng số dòng sản phẩm.");
            sb.AppendLine("- Giữ nguyên thứ tự các dòng sản phẩm đang có.");
            sb.AppendLine("- Không biến câu refinement thành so sánh, không biến câu so sánh thành recommendation.");
            sb.AppendLine("- Nếu bản nháp đã có câu mở đầu đúng trọng tâm thì chỉ làm mềm câu chữ, không đổi ý chính.");
            sb.AppendLine("- KHÔNG thêm lời quảng cáo, không nói quá, không thêm nhận định không có trong bản nháp.");
            sb.AppendLine("- KHÔNG dùng các từ như: 'tuyệt vời', 'siêu tốt', 'rất đáng mua', 'được nhiều người tin dùng' nếu bản nháp không có.");
            sb.AppendLine("- KHÔNG thêm cảm nhận chủ quan như 'êm ái', 'bốc', 'đầm', 'sướng'.");
            sb.AppendLine("- Nếu không thể viết lại mà vẫn giữ nguyên dữ kiện, hãy trả lại gần như nguyên văn bản nháp.");
            sb.AppendLine("- Giữ định dạng xuống dòng của bản nháp, đặc biệt là danh sách gạch đầu dòng.");
            sb.AppendLine("- Không gộp các dòng sản phẩm vào cùng một đoạn văn.");
            sb.AppendLine("- Không xóa dòng trống giữa phần mở đầu, danh sách sản phẩm và câu kết nếu bản nháp có.");
            sb.AppendLine();

            sb.AppendLine("CÁCH VIẾT:");
            sb.AppendLine("- Viết tự nhiên như người tư vấn thật.");
            sb.AppendLine("- Luôn xưng là 'mình', không dùng 'tôi'.");
            sb.AppendLine("- Tránh các cụm máy móc như 'lọc', 'xét riêng nhóm', 'bộ lọc' nếu có thể đổi tự nhiên hơn.");
            sb.AppendLine("- Không lặp lại tiêu chí user đã nói quá nhiều, ví dụ user hỏi xe số thì không cần nhắc 'ưu tiên xe số' ở từng dòng.");
            sb.AppendLine("- Ưu tiên câu ngắn, rõ, dễ đọc.");
            sb.AppendLine("- Tránh lặp các khung như 'mẫu này', 'lựa chọn này' quá nhiều.");
            sb.AppendLine("- Nếu user đang hỏi tiếp theo ngữ cảnh, giọng văn nên nối mạch tự nhiên.");
            sb.AppendLine("- Không cần quá văn vẻ. Không được bịa thêm ý.");
            sb.AppendLine();

            sb.AppendLine($"Tin nhắn người dùng: {userMessage}");
            sb.AppendLine();
            sb.AppendLine("Bản nháp gốc:");
            sb.AppendLine(draftReply);
            sb.AppendLine();
            sb.AppendLine("Hãy viết lại đúng một phiên bản trả lời cuối cùng, không giải thích thêm.");

            return sb.ToString();
        }

        private static string Cleanup(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var cleaned = text
                .Replace("```", string.Empty)
                .Trim();

            cleaned = Regex.Replace(cleaned, @"^\s*(Câu trả lời|Trả lời|Viết lại câu trả lời)\s*:\s*", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
            cleaned = Regex.Replace(cleaned, @"(?<!\n)(-\s+)", "\n$1");
            cleaned = Regex.Replace(cleaned, @"(?<=[.!?])\s+(?=-\s)", "\n");
            cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
            cleaned = cleaned.Replace(" tôi ", " mình ");
            cleaned = cleaned.Replace(" Tôi ", " Mình ");
            cleaned = cleaned.Replace("Tôi ", "Mình ");
            cleaned = cleaned.Replace(" tôi.", " mình.");
            cleaned = cleaned.Replace(" tôi,", " mình,");
            return cleaned.Trim();
        }

        private static bool LooksSafe(string draftReply, string rewritten)
        {
            if (string.IsNullOrWhiteSpace(rewritten))
                return false;

            if (string.Equals(draftReply.Trim(), rewritten.Trim(), StringComparison.Ordinal))
                return true;

            if (rewritten.Length > draftReply.Length * 1.8)
                return false;

            if (rewritten.Length < Math.Max(20, (int)(draftReply.Length * 0.35)))
                return false;

            if (!PreservesImportantPrices(draftReply, rewritten))
                return false;

            if (!PreservesBulletStructure(draftReply, rewritten))
                return false;

            if (!PreservesLikelyProductLines(draftReply, rewritten))
                return false;
            if (!PreservesProductNames(draftReply, rewritten))
    return false;
            if (DetectReplyType(draftReply) != "general" &&
    DetectReplyType(rewritten) != "general" &&
    !string.Equals(DetectReplyType(draftReply), DetectReplyType(rewritten), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        private static bool PreservesImportantPrices(string draftReply, string rewritten)
        {
            var draftPrices = PriceRegex.Matches(draftReply)
                .Select(m => NormalizeSpaces(m.Value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rewrittenPrices = PriceRegex.Matches(rewritten)
                .Select(m => NormalizeSpaces(m.Value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return draftPrices.All(p => rewrittenPrices.Contains(p));
        }

        private static bool PreservesBulletStructure(string draftReply, string rewritten)
        {
            var draftBulletCount = BulletLineRegex.Matches(draftReply).Count;
            var rewrittenBulletCount = BulletLineRegex.Matches(rewritten).Count;

            if (draftBulletCount == 0)
                return true;

            return draftBulletCount == rewrittenBulletCount;
        }

        private static bool PreservesLikelyProductLines(string draftReply, string rewritten)
        {

            var draftLines = SplitLines(draftReply);
            var rewrittenText = NormalizeSpaces(rewritten).ToLowerInvariant();

            var likelyProductLines = draftLines
                .Where(IsLikelyProductLine)
                .ToList();

            if (likelyProductLines.Count == 0)
                return true;

            foreach (var line in likelyProductLines)
            {
                var productName = ExtractProductName(line);
                if (string.IsNullOrWhiteSpace(productName))
                    continue;

                if (!rewrittenText.Contains(NormalizeSpaces(productName).ToLowerInvariant()))
                    return false;
            }

            return true;
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static bool IsLikelyProductLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (!line.StartsWith("-"))
                return false;

            if (!line.Contains(':'))
                return false;

            return PriceRegex.IsMatch(line) || line.Contains("VNĐ", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractProductName(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var cleaned = line.Trim().TrimStart('-', '•').Trim();
            var colonIndex = cleaned.IndexOf(':');
            if (colonIndex <= 0)
                return cleaned;

            return cleaned[..colonIndex].Trim();
        }

        private static string NormalizeSpaces(string text)
        {
            return Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
        }
        private static string DetectReplyType(string draftReply)
        {
            if (string.IsNullOrWhiteSpace(draftReply))
                return "unknown";

            var text = draftReply.Trim();

            if (text.Contains("Mình so sánh nhanh", StringComparison.OrdinalIgnoreCase))
                return "compare";

            if (text.Contains("Trong nhóm mình vừa gợi ý", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Trong các mẫu vừa rồi", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Nếu xét trong nhóm", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Nếu chọn trong nhóm", StringComparison.OrdinalIgnoreCase))
                return "refinement";

            if (text.Contains("Mình thấy", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Trong tầm", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Hiện mình nghiêng hơn về", StringComparison.OrdinalIgnoreCase))
                return "recommendation";

            if (text.Contains("hiện chưa có mẫu nào", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("chưa còn mẫu nào", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("khá ít lựa chọn", StringComparison.OrdinalIgnoreCase))
                return "no_match";

            return "general";
        }
        private static bool PreservesProductNames(string draftReply, string rewritten)
        {
            var draftNames = ProductNameRegex.Matches(draftReply)
                .Select(m => NormalizeSpaces(m.Value).Trim('.', ',', ':', ';'))
                .Where(x => x.Length >= 6)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (draftNames.Count == 0)
                return true;

            var rewrittenText = NormalizeSpaces(rewritten).ToLowerInvariant();

            return draftNames.All(name =>
                rewrittenText.Contains(name.ToLowerInvariant()));
        }
    }
}
