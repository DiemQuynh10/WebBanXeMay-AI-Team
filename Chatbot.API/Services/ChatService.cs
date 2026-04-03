using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ChatService : IChatService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly ILogger<ChatService> _logger;
        private readonly IQueryNormalizationService _queryNormalizationService;
        private readonly IClarificationStateService _clarificationStateService;
        private readonly IRagService _ragService;
        private readonly IPriceIntentParser _priceIntentParser;
        private readonly IIntentParserService _intentParserService;
        private readonly IProductRecommendationService _productRecommendationService;

        public ChatService(IOpenAIService openAIService, IWebBanXeMayToolClient toolClient, ILogger<ChatService> logger, IQueryNormalizationService queryNormalizationService, IClarificationStateService clarificationStateService, IRagService ragService, IPriceIntentParser priceIntentParser, IIntentParserService intentParserService, IProductRecommendationService productRecommendationService)
        {
            _openAIService = openAIService;
            _toolClient = toolClient;
            _logger = logger;
            _queryNormalizationService = queryNormalizationService;
            _clarificationStateService = clarificationStateService;
            _ragService = ragService;
            _priceIntentParser = priceIntentParser;
            _intentParserService = intentParserService;
            _productRecommendationService = productRecommendationService;
        }

        public async Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (request == null)
                {
                    return new ChatResponse
                    {
                        Success = false,
                        Reply = string.Empty,
                        ErrorMessage = "Request không hợp lệ.",
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return new ChatResponse
                    {
                        Success = false,
                        Reply = string.Empty,
                        ErrorMessage = "Tin nhắn không được để trống.",
                        ConversationId = request.ConversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                    ? Guid.NewGuid().ToString()
                    : request.ConversationId.Trim();

                request.ConversationId = conversationId;

                var pendingClarification = _clarificationStateService.GetPending(conversationId);
                var originalMessage = request.Message.Trim();

                if (!string.IsNullOrWhiteSpace(pendingClarification))
                {
                    if (IsAffirmative(originalMessage))
                    {
                        _clarificationStateService.Clear(conversationId);
                        originalMessage = pendingClarification!;

                        _logger.LogInformation(
                            "User confirmed normalized message. ConversationId: {ConversationId}, Message: {Message}",
                            conversationId,
                            originalMessage);
                    }
                    else if (IsNegative(originalMessage))
                    {
                        _clarificationStateService.Clear(conversationId);

                        return new ChatResponse
                        {
                            Success = true,
                            Reply = "Bạn có thể nhập lại giúp mình câu hỏi rõ hơn được không?",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                    else
                    {
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = $"Mình đang chờ bạn xác nhận câu hỏi trước đó. Bạn muốn hỏi \"{pendingClarification}\" đúng không?",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                }

                var normalizationResult = _queryNormalizationService.Analyze(originalMessage);

                _logger.LogInformation(
                    "Normalization result. Original: {OriginalMessage}, Normalized: {NormalizedMessage}, NeedsConfirmation: {NeedsConfirmation}",
                    normalizationResult.OriginalText,
                    normalizationResult.NormalizedText,
                    normalizationResult.NeedsConfirmation);

                if (normalizationResult.NeedsConfirmation)
                {
                    _clarificationStateService.SetPending(conversationId, normalizationResult.NormalizedText);

                    return new ChatResponse
                    {
                        Success = true,
                        Reply = $"Bạn muốn hỏi \"{normalizationResult.NormalizedText}\" đúng không?",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                        DebugInfo = new
                        {
                            original = normalizationResult.OriginalText,
                            normalized = normalizationResult.NormalizedText,
                            reason = normalizationResult.Reason
                        }
                    };
                }

                var normalizedMessage = normalizationResult.NormalizedText;
                var effectivePrompt = normalizedMessage;

                var priceRange = _priceIntentParser.Parse(normalizedMessage);

                _logger.LogInformation(
                    "Price parsed. Min: {Min}, Max: {Max}",
                    priceRange.MinPrice,
                    priceRange.MaxPrice
                );

                var parsedIntent = await _intentParserService.ParseAsync(normalizedMessage);

                parsedIntent.PriceMin = priceRange.MinPrice ?? parsedIntent.PriceMin;
                parsedIntent.PriceMax = priceRange.MaxPrice ?? parsedIntent.PriceMax;

                _logger.LogInformation(
                    "Intent parsed. Category: {Category}, Brand: {Brand}, Target: {Target}, PriceMin: {PriceMin}, PriceMax: {PriceMax}",
                    parsedIntent.Category,
                    parsedIntent.Brand,
                    parsedIntent.Target,
                    parsedIntent.PriceMin,
                    parsedIntent.PriceMax);

                if (string.IsNullOrWhiteSpace(request.Channel))
                {
                    request.Channel = "web";
                }
                else
                {
                    request.Channel = request.Channel.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.UserId))
                {
                    request.UserId = request.UserId.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.Phone))
                {
                    request.Phone = request.Phone.Trim();
                }

                _logger.LogInformation(
                    "Processing chat message. ConversationId: {ConversationId}, Channel: {Channel}, UserId: {UserId}",
                    request.ConversationId,
                    request.Channel,
                    request.UserId);

                if (IsOrderLookupIntent(normalizedMessage))
                {
                    _logger.LogInformation(
                        "Order lookup intent detected. ConversationId: {ConversationId}, Message: {Message}",
                        request.ConversationId,
                        normalizedMessage);

                    var result = await HandleOrderLookupAsync(request, normalizedMessage);
                    stopwatch.Stop();

                    result.ConversationId = conversationId;
                    result.UsedAI = false;
                    result.ElapsedMs = stopwatch.ElapsedMilliseconds;
                    return result;
                }

                string? forcedToolName = null;
                bool hasPreparedToolPrompt = false;

                if (IsCategoryConsultationOnly(normalizedMessage))
                {
                    var detectedCategory = parsedIntent.Category ?? ExtractVehicleCategory(normalizedMessage);

                    if (!string.IsNullOrWhiteSpace(detectedCategory))
                    {
                        _logger.LogInformation(
                            "Category consultation detected. ConversationId: {ConversationId}, Category: {Category}, Brand: {Brand}, Target: {Target}",
                            request.ConversationId,
                            detectedCategory,
                            parsedIntent.Brand,
                            parsedIntent.Target);

                        var toolResult = await _toolClient.GetProductsByFiltersAsync(
                            brand: parsedIntent.Brand,
                            minPrice: parsedIntent.PriceMin,
                            maxPrice: parsedIntent.PriceMax,
                            category: detectedCategory,
                            take: 15);

                        if (toolResult != null && toolResult.Items != null && toolResult.Items.Any())
                        {
                            forcedToolName = "get_products_by_filters";
                            hasPreparedToolPrompt = true;

                            var rankedItems = _productRecommendationService.RankProducts(
                                toolResult.Items,
                                parsedIntent,
                                normalizedMessage,
                                take: 5);

                            var toolContext = BuildProductSuggestionContext(rankedItems);

                            var targetHint = string.IsNullOrWhiteSpace(parsedIntent.Target)
                                ? ""
                                : $"Đối tượng người dùng: {parsedIntent.Target}\n";

                            effectivePrompt =
                                $"{toolContext}\n\n" +
                                $"{targetHint}" +
                                $"Yêu cầu của người dùng: {normalizedMessage}\n" +
                                "Hãy tư vấn ngắn gọn, tự nhiên, ưu tiên những mẫu phù hợp nhất với nhu cầu đã nêu. " +
                                "Nếu người dùng có nêu ngân sách, hãy ưu tiên mạnh các mẫu nằm trong hoặc rất gần ngân sách đó. " +
                                "Chỉ đề xuất mẫu vượt ngân sách khi chênh lệch không đáng kể và phải nói rõ lý do. " +
                                "Giải thích ngắn vì sao từng mẫu phù hợp. Không bịa thêm sản phẩm ngoài dữ liệu trên. " +
                                "Ưu tiên 3 đến 5 mẫu tốt nhất theo mức độ phù hợp, không chỉ sắp xếp theo giá rẻ nhất.";

                            _logger.LogInformation(
                                "Tool-first consultation applied. ConversationId: {ConversationId}, Tool: {ToolName}, Count: {Count}",
                                request.ConversationId,
                                forcedToolName,
                                rankedItems.Count);
                        }
                    }
                }

                string? ragContext = null;

                if (ShouldUseRag(normalizedMessage) || ShouldUseToolAndRag(normalizedMessage))
                {
                    try
                    {
                        var ragResult = await _ragService.QueryAsync(normalizedMessage, topK: 4);

                        _logger.LogInformation(
                            "RAG result - Success: {Success}, ContextLength: {Length}, Preview: {Preview}",
                            ragResult?.Success,
                            ragResult?.Context?.Length ?? 0,
                            string.IsNullOrWhiteSpace(ragResult?.Context)
                                ? "(empty)"
                                : ragResult!.Context.Substring(0, Math.Min(200, ragResult.Context.Length)));

                        if (ragResult != null && ragResult.Success && !string.IsNullOrWhiteSpace(ragResult.Context))
                        {
                            ragContext = ragResult.Context;

                            _logger.LogInformation(
                                "RAG context retrieved. ConversationId: {ConversationId}, Length: {Length}",
                                request.ConversationId,
                                ragContext.Length);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "No RAG context found. ConversationId: {ConversationId}. Fallback to AI only.",
                                request.ConversationId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "RAG query failed. ConversationId: {ConversationId}. Fallback to AI only.",
                            request.ConversationId);
                    }
                }

                var lowerNormalized = normalizedMessage.ToLowerInvariant();

                bool isScooter =
                    lowerNormalized.Contains("xe ga") ||
                    lowerNormalized.Contains("tay ga") ||
                    lowerNormalized.Contains("scooter");

                var hintLines = new List<string>();

                if (priceRange.MinPrice != null || priceRange.MaxPrice != null)
                {
                    var priceHint = "Giá đã chuẩn hóa: ";

                    if (priceRange.MinPrice != null)
                        priceHint += $"từ {priceRange.MinPrice} triệu ";

                    if (priceRange.MaxPrice != null)
                        priceHint += $"đến {priceRange.MaxPrice} triệu";

                    hintLines.Add(priceHint.Trim());
                }

                if (isScooter)
                {
                    hintLines.Add("Loại xe quan tâm: xe ga");
                }

                if (!hasPreparedToolPrompt)
                {
                    effectivePrompt = normalizedMessage;

                    if (hintLines.Any())
                    {
                        effectivePrompt =
                            string.Join("\n", hintLines) +
                            "\nCâu hỏi của người dùng: " + normalizedMessage;
                    }
                }

                var aiContext = new AIRequestContext
                {
                    ConversationId = conversationId,
                    Channel = request.Channel ?? "web",
                    UserId = request.UserId,
                    OriginalUserMessage = originalMessage,
                    EffectivePrompt = effectivePrompt,
                    RagContext = ragContext
                };

                var aiResult = await _openAIService.AskAsync(aiContext);
                stopwatch.Stop();

                aiResult.ConversationId = conversationId;
                aiResult.UsedAI = true;
                aiResult.ElapsedMs = stopwatch.ElapsedMilliseconds;

                if (!string.IsNullOrWhiteSpace(forcedToolName))
                {
                    aiResult.UsedTool = forcedToolName;
                }

                return aiResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing chat message. ConversationId: {ConversationId}", request?.ConversationId);

                return new ChatResponse
                {
                    Success = false,
                    Reply = "Xin lỗi, hệ thống chatbot đang gặp lỗi tạm thời.",
                    ErrorMessage = ex.Message,
                    ConversationId = request?.ConversationId,
                    UsedAI = false,
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private async Task<ChatResponse> HandleOrderLookupAsync(ChatRequest request, string message)
        {
            var orderId = ExtractOrderId(message);

            var phone = !string.IsNullOrWhiteSpace(request.Phone)
                ? request.Phone!.Trim()
                : ExtractPhone(message);

            if (orderId == null && string.IsNullOrWhiteSpace(phone))
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = "Để mình kiểm tra đơn hàng cho bạn, bạn vui lòng cung cấp mã đơn hàng và số điện thoại dùng khi đặt hàng nhé.",
                    UsedTool = null
                };
            }

            if (orderId == null)
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = "Mình đã nhận được số điện thoại. Bạn vui lòng cung cấp thêm mã đơn hàng để mình tra cứu chính xác nhé.",
                    UsedTool = null
                };
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = $"Mình đã nhận được mã đơn {orderId}. Bạn vui lòng gửi thêm số điện thoại dùng khi đặt hàng để mình kiểm tra nhé.",
                    UsedTool = null
                };
            }

            if (!IsValidVietnamPhone(phone))
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = $"Mình đã nhận được mã đơn {orderId}, nhưng số điện thoại \"{phone}\" chưa hợp lệ. Bạn vui lòng gửi số điện thoại 10 chữ số dùng khi đặt hàng để mình kiểm tra nhé.",
                    UsedTool = null
                };
            }

            var order = await _toolClient.LookupOrderAsync(orderId.Value, phone);

            if (order == null)
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = $"Mình chưa tìm thấy đơn hàng {orderId} với số điện thoại {phone}. Bạn vui lòng kiểm tra lại thông tin giúp mình nhé.",
                    UsedTool = "lookup_order"
                };
            }

            var trangThaiDon = FormatOrderStatus(order.TrangThai);
            var trangThaiCoc = string.IsNullOrWhiteSpace(order.TrangThaiCoc)
                ? null
                : FormatDepositStatus(order.TrangThaiCoc);

            var itemText = order.Items != null && order.Items.Any()
                ? string.Join("; ", order.Items.Select(i => $"{i.TenSP} x {i.SoLuong}"))
                : "Không có chi tiết sản phẩm";

            var reply =
                $"Đơn hàng {order.MaDH} hiện ở trạng thái: {trangThaiDon}. " +
                $"Tổng tiền: {order.TongTien:N0} VNĐ. " +
                $"Người nhận: {order.NguoiNhan ?? "Chưa có thông tin"}. ";

            if (!string.IsNullOrWhiteSpace(trangThaiCoc))
            {
                reply += $"Trạng thái cọc: {trangThaiCoc}. ";
            }

            reply += $"Sản phẩm: {itemText}.";

            return new ChatResponse
            {
                Success = true,
                Reply = reply,
                UsedTool = "lookup_order"
            };
        }

        private static bool IsOrderLookupIntent(string message)
        {
            var lower = message.ToLowerInvariant();

            return lower.Contains("đơn hàng")
                || lower.Contains("mã đơn")
                || lower.Contains("kiểm tra đơn")
                || lower.Contains("tra đơn")
                || lower.Contains("tình trạng đơn")
                || lower.Contains("đơn của tôi");
        }

        private static int? ExtractOrderId(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            // Chỉ bắt mã đơn khi có ngữ cảnh rõ ràng: "mã đơn", "đơn hàng", "đơn"
            var patterns = new[]
            {
        @"mã\s*đơn\s*(?:hàng)?\s*[:#]?\s*(\d{1,9})",
        @"đơn\s*hàng\s*[:#]?\s*(\d{1,9})",
        @"\bđơn\s*[:#]?\s*(\d{1,9})"
    };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
                {
                    return id;
                }
            }

            return null;
        }

        private static string? ExtractPhone(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var match = Regex.Match(message, @"(?<!\d)0\d{8,9}(?!\d)");
            return match.Success ? match.Value : null;
        }

        private static bool IsValidVietnamPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return false;

            return Regex.IsMatch(phone, @"^0\d{9}$");
        }

        private static string FormatOrderStatus(string status)
        {
            return status switch
            {
                "ChoXacNhan" => "Chờ xác nhận",
                "DangXuLy" => "Đang xử lý",
                "DangGiao" => "Đang giao",
                "HoanTat" => "Hoàn tất",
                "DaHuy" => "Đã hủy",
                _ => status
            };
        }

        private static string FormatDepositStatus(string status)
        {
            return status switch
            {
                "ChuaCoc" => "Chưa cọc",
                "ChoXacNhanCoc" => "Chờ xác nhận cọc",
                "DaCoc" => "Đã cọc",
                "HoanCoc" => "Hoàn cọc",
                "MatCoc" => "Mất cọc",
                _ => status
            };
        }
        private static bool IsAffirmative(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var text = message.Trim().ToLowerInvariant();
            return text is "đúng" or "uh" or "ừ" or "ok" or "oke" or "đúng rồi" or "yes" or "y";
        }

        private static bool IsNegative(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            var text = message.Trim().ToLowerInvariant();
            return text is "không" or "ko" or "k" or "không phải" or "sai" or "no" or "n";
        }
        private static bool IsCategoryConsultationOnly(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            bool hasConsultIntent =
                text.Contains("tư vấn") ||
                text.Contains("phù hợp") ||
                text.Contains("nên mua") ||
                text.Contains("gợi ý") ||
                text.Contains("rẻ") ||
                text.Contains("cho sinh viên") ||
                text.Contains("đi học") ||
                text.Contains("đi làm") ||
                text.Contains("cho nữ") ||
                text.Contains("cho nam");

            bool hasCategory =
                text.Contains("xe ga") ||
                text.Contains("tay ga") ||
                text.Contains("scooter") ||
                text.Contains("xe số") ||
                text.Contains("côn tay") ||
                text.Contains("xe côn");

            bool hasBrand =
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio");

            bool hasBudget =
                text.Contains("triệu") ||
                text.Contains("giá") ||
                text.Contains("dưới") ||
                text.Contains("trên") ||
                text.Contains("tầm") ||
                text.Contains("khoảng");

            bool hasTarget =
                text.Contains("sinh viên") ||
                text.Contains("học sinh") ||
                text.Contains("người mới đi") ||
                text.Contains("nữ") ||
                text.Contains("nam");

            return hasCategory && (hasConsultIntent || hasBudget || hasBrand || hasTarget);
        }

        private static string? ExtractVehicleCategory(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;

            var text = message.ToLowerInvariant();

            if (text.Contains("xe ga") || text.Contains("tay ga") || text.Contains("scooter"))
                return "xe ga";

            if (text.Contains("xe số"))
                return "xe số";

            if (text.Contains("côn tay") || text.Contains("xe côn"))
                return "côn tay";

            return null;
        }
        private bool ShouldUseRag(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            string[] ragKeywords =
            {
        "tư vấn", "phù hợp", "nên mua", "gợi ý", "so sánh",
        "trả góp", "bảo hành", "thủ tục", "địa chỉ", "giờ mở cửa",
        "ưu nhược điểm", "tiết kiệm xăng", "xe ga", "xe số", "đi học", "đi làm"
    };

            return ragKeywords.Any(k => text.Contains(k));
        }
        private static string BuildProductSuggestionContext(IEnumerable<Chatbot.API.Models.ToolApi.ProductSummaryDto> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Danh sách sản phẩm đã được chọn từ dữ liệu thực tế của hệ thống:");

            int index = 1;
            foreach (var item in items)
            {
                sb.AppendLine(
                    $"{index}. {item.Ten} | Giá: {item.Gia:N0} VNĐ | Còn hàng: {item.SoLuong} | Hãng: {item.ThuongHieu} | Loại: {item.Loai}");
                index++;
            }

            return sb.ToString();
        }

        private bool ShouldUseToolAndRag(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            bool hasConsult =
                text.Contains("tư vấn") ||
                text.Contains("phù hợp") ||
                text.Contains("nên mua") ||
                text.Contains("gợi ý") ||
                text.Contains("rẻ");

            bool hasBudget =
                text.Contains("triệu") ||
                text.Contains("giá") ||
                text.Contains("dưới") ||
                text.Contains("tầm") ||
                text.Contains("khoảng");

            bool hasProductHint =
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("xe ga") ||
                text.Contains("xe số") ||
                text.Contains("côn tay");

            bool hasTarget =
                text.Contains("sinh viên") ||
                text.Contains("đi học") ||
                text.Contains("đi làm") ||
                text.Contains("nữ") ||
                text.Contains("nam");

            return (hasConsult || hasTarget) && (hasBudget || hasProductHint || hasTarget);
        }
    }
}