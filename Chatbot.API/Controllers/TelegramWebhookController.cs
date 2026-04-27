using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Telegram;
using Chatbot.API.Services;
using Chatbot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Chatbot.API.Models.Responses;
using System.Text;
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
        private readonly IInputTextSanitizer _inputTextSanitizer;
        private readonly TelegramSettings _telegramSettings;
        private readonly ToolApiOptions _toolApiOptions;
        private readonly ILogger<TelegramWebhookController> _logger;
        private static readonly ConcurrentDictionary<long, byte> ProcessedUpdateIds = new();

        public TelegramWebhookController(
    IChatService chatService,
    ITelegramService telegramService,
    IConversationHistoryService historyService,
    IInputTextSanitizer inputTextSanitizer,
    ILogger<TelegramWebhookController> logger,
    IOptions<TelegramSettings> telegramSettings,
    IOptions<ToolApiOptions> toolApiOptions)
        {
            _chatService = chatService;
            _telegramService = telegramService;
            _historyService = historyService;
            _inputTextSanitizer = inputTextSanitizer;
            _logger = logger;
            _telegramSettings = telegramSettings.Value;
            _toolApiOptions = toolApiOptions.Value;
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
            var messageText = _inputTextSanitizer.Sanitize(update.Message.Text);

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
                    var telegramStaticReply = ChatChannelMessageHelper.FormatTelegramReply(
                        staticReply,
                        "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé.");

                    await _telegramService.SendMessageAsync(
                        chatId,
                        telegramStaticReply,
                        TelegramKeyboardFactory.MainMenu(),
                        "HTML");

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

                var telegramReply = ChatChannelMessageHelper.FormatTelegramReply(
                    chatResult?.Reply,
                    "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé.");

                var mergedTelegramReply = BuildMergedTelegramReply(telegramReply, chatResult?.Products);
                var mergedHistoryReply = BuildMergedHistoryReply(reply, chatResult?.Products);

                if (!string.IsNullOrWhiteSpace(reply))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        mergedTelegramReply,
                        TelegramKeyboardFactory.MainMenu(),
                        "HTML");

                    await SendProductCardsAsync(chatId, chatResult?.Products);

                    await SaveExchangeIfNeededAsync(
                        conversationId,
                        chatId.ToString(),
                        messageText,
                        mergedHistoryReply);
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

        private string? BuildProductUrl(ChatProductCard product, string? resolvedBaseUrl = null)
        {
            var baseUrl = resolvedBaseUrl ?? ResolvePublicWebBaseUrl();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return null;
            }

            var candidates = BuildProductPathCandidates(product);
            foreach (var relativePath in candidates)
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var combined = $"{baseUrl}{relativePath}";
                if (IsValidTelegramLinkUrl(combined))
                {
                    return combined;
                }
            }

            return null;
        }

        private string? ResolvePublicWebBaseUrl()
        {
            var candidates = new[]
            {
                _telegramSettings.PublicWebBaseUrl,
                _toolApiOptions.BaseUrl,
                ExtractOriginFromUrl(_telegramSettings.WebhookUrl)
            };

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var normalized = NormalizeOrigin(candidate);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (IsValidTelegramLinkUrl(normalized))
                {
                    return normalized;
                }
            }

            _logger.LogWarning(
                "No public web base URL available for Telegram product links. Set Telegram:PublicWebBaseUrl to a public domain.");

            return null;
        }

        private static string? NormalizeOrigin(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            return $"{uri.Scheme}://{uri.Authority}";
        }

        private static bool IsValidTelegramLinkUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractOriginFromUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            return $"{uri.Scheme}://{uri.Authority}";
        }

        private string BuildMergedTelegramReply(string telegramReply, IReadOnlyCollection<ChatProductCard>? products)
        {
            if (products == null || products.Count == 0)
            {
                return TrimToTelegramMessageLimit(telegramReply);
            }

            var lines = new List<string>
            {
                telegramReply.Trim(),
                string.Empty,
                "<b>Mình gửi kèm ảnh + giá + nút mở link chi tiết từng xe ngay dưới 👇</b>"
            };

            return TrimToTelegramMessageLimit(string.Join("\n", lines));
        }

        private string BuildMergedHistoryReply(string reply, IReadOnlyCollection<ChatProductCard>? products)
        {
            if (products == null || products.Count == 0)
            {
                return reply;
            }

            var visibleProducts = products.Take(5).ToList();
            var sb = new StringBuilder();

            sb.AppendLine(reply.Trim());
            sb.AppendLine();
            sb.AppendLine("Goi y san pham:");

            for (var i = 0; i < visibleProducts.Count; i++)
            {
                var product = visibleProducts[i];
                var productUrl = BuildProductUrl(product);
                var line = $"{i + 1}. {product.Ten} - {product.Gia:N0} VND (con {product.SoLuong})";

                if (!string.IsNullOrWhiteSpace(productUrl))
                {
                    line += $" - {productUrl}";
                }

                sb.AppendLine(line);
            }

            if (products.Count > visibleProducts.Count)
            {
                sb.AppendLine($"... va {products.Count - visibleProducts.Count} mau khac.");
            }

            return sb.ToString().Trim();
        }

        private static string TrimToTelegramMessageLimit(string value)
        {
            const int maxMessageLength = 3900;

            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();

            if (trimmed.Length <= maxMessageLength)
            {
                return trimmed;
            }

            return trimmed[..(maxMessageLength - 3)].TrimEnd() + "...";
        }

        private async Task SendProductCardsAsync(long chatId, IReadOnlyCollection<ChatProductCard>? products)
        {
            if (products == null || products.Count == 0)
            {
                return;
            }

            var baseUrl = ResolvePublicWebBaseUrl();
            var visibleProducts = products.ToList();

            foreach (var product in visibleProducts)
            {
                var productUrl = BuildProductUrl(product, baseUrl);
                var caption = BuildProductCardCaption(product, productUrl);
                var replyMarkup = BuildProductCardReplyMarkup(productUrl);
                var photoUrl = BuildTelegramPhotoUrl(product.ImageUrl, baseUrl);
                var sent = false;

                try
                {
                    if (!string.IsNullOrWhiteSpace(photoUrl))
                    {
                        await _telegramService.SendPhotoAsync(
                            chatId,
                            photoUrl,
                            caption,
                            replyMarkup,
                            "HTML");

                        sent = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to send Telegram product card. ProductId: {ProductId}, ProductName: {ProductName}",
                        product.Id,
                        product.Ten);
                }

                if (!sent)
                {
                    try
                    {
                        await _telegramService.SendMessageAsync(
                            chatId,
                            caption,
                            replyMarkup,
                            "HTML");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to send Telegram text fallback card. ProductId: {ProductId}, ProductName: {ProductName}",
                            product.Id,
                            product.Ten);
                    }
                }
            }
        }

        private static string BuildProductCardCaption(ChatProductCard product, string? productUrl)
        {
            var safeName = EscapeTelegramHtml(product.Ten);
            var lines = new List<string>
            {
                $"<b>{safeName}</b>",
                $"💰 Giá: {product.Gia:N0} VNĐ",
                $"📦 Còn hàng: {product.SoLuong}"
            };

            if (!string.IsNullOrWhiteSpace(productUrl))
            {
                lines.Add("🔗 Nhấn nút bên dưới để mở trang chi tiết");
            }

            return string.Join("\n", lines);
        }

        private static object? BuildProductCardReplyMarkup(string? productUrl)
        {
            if (string.IsNullOrWhiteSpace(productUrl))
            {
                return null;
            }

            return new
            {
                inline_keyboard = new[]
                {
                    new[]
                    {
                        new
                        {
                            text = "Xem chi tiết sản phẩm",
                            url = productUrl
                        }
                    }
                }
            };
        }

        private string? BuildTelegramPhotoUrl(string? imageUrl, string? resolvedBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            var trimmed = imageUrl.Trim();

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
                {
                    return null;
                }

                var relativePath = trimmed.StartsWith("/") ? trimmed : "/" + trimmed;
                var combined = $"{resolvedBaseUrl}{relativePath}";

                return CanSendPhotoUrlToTelegram(combined)
                    ? combined
                    : null;
            }

            if (CanSendPhotoUrlToTelegram(trimmed))
            {
                return trimmed;
            }

            if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
            {
                return null;
            }

            var rebuilt = $"{resolvedBaseUrl}{absoluteUri.PathAndQuery}";

            return CanSendPhotoUrlToTelegram(rebuilt)
                ? rebuilt
                : null;
        }

        private static IEnumerable<string> BuildProductPathCandidates(ChatProductCard product)
        {
            if (!string.IsNullOrWhiteSpace(product.Slug))
            {
                var encodedSlug = Uri.EscapeDataString(product.Slug.Trim());
                yield return $"/SanPham/Details?slug={encodedSlug}";
                yield return $"/san-pham/{encodedSlug}";
            }

            if (product.Id > 0)
            {
                yield return $"/SanPham/Details?id={product.Id}";
                yield return $"/SanPham/Details/{product.Id}";
                yield return $"/san-pham/{product.Id}";
            }
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