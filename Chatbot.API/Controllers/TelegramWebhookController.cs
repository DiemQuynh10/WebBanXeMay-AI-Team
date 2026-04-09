using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Telegram;
using Chatbot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Chatbot.API.Models.Responses;
using System.Text.Json;
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
        private static readonly HashSet<long> ProcessedUpdateIds = new();

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
            Console.WriteLine("=== TELEGRAM WEBHOOK HIT ===");
            Console.WriteLine(JsonSerializer.Serialize(update));

            var secretHeader = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(secretHeader) || secretHeader != _telegramSettings.SecretToken)
            {
                Console.WriteLine("Unauthorized: secret token mismatch");
                return Unauthorized();
            }

            if (update == null || update.Message == null || update.Message.Chat == null)
            {
                return Ok(new { success = false, step = "invalid update" });
            }

            if (ProcessedUpdateIds.Contains(update.UpdateId))
            {
                return Ok(new { success = true, duplicated = true });
            }

            ProcessedUpdateIds.Add(update.UpdateId);

            var chatId = update.Message.Chat.Id;
            var messageText = update.Message.Text?.Trim();

            try
            {
                if (string.IsNullOrWhiteSpace(messageText))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        "Hiện tại mình mới hỗ trợ tin nhắn văn bản để tư vấn xe máy nhé.",
                        TelegramKeyboardFactory.MainMenu());
                    return Ok(new { success = true });
                }

                if (messageText.Equals("/start", StringComparison.OrdinalIgnoreCase))
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
     TelegramKeyboardFactory.MainMenu()
 );

                    return Ok(new { success = true });
                }

                if (messageText.Equals("/help", StringComparison.OrdinalIgnoreCase))
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

                    await _telegramService.SendMessageAsync(chatId, help, TelegramKeyboardFactory.MainMenu());
                    return Ok(new { success = true });
                }

                if (messageText.Equals("/menu", StringComparison.OrdinalIgnoreCase))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        "Đây là menu nhanh, bạn chọn nội dung muốn tra cứu nhé.",
                        TelegramKeyboardFactory.MainMenu());
                    return Ok(new { success = true });
                }

                messageText = NormalizeQuickMenu(messageText);

                await _telegramService.SendTypingAsync(chatId);

                var conversationId = $"telegram_{chatId}";

                var chatRequest = new ChatRequest
                {
                    ConversationId = conversationId,
                    Channel = "telegram",
                    UserId = chatId.ToString(),
                    Message = messageText
                };

                var chatResult = await _chatService.ProcessMessageAsync(chatRequest);

                var reply = string.IsNullOrWhiteSpace(chatResult?.Reply)
                    ? "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé."
                    : FormatTelegramReply(chatResult.Reply);

                if (!string.IsNullOrWhiteSpace(reply))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        reply,
                        TelegramKeyboardFactory.MainMenu());
                }

                if (chatResult?.Products != null && chatResult.Products.Any())
                {
                    foreach (var product in chatResult.Products)
                    {
                        var finalImageUrl = NormalizeImageUrl(product.ImageUrl);
                        var caption = BuildProductCaption(product);

                        if (!string.IsNullOrWhiteSpace(finalImageUrl))
                        {
                            await _telegramService.SendPhotoAsync(
                                chatId,
                                finalImageUrl,
                                caption,
                                null,
                                "HTML");
                        }
                        else
                        {
                            await _telegramService.SendMessageAsync(
                                chatId,
                                caption,
                                null,
                                "HTML");
                        }
                    }
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Webhook error:");
                Console.WriteLine(ex);

                try
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        "Xin lỗi, hệ thống đang bận một chút. Bạn thử gửi lại sau ít phút nhé.",
                        TelegramKeyboardFactory.MainMenu());
                }
                catch
                {
                }

                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
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

            return text.Replace("\r\n", "\n").Trim();
        }
        private static string NormalizeImageUrl(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return string.Empty;

            return imageUrl.Trim();
        }

        private static string BuildProductCaption(ChatProductCard product)
        {
            var lines = new List<string>
    {
        $"<b>{product.Ten}</b>",
        $"💰 Giá: {product.Gia:N0} VNĐ",
        $"📦 Còn hàng: {product.SoLuong}"
    };

            if (!string.IsNullOrWhiteSpace(product.ThuongHieu))
            {
                lines.Add($"🏷️ Hãng: {product.ThuongHieu}");
            }

            if (!string.IsNullOrWhiteSpace(product.Loai))
            {
                lines.Add($"🛵 Loại: {product.Loai}");
            }

            if (!string.IsNullOrWhiteSpace(product.CC))
            {
                lines.Add($"⚙️ Phân khối: {product.CC}");
            }

            return string.Join("\n", lines);
        }
    }
}