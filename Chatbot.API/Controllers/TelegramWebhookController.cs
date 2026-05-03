using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.Telegram;
using Chatbot.API.Services;
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
        private readonly IConversationHistoryService _historyService;
        private readonly IInputTextSanitizer _inputTextSanitizer;
        private readonly ILogger<TelegramWebhookController> _logger;
        private readonly TelegramSettings _telegramSettings;
        private readonly ToolApiOptions _toolApiOptions;

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
            var swTotal = Stopwatch.StartNew();

            try
            {
                CleanupProcessedUpdates();

                _logger.LogInformation("Telegram webhook hit. UpdateId: {UpdateId}", update?.UpdateId);
                Console.WriteLine("=== TELEGRAM WEBHOOK HIT ===");
                Console.WriteLine(JsonSerializer.Serialize(update));

                var secretHeader = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(_telegramSettings.SecretToken)
                    && !string.Equals(secretHeader, _telegramSettings.SecretToken, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Telegram webhook unauthorized due to secret token mismatch.");
                    return Unauthorized();
                }

                if (update?.Message?.Chat == null)
                {
                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] invalid_update => {swTotal.ElapsedMilliseconds} ms");
                    return Ok(new { success = false, step = "invalid_update" });
                }

                if (!TryMarkUpdateAsProcessed(update.UpdateId))
                {
                    _logger.LogInformation("Telegram duplicated update ignored. UpdateId: {UpdateId}", update.UpdateId);
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

                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] empty_text => {swTotal.ElapsedMilliseconds} ms");

                    return Ok(new { success = true });
                }

                var messageText = _inputTextSanitizer.Sanitize(rawMessageText);

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
                        $"telegram_{chatId}_main",
                        chatId.ToString(),
                        messageText,
                        ChatChannelMessageHelper.FormatReply(
                            staticReply,
                            "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé."));

                    swTotal.Stop();
                    Console.WriteLine($"[TELEGRAM TIMING] static_command => {swTotal.ElapsedMilliseconds} ms");

                    return Ok(new { success = true });
                }

                messageText = ChatChannelMessageHelper.NormalizeQuickMenuInput(messageText);

                try
                {
                    await _telegramService.SendTypingAsync(chatId);
                }
                catch (Exception exTyping)
                {
                    _logger.LogWarning(exTyping, "Failed to send Telegram typing action.");
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

                Console.WriteLine("=== TELEGRAM CHAT RESULT DEBUG ===");
                Console.WriteLine($"Success: {chatResult?.Success}");
                Console.WriteLine($"Reply: {chatResult?.Reply}");
                Console.WriteLine($"Error: {chatResult?.ErrorMessage}");
                Console.WriteLine($"ConversationId: {chatResult?.ConversationId}");
                Console.WriteLine($"Products count: {chatResult?.Products?.Count ?? 0}");

                var reply = ChatChannelMessageHelper.FormatReply(
                    chatResult?.Reply,
                    "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé.");

                var telegramReply = ChatChannelMessageHelper.FormatTelegramReply(
                    chatResult?.Reply,
                    "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé.");

                var mergedTelegramReply = BuildMergedTelegramReply(telegramReply, chatResult?.Products);
                var mergedHistoryReply = BuildMergedHistoryReply(reply, chatResult?.Products);

                if (!string.IsNullOrWhiteSpace(mergedTelegramReply))
                {
                    await _telegramService.SendMessageAsync(
                        chatId,
                        mergedTelegramReply,
                        TelegramKeyboardFactory.MainMenu(),
                        "HTML");
                }

                await SendProductCardsAsync(chatId, chatResult?.Products);

                await SaveExchangeIfNeededAsync(
                    conversationId,
                    chatId.ToString(),
                    messageText,
                    mergedHistoryReply);

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
                swTotal.Stop();

                _logger.LogError(ex, "Telegram webhook error while handling update.");
                Console.WriteLine($"[TELEGRAM TIMING] ERROR => {swTotal.ElapsedMilliseconds} ms");

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
                    _logger.LogWarning(exSendError, "Failed to send Telegram fallback error message.");
                }

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

            if (IsExplicitProductLookup(normalized))
            {
                var lookupKey = BuildLookupKey(normalized);
                return $"telegram_{chatId}_lookup_{lookupKey}";
            }

            return $"telegram_{chatId}_main";
        }

        private static bool IsExplicitProductLookup(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var hasLookupSignal = LookupSignals.Any(s => normalized.Contains(s));
            if (!hasLookupSignal)
            {
                return false;
            }

            if (GenericRecommendationKeywords.Any(k => normalized.Contains(k)))
            {
                return LooksLikeNamedProductQuery(normalized);
            }

            return LooksLikeNamedProductQuery(normalized);
        }

        private static bool LooksLikeNamedProductQuery(string normalized)
        {
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

            if (string.IsNullOrWhiteSpace(stripped))
            {
                return false;
            }

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
            {
                stripped = normalized;
            }

            stripped = stripped.Replace(" ", "_");
            stripped = Regex.Replace(stripped, @"[^a-z0-9_]", "");

            return string.IsNullOrWhiteSpace(stripped) ? "general" : stripped;
        }

        private static string NormalizeForIntent(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

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
                "<b>Mình gửi kèm ảnh, giá và nút mở chi tiết từng xe ngay dưới 👇</b>"
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
            sb.AppendLine("Gợi ý sản phẩm:");

            for (var i = 0; i < visibleProducts.Count; i++)
            {
                var product = visibleProducts[i];
                var productUrl = BuildProductUrl(product);
                var line = $"{i + 1}. {product.Ten} - {product.Gia:N0} VNĐ";

                if (product.SoLuong >= 0)
                {
                    line += $" - còn {product.SoLuong}";
                }

                if (!string.IsNullOrWhiteSpace(productUrl))
                {
                    line += $" - {productUrl}";
                }

                sb.AppendLine(line);
            }

            if (products.Count > visibleProducts.Count)
            {
                sb.AppendLine($"... và {products.Count - visibleProducts.Count} mẫu khác.");
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
            var visibleProducts = products.Take(5).ToList();

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
                        "Failed to send Telegram product photo card. ProductId: {ProductId}, ProductName: {ProductName}",
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
                            "Failed to send Telegram product text card. ProductId: {ProductId}, ProductName: {ProductName}",
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

        private string? BuildProductUrl(ChatProductCard product, string? resolvedBaseUrl = null)
        {
            var apiBaseUrl = NormalizeOrigin(_telegramSettings.WebhookUrl);

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(product.Slug))
            {
                var slug = Uri.EscapeDataString(product.Slug.Trim());
                return $"{apiBaseUrl}/api/telegram/product-detail?slug={slug}";
            }

            if (product.Id > 0)
            {
                return $"{apiBaseUrl}/api/telegram/product-detail?id={product.Id}";
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

                return CanSendPhotoUrlToTelegram(combined) ? combined : null;
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

            return CanSendPhotoUrlToTelegram(rebuilt) ? rebuilt : null;
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

        private static bool TryMarkUpdateAsProcessed(long updateId)
        {
            return ProcessedUpdateIds.TryAdd(updateId, DateTime.UtcNow);
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
        [HttpGet("product-detail")]
        public IActionResult ProductDetailRedirect([FromQuery] string? slug, [FromQuery] int? id)
        {
            var webBaseUrl = NormalizeOrigin(_telegramSettings.PublicWebBaseUrl)
                             ?? "https://localhost:7097";

            if (!string.IsNullOrWhiteSpace(slug))
            {
                return Redirect($"{webBaseUrl}/SanPham/Details?slug={Uri.EscapeDataString(slug.Trim())}");
            }

            if (id.HasValue && id.Value > 0)
            {
                return Redirect($"{webBaseUrl}/SanPham/Details?id={id.Value}");
            }

            return BadRequest("Thiếu thông tin sản phẩm.");
        }
    }
}