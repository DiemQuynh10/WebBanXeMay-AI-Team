using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Telegram;
using Chatbot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Chatbot.API.Models.Responses;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramWebhookController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly ITelegramService _telegramService;
        private readonly IConversationHistoryService _historyService;
        private readonly TelegramSettings _telegramSettings;
        private readonly ILogger<TelegramWebhookController> _logger;
        private static readonly ConcurrentDictionary<long, byte> ProcessedUpdateIds = new();

        public TelegramWebhookController(
    IChatService chatService,
    ITelegramService telegramService,
    IConversationHistoryService historyService,
    ILogger<TelegramWebhookController> logger,
    IOptions<TelegramSettings> telegramSettings)
        {
            _chatService = chatService;
            _telegramService = telegramService;
            _historyService = historyService;
            _logger = logger;
            _telegramSettings = telegramSettings.Value;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> ReceiveUpdate([FromBody] TelegramUpdate update)
        {
            _logger.LogInformation("Telegram webhook hit. UpdateId: {UpdateId}", update?.UpdateId);

            var secretHeader = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(_telegramSettings.SecretToken)
                && !string.Equals(secretHeader, _telegramSettings.SecretToken, StringComparison.Ordinal))
            {
                _logger.LogWarning("Telegram webhook unauthorized due to secret token mismatch.");
                return Unauthorized();
            }

            if (update == null || update.Message == null || update.Message.Chat == null)
            {
                return Ok(new { success = false, step = "invalid update" });
            }

            if (!TryMarkUpdateAsProcessed(update.UpdateId))
            {
                _logger.LogInformation("Telegram duplicated update ignored. UpdateId: {UpdateId}", update.UpdateId);
                return Ok(new { success = true, duplicated = true });
            }

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

                    _logger.LogInformation("Telegram non-text message handled. ChatId: {ChatId}", chatId);
                    return Ok(new { success = true });
                }

                if (ChatChannelMessageHelper.TryGetStaticCommandReply(messageText, out var staticReply))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        staticReply,
                        TelegramKeyboardFactory.MainMenu());

                    await SaveExchangeIfNeededAsync(
                        $"telegram_{chatId}",
                        chatId.ToString(),
                        messageText,
                        ChatChannelMessageHelper.FormatReply(
                            staticReply,
                            "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé."));

                    _logger.LogInformation(
                        "Telegram static command handled. ChatId: {ChatId}, Message: {Message}",
                        chatId,
                        messageText);

                    return Ok(new { success = true });
                }

                messageText = ChatChannelMessageHelper.NormalizeQuickMenuInput(messageText);

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

                var reply = ChatChannelMessageHelper.FormatReply(
                    chatResult?.Reply,
                    "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé.");

                if (!string.IsNullOrWhiteSpace(reply))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        reply,
                        TelegramKeyboardFactory.MainMenu());

                    await SaveExchangeIfNeededAsync(
                        conversationId,
                        chatId.ToString(),
                        messageText,
                        reply);
                }

                if (chatResult?.Products != null && chatResult.Products.Any())
                {
                    foreach (var product in chatResult.Products)
                    {
                        var finalImageUrl = NormalizeImageUrl(product.ImageUrl);
                        var caption = BuildProductCaption(product);

                        if (CanSendPhotoUrlToTelegram(finalImageUrl))
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
                            if (!string.IsNullOrWhiteSpace(finalImageUrl))
                            {
                                _logger.LogWarning(
                                    "Telegram photo URL is not publicly reachable, fallback to text. ChatId: {ChatId}, Url: {Url}",
                                    chatId,
                                    finalImageUrl);
                            }

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
                _logger.LogError(ex, "Telegram webhook error while handling update.");

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

        private static string NormalizeImageUrl(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return string.Empty;

            return imageUrl.Trim();
        }

        private static string BuildProductCaption(ChatProductCard product)
        {
            var safeName = EscapeTelegramHtml(product.Ten);
            var safeBrand = EscapeTelegramHtml(product.ThuongHieu);
            var safeCategory = EscapeTelegramHtml(product.Loai);
            var safeCc = EscapeTelegramHtml(product.CC);

            var lines = new List<string>
    {
        $"<b>{safeName}</b>",
        $"💰 Giá: {product.Gia:N0} VNĐ",
        $"📦 Còn hàng: {product.SoLuong}"
    };

            if (!string.IsNullOrWhiteSpace(safeBrand))
            {
                lines.Add($"🏷️ Hãng: {safeBrand}");
            }

            if (!string.IsNullOrWhiteSpace(safeCategory))
            {
                lines.Add($"🛵 Loại: {safeCategory}");
            }

            if (!string.IsNullOrWhiteSpace(safeCc))
            {
                lines.Add($"⚙️ Phân khối: {safeCc}");
            }

            return string.Join("\n", lines);
        }

        private static bool TryMarkUpdateAsProcessed(long updateId)
        {
            if (ProcessedUpdateIds.Count > 10000)
            {
                ProcessedUpdateIds.Clear();
            }

            return ProcessedUpdateIds.TryAdd(updateId, 1);
        }

        private static bool CanSendPhotoUrlToTelegram(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return false;
            }

            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            if (uri.IsLoopback)
            {
                return false;
            }

            var host = uri.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IPAddress.TryParse(host, out var ipAddress))
            {
                var bytes = ipAddress.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    if (bytes[0] == 10 || bytes[0] == 127)
                    {
                        return false;
                    }

                    if (bytes[0] == 192 && bytes[1] == 168)
                    {
                        return false;
                    }

                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string EscapeTelegramHtml(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return WebUtility.HtmlEncode(value.Trim());
        }

        private async Task SaveExchangeIfNeededAsync(
            string conversationId,
            string userId,
            string userMessage,
            string botReply)
        {
            if (string.IsNullOrWhiteSpace(conversationId)
                || string.IsNullOrWhiteSpace(userMessage)
                || string.IsNullOrWhiteSpace(botReply))
            {
                return;
            }

            await _historyService.SaveExchangeAsync(
                conversationId.Trim(),
                "telegram",
                userId,
                userMessage.Trim(),
                botReply.Trim());
        }
    }
}