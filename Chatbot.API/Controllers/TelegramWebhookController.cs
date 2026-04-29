using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.Telegram;
using Chatbot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramWebhookController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ITelegramService _telegramService;
        private readonly TelegramSettings _telegramSettings;

        private static readonly ConcurrentDictionary<long, DateTime> ProcessedUpdateIds = new();
        private static readonly TimeSpan ProcessedUpdateRetention = TimeSpan.FromMinutes(10);

        private static readonly string[] GenericRecommendationKeywords =
        {
            "tư vấn", "goi y", "gợi ý", "chon xe", "chọn xe", "phu hop", "phù hợp",
            "xe cho", "xe ga", "xe so", "xe số", "duoi", "dưới", "tam", "tầm",
            "sinh vien", "sinh viên", "di hoc", "đi học", "di lam", "đi làm",
            "phai nu", "phái nữ", "cho nu", "cho nữ", "cop rong", "cốp rộng",
            "tiet kiem xang", "tiết kiệm xăng"
        };

        private static readonly string[] LookupSignals =
        {
            "gia bao nhieu", "giá bao nhiêu",
            "gia bn", "giá bn",
            "bao nhieu tien", "bao nhiêu tiền",
            "con hang khong", "còn hàng không",
            "con hang ko", "còn hàng ko",
            "con khong", "còn không",
            "gia", "giá"
        };

        public TelegramWebhookController(
            IChatService chatService,
            ITelegramService telegramService,
            IOptions<TelegramSettings> telegramSettings)
        {
            _chatService = chatService;
            _telegramService = telegramService;
            _telegramSettings = telegramSettings.Value;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveUpdate([FromBody] TelegramUpdate update)
        {
            var swTotal = Stopwatch.StartNew();

            Console.WriteLine("=== TELEGRAM WEBHOOK HIT ===");
            Console.WriteLine(JsonSerializer.Serialize(update));

            try
            {
                CleanupProcessedUpdates();

                var secretHeader = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(_telegramSettings.SecretToken))
                {
                    if (string.IsNullOrWhiteSpace(secretHeader) || secretHeader != _telegramSettings.SecretToken)
                    {
                        Console.WriteLine("Unauthorized: secret token mismatch");
                        return Unauthorized();
                    }
                }

                if (update?.Message?.Chat == null)
                {
                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] invalid update => {swTotal.ElapsedMilliseconds} ms");
                    return Ok(new { success = false, step = "invalid_update" });
                }

                if (ProcessedUpdateIds.ContainsKey(update.UpdateId))
                {
                    Console.WriteLine($"Duplicate update skipped: {update.UpdateId}");
                    return Ok(new { success = true, duplicated = true });
                }

                var chatId = update.Message.Chat.Id;
                var rawMessageText = update.Message.Text?.Trim();

                Console.WriteLine($"UpdateId: {update.UpdateId}");
                Console.WriteLine($"ChatId: {chatId}");
                Console.WriteLine($"MessageText: {rawMessageText}");

                if (string.IsNullOrWhiteSpace(rawMessageText))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        "Hiện tại mình mới hỗ trợ tin nhắn văn bản để tư vấn xe máy nhé.",
                        TelegramKeyboardFactory.MainMenu());

                    MarkProcessed(update.UpdateId);

                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] empty_text => {swTotal.ElapsedMilliseconds} ms");
                    return Ok(new { success = true });
                }

                if (rawMessageText.Equals("/start", StringComparison.OrdinalIgnoreCase))
                {
                    var welcome = """
Xin chào 👋
Mình là bot hỗ trợ tư vấn xe máy.

Mình có thể giúp bạn:
- Tư vấn chọn xe theo nhu cầu
- Gợi ý xe theo ngân sách
- Tra cứu giá xe
- Kiểm tra mẫu phù hợp

Bạn có thể chọn nhanh bằng menu bên dưới hoặc nhắn tự nhiên như:
- xe ga cho sinh viên
- xe cho nữ dưới 40 triệu
- air blade giá bao nhiêu
""";

                    await _telegramService.SendMessageAsync(
                        chatId,
                        welcome,
                        TelegramKeyboardFactory.MainMenu());

                    MarkProcessed(update.UpdateId);

                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] /start => {swTotal.ElapsedMilliseconds} ms");
                    return Ok(new { success = true });
                }

                if (rawMessageText.Equals("/help", StringComparison.OrdinalIgnoreCase))
                {
                    var help = """
Bạn có thể hỏi mình theo các cách sau:

- Tư vấn xe cho nữ tầm 35 triệu
- Xe ga nào hợp đi học
- Honda Vision giá bao nhiêu
- Xe nào phù hợp đi làm
- So sánh Vision và Janus

Gõ /menu để hiện lại menu nhanh.
""";

                    await _telegramService.SendMessageAsync(
                        chatId,
                        help,
                        TelegramKeyboardFactory.MainMenu());

                    MarkProcessed(update.UpdateId);

                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] /help => {swTotal.ElapsedMilliseconds} ms");
                    return Ok(new { success = true });
                }

                if (rawMessageText.Equals("/menu", StringComparison.OrdinalIgnoreCase))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        "Đây là menu nhanh, bạn chọn nội dung muốn tra cứu nhé.",
                        TelegramKeyboardFactory.MainMenu());

                    MarkProcessed(update.UpdateId);

                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] /menu => {swTotal.ElapsedMilliseconds} ms");
                    return Ok(new { success = true });
                }

                var messageText = NormalizeQuickMenu(rawMessageText);

                try
                {
                    await _telegramService.SendTypingAsync(chatId);
                }
                catch (Exception exTyping)
                {
                    Console.WriteLine("SendTyping failed:");
                    Console.WriteLine(exTyping);
                }

                var conversationId = ResolveConversationId(chatId, messageText);

                Console.WriteLine($"ResolvedConversationId: {conversationId}");

                var chatRequest = new ChatRequest
                {
                    ConversationId = conversationId,
                    Channel = "telegram",
                    UserId = chatId.ToString(),
                    Message = messageText
                };

                var swLogic = Stopwatch.StartNew();
                var chatResult = await _chatService.ProcessMessageAsync(chatRequest);
                swLogic.Stop();

                Console.WriteLine("=== TELEGRAM CHAT RESULT ===");
                Console.WriteLine("Reply: " + chatResult?.Reply);
                Console.WriteLine("Products count: " + (chatResult?.Products?.Count ?? 0));

                if (chatResult?.Products != null)
                {
                    foreach (var p in chatResult.Products)
                    {
                        Console.WriteLine($"Product: {p.Ten} | ImageUrl: {p.ImageUrl}");
                    }
                }

                var reply = string.IsNullOrWhiteSpace(chatResult?.Reply)
                    ? "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé."
                    : FormatTelegramReply(chatResult.Reply);

                if (!string.IsNullOrWhiteSpace(reply))
                {
                    Console.WriteLine("=== TELEGRAM SENDING MAIN REPLY ===");
                    Console.WriteLine(reply);

                    await _telegramService.SendMessageAsync(
                        chatId,
                        reply,
                        TelegramKeyboardFactory.MainMenu());
                }

                // Cố ý KHÔNG gửi ảnh/product trong luồng chính để:
                // 1) tránh ảnh localhost làm Telegram lỗi
                // 2) giữ Telegram timing ổn định
                // 3) giúp Telegram gần với web hơn ở phần phản hồi text chính
                //
                // Nếu muốn bật lại sau này, chỉ dùng URL public thật.

                MarkProcessed(update.UpdateId);

                swTotal.Stop();
                Console.WriteLine($"[TELEGRAM TIMING] LOGIC {rawMessageText} => {swLogic.ElapsedMilliseconds} ms");
                Console.WriteLine($"[TELEGRAM TIMING] TOTAL {rawMessageText} => {swTotal.ElapsedMilliseconds} ms");

                return Ok(new
                {
                    success = true,
                    logicMs = swLogic.ElapsedMilliseconds,
                    totalMs = swTotal.ElapsedMilliseconds,
                    conversationId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Webhook error:");
                Console.WriteLine(ex);

                try
                {
                    if (update?.Message?.Chat != null)
                    {
                        await _telegramService.SendMessageAsync(
                            update.Message.Chat.Id,
                            "Xin lỗi, hệ thống đang bận một chút. Bạn thử gửi lại sau ít phút nhé.",
                            TelegramKeyboardFactory.MainMenu());
                    }
                }
                catch (Exception exSendError)
                {
                    Console.WriteLine("Failed to send fallback error message:");
                    Console.WriteLine(exSendError);
                }

                swTotal.Stop();
                Console.WriteLine($"[TELEGRAM TIMING] ERROR => {swTotal.ElapsedMilliseconds} ms");

                return Ok(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        private static string ResolveConversationId(long chatId, string messageText)
        {
            var normalized = NormalizeForIntent(messageText);

            // Nhóm tra cứu cụ thể: dùng context riêng để không nhiễm state tư vấn cũ
            if (IsExplicitProductLookup(normalized))
            {
                var lookupKey = BuildLookupKey(normalized);
                return $"telegram_{chatId}_lookup_{lookupKey}";
            }

            // Các câu follow-up ngắn như "rẻ hơn chút", "dưới 40 triệu", "còn Honda thì sao"
            // cần giữ context chính
            return $"telegram_{chatId}_main";
        }

        private static bool IsExplicitProductLookup(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var hasLookupSignal = LookupSignals.Any(s => normalized.Contains(s));
            if (!hasLookupSignal)
                return false;

            // Nếu câu là tư vấn chung, không coi là lookup riêng
            if (GenericRecommendationKeywords.Any(k => normalized.Contains(k)))
            {
                // ngoại lệ: có tên xe cụ thể + giá/còn hàng
                return LooksLikeNamedProductQuery(normalized);
            }

            return LooksLikeNamedProductQuery(normalized);
        }

        private static bool LooksLikeNamedProductQuery(string normalized)
        {
            // loại bớt stopwords phổ biến để xem còn lại có giống tên sản phẩm không
            var stripped = normalized;

            var stopWords = new[]
            {
                "xe", "gia bao nhieu", "gia bn", "gia", "bao nhieu tien",
                "con hang khong", "con hang ko", "con khong", "co khong",
                "khong", "khong?", "bao nhieu", "hay", "la", "the", "nao"
            };

            foreach (var word in stopWords.OrderByDescending(x => x.Length))
            {
                stripped = stripped.Replace(word, " ");
            }

            stripped = Regex.Replace(stripped, @"\s+", " ").Trim();

            // Ví dụ còn lại: vision, air blade, winner x, janus...
            if (string.IsNullOrWhiteSpace(stripped))
                return false;

            var tokenCount = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            return tokenCount is >= 1 and <= 4;
        }

        private static string BuildLookupKey(string normalized)
        {
            var stripped = normalized;

            var removable = new[]
            {
                "gia bao nhieu", "gia bn", "bao nhieu tien", "con hang khong",
                "con hang ko", "con khong", "gia", "co khong", "xe"
            };

            foreach (var item in removable.OrderByDescending(x => x.Length))
            {
                stripped = stripped.Replace(item, " ");
            }

            stripped = Regex.Replace(stripped, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(stripped))
                stripped = normalized;

            stripped = stripped.Replace(" ", "_");
            stripped = Regex.Replace(stripped, @"[^a-z0-9_]", "");

            return string.IsNullOrWhiteSpace(stripped) ? "general" : stripped;
        }

        private static string NormalizeQuickMenu(string input)
        {
            return input switch
            {
                "Tư vấn xe" => "Tư vấn xe máy phù hợp cho tôi",
                "Xe ga" => "Gợi ý các mẫu xe ga phù hợp",
                "Xe số" => "Gợi ý các mẫu xe số phù hợp",
                "Xe cho nữ" => "Tư vấn xe máy phù hợp cho nữ",
                "Dưới 40 triệu" => "Tư vấn xe máy dưới 40 triệu",
                "Kiểm tra giá xe" => "Cho tôi biết giá các mẫu xe nổi bật",
                _ => input
            };
        }

        private static string FormatTelegramReply(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Mình chưa có câu trả lời phù hợp.";

            return text
                .Replace("\r\n", "\n")
                .Replace("VND", "VNĐ")
                .Trim();
        }

        private static string NormalizeForIntent(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var lower = input.ToLowerInvariant().Trim();
            var normalized = lower.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            var result = sb.ToString().Normalize(NormalizationForm.FormC);
            result = result.Replace('đ', 'd');

            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result;
        }

        private static void MarkProcessed(long updateId)
        {
            ProcessedUpdateIds[updateId] = DateTime.UtcNow;
        }

        private static void CleanupProcessedUpdates()
        {
            var cutoff = DateTime.UtcNow - ProcessedUpdateRetention;

            foreach (var item in ProcessedUpdateIds)
            {
                if (item.Value < cutoff)
                {
                    ProcessedUpdateIds.TryRemove(item.Key, out _);
                }
            }
        }
    }
}