using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

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
        private readonly IConversationPreferenceService _conversationPreferenceService;

        public ChatService(IOpenAIService openAIService, IWebBanXeMayToolClient toolClient, ILogger<ChatService> logger, IQueryNormalizationService queryNormalizationService, IClarificationStateService clarificationStateService, IRagService ragService, IPriceIntentParser priceIntentParser, IIntentParserService intentParserService, IProductRecommendationService productRecommendationService, IConversationPreferenceService conversationPreferenceService)
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
            _conversationPreferenceService = conversationPreferenceService;
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
                        originalMessage = pendingClarification;

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
                            Reply = "Bạn nhập lại giúp mình câu hỏi theo ý bạn nhé, mình sẽ xử lý tiếp.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                    else if (LooksLikeNewQuery(originalMessage))
                    {
                        _logger.LogInformation(
                            "Pending clarification cleared because user sent a new query. ConversationId: {ConversationId}, NewMessage: {Message}",
                            conversationId,
                            originalMessage);

                        _clarificationStateService.Clear(conversationId);
                    }
                    else if (IsWeakAmbiguousReply(originalMessage))
                    {
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = $"Bạn muốn hỏi \"{pendingClarification}\" đúng không? Nếu đúng bạn chỉ cần trả lời \"đúng\", còn không thì cứ gửi luôn câu hỏi mới nhé.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                    else
                    {
                        _clarificationStateService.Clear(conversationId);
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
                    "Price parsed. FilterType: {FilterType}, TargetPrice: {TargetPrice}, Min: {Min}, Max: {Max}",
                    priceRange.FilterType,
                    priceRange.TargetPrice,
                    priceRange.MinPrice,
                    priceRange.MaxPrice);

                var parsedIntent = await _intentParserService.ParseAsync(normalizedMessage);

                parsedIntent.PriceMin = priceRange.MinPrice ?? parsedIntent.PriceMin;
                parsedIntent.PriceMax = priceRange.MaxPrice ?? parsedIntent.PriceMax;
                parsedIntent.FilterType = priceRange.FilterType;
                parsedIntent.TargetPrice = priceRange.TargetPrice;
                var conversationProfile = await _conversationPreferenceService.MergeAsync(conversationId, parsedIntent);

                _logger.LogInformation(
                    "Conversation profile merged. ConversationId: {ConversationId}, ProfileSummary: {ProfileSummary}",
                    conversationId,
                    _conversationPreferenceService.BuildProfileSummary(conversationProfile));
                _logger.LogInformation(
                    "Intent parsed. Category: {Category}, Brand: {Brand}, Target: {Target}, PriceMin: {PriceMin}, PriceMax: {PriceMax}, FilterType: {FilterType}, TargetPrice: {TargetPrice}",
                    parsedIntent.Category,
                    parsedIntent.Brand,
                    parsedIntent.Target,
                    parsedIntent.PriceMin,
                    parsedIntent.PriceMax,
                    parsedIntent.FilterType,
                    parsedIntent.TargetPrice);

                NormalizeRequestMetadata(request);

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

                if (NeedsClarificationForConsultation(normalizedMessage, parsedIntent, conversationProfile))
                {
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = BuildClarificationQuestion(normalizedMessage, parsedIntent, conversationProfile),
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                string? forcedToolName = null;
                bool hasPreparedToolPrompt = false;
                bool wantedToolFirstConsultation = ShouldUseToolFirstConsultation(normalizedMessage, parsedIntent, conversationProfile);

                if (wantedToolFirstConsultation)
                {
                    var consultationResponse = await TryBuildToolFirstConsultationAsync(
                        request,
                        conversationId,
                        normalizedMessage,
                        parsedIntent,
                        conversationProfile);

                    if (consultationResponse != null)
                    {
                        if (!string.IsNullOrWhiteSpace(consultationResponse.Reply))
                        {
                            stopwatch.Stop();

                            return new ChatResponse
                            {
                                Success = true,
                                Reply = consultationResponse.Reply,
                                ConversationId = conversationId,
                                UsedAI = false,
                                UsedTool = consultationResponse.ToolName,
                                ElapsedMs = stopwatch.ElapsedMilliseconds
                            };
                        }

                        forcedToolName = consultationResponse.ToolName;
                        effectivePrompt = consultationResponse.EffectivePrompt;
                        hasPreparedToolPrompt = true;
                    }
                    else
                    {
                        stopwatch.Stop();

                        return new ChatResponse
                        {
                            Success = true,
                            Reply = "Mình chưa lọc ra được mẫu thật sự phù hợp từ dữ liệu hiện tại. Bạn nói thêm 1 tiêu chí ngắn như hãng muốn ưu tiên, mức giá tối đa hoặc loại xe mình sẽ lọc sát hơn nhé.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                }

                string? ragContext = null;

                if (ShouldUseRag(normalizedMessage) || ShouldUseToolAndRag(normalizedMessage))
                {
                    try
                    {
                        var ragResult = await _ragService.QueryAsync(normalizedMessage, topK: 4);

                        _logger.LogInformation(
                            "RAG result - Success: {Success}, ContextLength: {Length}",
                            ragResult?.Success,
                            ragResult?.Context?.Length ?? 0);

                        if (ragResult != null && ragResult.Success && !string.IsNullOrWhiteSpace(ragResult.Context))
                        {
                            ragContext = ragResult.Context;
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

                if (!hasPreparedToolPrompt)
                {
                    effectivePrompt = BuildFallbackPrompt(
                        normalizedMessage,
                        parsedIntent,
                        priceRange,
                        _conversationPreferenceService.BuildProfileSummary(conversationProfile));
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
                    ErrorMessage = "ChatService error",
                    ConversationId = request?.ConversationId,
                    UsedAI = false,
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private async Task<ToolFirstConsultationResult?> TryBuildToolFirstConsultationAsync(
    ChatRequest request,
    string conversationId,
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile conversationProfile)
        {
            try
            {
                var take = GetConsultationCandidateCount(normalizedMessage, parsedIntent);

                var categoryForTool = !string.IsNullOrWhiteSpace(parsedIntent.Category)
                    ? ResolveCategoryForToolFiltering(normalizedMessage, parsedIntent)
                    : conversationProfile.PreferredCategory;

                decimal? toolMinPrice = parsedIntent.PriceMin ?? conversationProfile.PriceMin;
                decimal? toolMaxPrice = parsedIntent.PriceMax ?? conversationProfile.PriceMax;

                if ((parsedIntent.FilterType == PriceFilterType.Around && parsedIntent.TargetPrice.HasValue) ||
                    (conversationProfile.FilterType == PriceFilterType.Around && conversationProfile.TargetPrice.HasValue))
                {
                    var target = parsedIntent.TargetPrice ?? conversationProfile.TargetPrice ?? 0;
                    toolMinPrice = Math.Max(0, target - 10_000_000m);
                    toolMaxPrice = target + 10_000_000m;
                }

                var toolResult = await _toolClient.GetProductsByFiltersAsync(
                    brand: parsedIntent.Brand ?? conversationProfile.PreferredBrand,
                    minPrice: toolMinPrice,
                    maxPrice: toolMaxPrice,
                    category: categoryForTool,
                    take: take);

                if ((toolResult == null || toolResult.Items == null || !toolResult.Items.Any()) && !string.IsNullOrWhiteSpace(categoryForTool))
                {
                    _logger.LogInformation(
                        "Tool-first consultation retry without category filter. ConversationId: {ConversationId}, Category: {Category}",
                        conversationId,
                        categoryForTool);

                    toolResult = await _toolClient.GetProductsByFiltersAsync(
                        brand: parsedIntent.Brand ?? conversationProfile.PreferredBrand,
                        minPrice: toolMinPrice,
                        maxPrice: toolMaxPrice,
                        category: null,
                        take: take);
                }

                if (toolResult == null || toolResult.Items == null || !toolResult.Items.Any())
                {
                    _logger.LogInformation(
                        "Tool-first consultation returned no data. ConversationId: {ConversationId}",
                        conversationId);

                    return null;
                }

                var rankTake = GetRecommendationTake(normalizedMessage, parsedIntent);

                var rankedItems = _productRecommendationService.RankProducts(
                    toolResult.Items,
                    parsedIntent,
                    conversationProfile,
                    normalizedMessage,
                    take: rankTake);

                if ((rankedItems == null || rankedItems.Count == 0) && !string.IsNullOrWhiteSpace(categoryForTool))
                {
                    var relaxedToolResult = await _toolClient.GetProductsByFiltersAsync(
                        brand: parsedIntent.Brand ?? conversationProfile.PreferredBrand,
                        minPrice: toolMinPrice,
                        maxPrice: toolMaxPrice,
                        category: null,
                        take: take);

                    if (relaxedToolResult?.Items != null && relaxedToolResult.Items.Any())
                    {
                        rankedItems = _productRecommendationService.RankProducts(
                            relaxedToolResult.Items,
                            parsedIntent,
                            conversationProfile,
                            normalizedMessage,
                            take: rankTake);
                    }
                }

                if (rankedItems == null || rankedItems.Count == 0)
                {
                    _logger.LogInformation(
                        "Ranking returned no items. ConversationId: {ConversationId}",
                        conversationId);

                    return null;
                }

                // ✅ RAG advisory: dùng để làm giàu lý do tư vấn, không thay Tool/Ranking
                string? advisoryContext = null;
                Dictionary<string, string> ragReasonHints = new(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var ragQuery = BuildRagAdvisoryQuery(
                        normalizedMessage,
                        parsedIntent,
                        conversationProfile,
                        rankedItems);

                    var ragResult = await _ragService.QueryAsync(ragQuery, topK: 4);

                    if (ragResult != null && ragResult.Success && !string.IsNullOrWhiteSpace(ragResult.Context))
                    {
                        advisoryContext = ragResult.Context;
                        ragReasonHints = BuildReasonHintsFromRagContext(advisoryContext, rankedItems);

                        _logger.LogInformation(
                            "RAG advisory applied. ConversationId: {ConversationId}, AdvisoryLength: {Length}",
                            conversationId,
                            advisoryContext.Length);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "RAG advisory failed. ConversationId: {ConversationId}",
                        conversationId);
                }

                var deterministicReply = BuildDeterministicConsultationReply(
                    rankedItems,
                    normalizedMessage,
                    parsedIntent,
                    conversationProfile,
                    ragReasonHints);

                var toolContext = BuildProductSuggestionContext(rankedItems);
                var effectivePrompt = BuildConsultationPrompt(
                    toolContext,
                    normalizedMessage,
                    parsedIntent,
                    conversationProfile);

                if (!string.IsNullOrWhiteSpace(advisoryContext))
                {
                    effectivePrompt += "\n\nGợi ý tư vấn bổ sung từ tri thức nội bộ:\n" + advisoryContext;
                }

                return new ToolFirstConsultationResult
                {
                    ToolName = ToolNames.GetProductsByFilters,
                    EffectivePrompt = effectivePrompt,
                    Reply = deterministicReply
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Tool-first consultation failed. ConversationId: {ConversationId}",
                    conversationId);

                return null;
            }
        }
        private async Task<ChatResponse> HandleOrderLookupAsync(ChatRequest request, string message)
        {
            var orderId = ExtractOrderId(message);

            var phone = !string.IsNullOrWhiteSpace(request.Phone)
                ? request.Phone.Trim()
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
                    UsedTool = ToolNames.LookupOrder
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
                UsedTool = ToolNames.LookupOrder
            };
        }

        private static void NormalizeRequestMetadata(ChatRequest request)
        {
            request.Channel = string.IsNullOrWhiteSpace(request.Channel)
                ? "web"
                : request.Channel.Trim();

            if (!string.IsNullOrWhiteSpace(request.UserId))
            {
                request.UserId = request.UserId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                request.Phone = request.Phone.Trim();
            }
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

        private static bool LooksLikeNewQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            if (text.Length >= 8)
                return true;

            string[] strongKeywords =
            {
                "xe", "honda", "yamaha", "suzuki", "sym", "piaggio",
                "vision", "air blade", "ab", "vario", "janus", "sirius",
                "giá", "bao nhiêu", "còn hàng", "tồn kho",
                "tư vấn", "phù hợp", "nên mua", "đơn hàng", "mã đơn"
            };

            return strongKeywords.Any(k => text.Contains(k));
        }

        private static bool IsWeakAmbiguousReply(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return true;

            var text = message.Trim().ToLowerInvariant();

            return text is "?" or "sao" or "gì" or "hả" or "ừm";
        }

        private static bool IsConsultationIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            return text.Contains("tư vấn")
                || text.Contains("phù hợp")
                || text.Contains("nên mua")
                || text.Contains("gợi ý")
                || text.Contains("xe nào")
                || text.Contains("mua xe nào")
                || text.Contains("đi học nên mua")
                || text.Contains("đi làm nên mua")
                || text.Contains("xe nào rẻ")
                || text.Contains("cho nữ")
                || text.Contains("cho nam")
                || text.Contains("sinh viên")
                || text.Contains("đi học")
                || text.Contains("đi làm")
                || text.Contains("đi phố")
                || text.Contains("tiết kiệm xăng")
                || text.Contains("cốp rộng")
                || text.Contains("nhẹ")
                || text.Contains("dễ đi")
                || text.Contains("cá tính")
                || text.Contains("thể thao")
                || text.Contains("thanh lịch")
                || text.Contains("xe ga")
                || text.Contains("xe số")
                || text.Contains("côn tay");
        }

        private static bool NeedsClarificationForConsultation(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();
            var isConsultation = IsConsultationIntent(message);

            if (!isConsultation)
                return false;

            bool hasBudget = parsedIntent.PriceMin.HasValue
    || parsedIntent.PriceMax.HasValue
    || parsedIntent.TargetPrice.HasValue
    || profile?.PriceMin.HasValue == true
    || profile?.PriceMax.HasValue == true
    || profile?.TargetPrice.HasValue == true
    || text.Contains("triệu")
    || text.Contains("tầm")
    || text.Contains("khoảng")
    || text.Contains("quanh");

            bool hasCategory = !string.IsNullOrWhiteSpace(parsedIntent.Category)
                || !string.IsNullOrWhiteSpace(profile?.PreferredCategory);

            bool hasBrand = !string.IsNullOrWhiteSpace(parsedIntent.Brand)
                || !string.IsNullOrWhiteSpace(profile?.PreferredBrand);

            bool hasTarget = !string.IsNullOrWhiteSpace(parsedIntent.Target)
                || !string.IsNullOrWhiteSpace(profile?.Target)
                || text.Contains("đi học")
                || text.Contains("đi làm")
                || text.Contains("sinh viên")
                || text.Contains("nam")
                || text.Contains("nữ");

            bool hasNeedHint = text.Contains("tiết kiệm xăng")
                || text.Contains("cốp rộng")
                || text.Contains("nhẹ")
                || text.Contains("dễ đi")
                || text.Contains("cá tính")
                || text.Contains("thể thao")
                || text.Contains("thanh lịch")
                || text.Contains("đi phố")
                || text.Contains("di pho")
                || profile?.WantsEasyControl == true
                || profile?.WantsFuelSaving == true
                || profile?.WantsLargeStorage == true
                || profile?.NeedsLowSeat == true
                || profile?.HeightCm.HasValue == true;

            if (hasBudget && (hasTarget || hasCategory || hasBrand || hasNeedHint))
                return false;

            if (hasTarget && hasNeedHint)
                return false;

            if (hasCategory && hasNeedHint)
                return false;

            int knownSignals = 0;
            if (hasBudget) knownSignals++;
            if (hasCategory) knownSignals++;
            if (hasBrand) knownSignals++;
            if (hasTarget) knownSignals++;
            if (hasNeedHint) knownSignals++;
            if (hasNeedHint && (text.Contains("đi phố") || text.Contains("di pho") || text.Contains("cá tính") || text.Contains("ca tinh")))
            {
                return false;
            }

            return knownSignals <= 1;
        }

        private static string BuildClarificationQuestion(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            var text = message.ToLowerInvariant();

            bool hasGender =
                text.Contains("nữ") ||
                text.Contains("nam") ||
                (parsedIntent.Target ?? string.Empty).Contains("nữ", StringComparison.OrdinalIgnoreCase) ||
                (parsedIntent.Target ?? string.Empty).Contains("nam", StringComparison.OrdinalIgnoreCase);

            bool hasBudget =
    parsedIntent.PriceMin.HasValue ||
    parsedIntent.PriceMax.HasValue ||
    parsedIntent.TargetPrice.HasValue ||
    profile?.PriceMin.HasValue == true ||
    profile?.PriceMax.HasValue == true ||
    profile?.TargetPrice.HasValue == true ||
    text.Contains("triệu") ||
    text.Contains("tầm") ||
    text.Contains("khoảng") ||
    text.Contains("quanh");

            bool hasCategory =
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(profile?.PreferredCategory);

            if (hasGender && !hasBudget)
            {
                return "Mình có thể gợi ý sơ bộ theo nhu cầu này. Bạn muốn mình ưu tiên tầm giá nào để lọc sát hơn?";
            }

            if (hasBudget && !hasCategory)
            {
                return "Mình lọc sơ bộ được rồi. Bạn thích nghiêng về xe ga, xe số hay để mình tự chọn mẫu phù hợp nhất?";
            }

            if (!hasBudget && !hasCategory)
            {
                return "Bạn cho mình thêm 1 ý là ngân sách hoặc loại xe bạn thích, mình sẽ gợi ý sát hơn nhé.";
            }

            return "Bạn nói thêm giúp mình 1 chi tiết quan trọng nhất như ngân sách, loại xe hoặc nhu cầu sử dụng để mình lọc chính xác hơn nhé.";
        }

        private static bool ShouldUseToolFirstConsultation(
     string message,
     ParsedIntent parsedIntent,
     CustomerPreferenceProfile? profile = null)
        {
            var safeMessage = message ?? string.Empty;
            var text = safeMessage.ToLowerInvariant();

            bool currentMessageLooksLikeConsultation =
                IsConsultationIntent(safeMessage)
                || text.Contains("không thích")
                || text.Contains("khong thich")
                || text.Contains("không muốn")
                || text.Contains("khong muon")
                || text.Contains("ghét")
                || text.Contains("ghet")
                || text.Contains("né")
                || text.Contains("ne ")
                || text.Contains("cốp rộng")
                || text.Contains("cop rong")
                || text.Contains("dễ đi")
                || text.Contains("de di")
                || text.Contains("dễ chống chân")
                || text.Contains("de chong chan")
                || text.Contains("yên thấp")
                || text.Contains("yen thap")
                || Regex.IsMatch(text, @"\b1m\d{1,2}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\bm\d{2}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\b\d{3}\s*cm\b", RegexOptions.IgnoreCase)
                || text.Contains("người thấp")
                || text.Contains("nguoi thap")
                || text.Contains("nhỏ con")
                || text.Contains("nho con")
                || text.Contains("còn honda thì sao")
                || text.Contains("còn yamaha thì sao")
                || text.Contains("còn suzuki thì sao")
                || text.Contains("còn piaggio thì sao")
                || text.StartsWith("còn ")
                || text.Contains("ưu tiên")
                || text.Contains("đi làm")
                || text.Contains("di lam")
                || text.Contains("đi học")
                || text.Contains("di hoc");

            bool hasCurrentSignals =
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.HeightCm.HasValue ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.ForSchool ||
                parsedIntent.ForWork ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                parsedIntent.ExcludedCategories.Count > 0 ||
                parsedIntent.ExcludedBrands.Count > 0 ||
                parsedIntent.RequestedStyles.Count > 0;

            bool hasStrongProfile =
                profile != null &&
                (
                    profile.PriceMin.HasValue ||
                    profile.PriceMax.HasValue ||
                    profile.TargetPrice.HasValue ||
                    !string.IsNullOrWhiteSpace(profile.PreferredCategory) ||
                    !string.IsNullOrWhiteSpace(profile.PreferredBrand) ||
                    !string.IsNullOrWhiteSpace(profile.Target) ||
                    profile.HeightCm.HasValue ||
                    profile.NeedsLowSeat ||
                    profile.WantsEasyControl ||
                    profile.WantsFuelSaving ||
                    profile.WantsLargeStorage ||
                    profile.ForSchool ||
                    profile.ForWork ||
                    profile.ForCity ||
                    profile.ForTour ||
                    profile.ExcludedCategories.Count > 0 ||
                    profile.ExcludedBrands.Count > 0 ||
                    profile.RequestedStyles.Count > 0
                );

            return currentMessageLooksLikeConsultation && (hasCurrentSignals || hasStrongProfile);
        }
        private static int GetConsultationCandidateCount(string message, ParsedIntent parsedIntent)
        {
            var text = message.ToLowerInvariant();

            if (parsedIntent.FilterType == PriceFilterType.Around || parsedIntent.TargetPrice.HasValue)
                return 40;

            if (!string.IsNullOrWhiteSpace(parsedIntent.Target) && string.IsNullOrWhiteSpace(parsedIntent.Category))
                return 36;

            if (text.Contains("tư vấn") || text.Contains("khoảng") || text.Contains("tầm") || text.Contains("quanh"))
                return 32;

            if (text.Contains("cá tính") || text.Contains("thể thao") || text.Contains("đi phố") || text.Contains("tiết kiệm xăng"))
                return 32;

            return 24;
        }

        private static int GetRecommendationTake(string message, ParsedIntent parsedIntent)
        {
            var text = message.ToLowerInvariant();

            bool openConsultation =
                text.Contains("khoảng") ||
                text.Contains("tầm") ||
                text.Contains("quanh") ||
                text.Contains("tư vấn") ||
                text.Contains("phù hợp") ||
                text.Contains("nên mua") ||
                text.Contains("gợi ý") ||
                text.Contains("xe nào") ||
                text.Contains("cá tính") ||
                text.Contains("thể thao") ||
                text.Contains("đi phố") ||
                text.Contains("tiết kiệm xăng");

            return openConsultation ? 4 : 3;
        }

        private static string? ResolveCategoryForToolFiltering(string message, ParsedIntent parsedIntent)
        {
            var text = message.ToLowerInvariant();
            var category = parsedIntent.Category?.Trim();

            if (string.IsNullOrWhiteSpace(category))
                return null;

            bool mentionsNegativeVehiclePreference =
                text.Contains("không thích xe côn") ||
                text.Contains("không thích côn") ||
                text.Contains("không muốn xe côn") ||
                text.Contains("không thích xe số") ||
                text.Contains("không muốn xe số") ||
                text.Contains("không thích xe ga") ||
                text.Contains("không muốn xe ga");

            if (mentionsNegativeVehiclePreference)
            {
                return null;
            }

            bool openConsultation =
                text.Contains("khoảng") ||
                text.Contains("tầm") ||
                text.Contains("quanh") ||
                text.Contains("tư vấn") ||
                text.Contains("gợi ý") ||
                text.Contains("phù hợp") ||
                text.Contains("nên mua");

            if (openConsultation)
            {
                if (category.Contains("ga", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("số", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("so", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("côn", StringComparison.OrdinalIgnoreCase)
                    || category.Contains("con", StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }

                return null;
            }

            return category;
        }

        private static string BuildConsultationPrompt(
    string toolContext,
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine(toolContext);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(parsedIntent.Target))
            {
                sb.AppendLine($"Đối tượng người dùng: {parsedIntent.Target}");
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Category))
            {
                sb.AppendLine($"Loại xe quan tâm: {parsedIntent.Category}");
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Brand))
            {
                sb.AppendLine($"Hãng xe quan tâm: {parsedIntent.Brand}");
            }
            if (profile != null)
            {
                if (profile.HeightCm.HasValue)
                    sb.AppendLine($"Chiều cao người dùng: khoảng {profile.HeightCm.Value}cm");

                if (profile.NeedsLowSeat)
                    sb.AppendLine("Ưu tiên: yên thấp, dễ chống chân");

                if (profile.WantsLargeStorage)
                    sb.AppendLine("Ưu tiên: cốp rộng");

                if (profile.ForWork)
                    sb.AppendLine("Nhu cầu sử dụng: đi làm hằng ngày");

                if (profile.ExcludedCategories.Count > 0)
                    sb.AppendLine($"Loại xe cần loại trừ: {string.Join(", ", profile.ExcludedCategories)}");
            }
            sb.AppendLine($"Yêu cầu của người dùng: {normalizedMessage}");
            sb.AppendLine("Hãy tư vấn ngắn gọn, tự nhiên, bám đúng dữ liệu thực tế ở trên.");
            sb.AppendLine("BẮT BUỘC: với câu hỏi mở như có 'khoảng', 'tầm', 'quanh', 'tư vấn', 'gợi ý', 'phù hợp', hãy đưa 2 đến 4 lựa chọn nổi bật để người dùng so sánh.");
            sb.AppendLine("Chỉ được chốt 1 mẫu duy nhất khi dữ liệu thực tế sau lọc chỉ còn 1 lựa chọn phù hợp rõ ràng.");
            sb.AppendLine("Ưu tiên mạnh các mẫu có giá nằm trong hoặc rất gần ngân sách. Không được ưu tiên các mẫu rẻ quá xa ngân sách chỉ vì rẻ hơn.");
            sb.AppendLine("Nếu ngân sách là dạng 'khoảng/tầm', hãy xem mức phù hợp là gần ngân sách. Một mẫu rẻ hơn quá nhiều chỉ được nêu khi thực sự không có mẫu nào gần hơn.");
            sb.AppendLine("Nếu người dùng nói không thích một loại xe, chỉ loại đúng loại đó. Vẫn tiếp tục gợi ý các loại còn lại nếu có.");
            sb.AppendLine("Không được trả lời 'không có xe phù hợp' khi vẫn còn phương án gần đúng hoặc phương án thay thế hợp lý trong dữ liệu.");
            sb.AppendLine("Khi chưa đủ dữ kiện, hãy gợi ý sơ bộ trước rồi hỏi thêm 1 câu ngắn để chốt nhu cầu, không hỏi lại toàn bộ từ đầu.");
            sb.AppendLine("Không được tự thêm bối cảnh mà người dùng không nêu. Nếu người dùng chỉ nói 'cho nữ' thì không được tự suy ra 'nữ sinh viên' hoặc 'đi học'.");
            sb.AppendLine("Với nhu cầu 'đi làm', ưu tiên mẫu trung tính, thực dụng, linh hoạt đi phố. Tránh đẩy mẫu quá thiên về nữ tính hoặc quá hầm hố nếu không có tín hiệu phù hợp.");
            sb.AppendLine("Với từ khóa như 'cá tính', 'thể thao', phải ưu tiên rõ các mẫu có phong cách đó hơn các mẫu hiền hoặc an toàn.");
            sb.AppendLine("Mỗi lựa chọn viết 1 dòng rõ ràng theo dạng: Tên xe - giá - lý do ngắn.");
            sb.AppendLine("Sau danh sách, chỉ thêm tối đa 1 câu chốt ngắn hoặc 1 câu hỏi phụ nếu thật sự cần.");
            sb.AppendLine("Không bịa thêm sản phẩm ngoài dữ liệu trên.");
            sb.AppendLine("Không được mô tả sai số lượng sản phẩm, sai giá, sai loại xe so với dữ liệu.");
            return sb.ToString().Trim();
        }

        private static string BuildFallbackPrompt(
    string normalizedMessage,
    ParsedIntent parsedIntent,
    PriceIntent priceRange,
    string? profileSummary = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Người dùng đang hỏi tư vấn xe máy.");
            sb.AppendLine($"Câu hỏi: {normalizedMessage}");
            if (!string.IsNullOrWhiteSpace(profileSummary))
            {
                sb.AppendLine(profileSummary);
            }
            if (priceRange.FilterType == PriceFilterType.Around && priceRange.TargetPrice.HasValue)
            {
                sb.AppendLine($"Ngân sách mục tiêu: khoảng {priceRange.TargetPrice.Value:N0} VNĐ");
            }
            else
            {
                if (priceRange.MinPrice.HasValue)
                    sb.AppendLine($"Giá tối thiểu: {priceRange.MinPrice.Value:N0} VNĐ");

                if (priceRange.MaxPrice.HasValue)
                    sb.AppendLine($"Giá tối đa: {priceRange.MaxPrice.Value:N0} VNĐ");
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Category))
            {
                sb.AppendLine($"Loại xe quan tâm: {parsedIntent.Category}");
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Target))
            {
                sb.AppendLine($"Đối tượng người dùng: {parsedIntent.Target}");
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Brand))
            {
                sb.AppendLine($"Hãng xe quan tâm: {parsedIntent.Brand}");
            }

            sb.AppendLine("Nếu không có dữ liệu sản phẩm cụ thể, không được bịa tên xe.");
            sb.AppendLine("Nếu có đủ tín hiệu như ngân sách + đối tượng hoặc nhu cầu, hãy tư vấn sơ bộ thay vì hỏi lại từ đầu.");
            sb.AppendLine("Nếu chưa đủ dữ liệu để chốt chính xác, hãy nêu 1-2 hướng phù hợp và hỏi thêm đúng 1 ý ngắn nhất cần làm rõ.");
            sb.AppendLine("Không dùng câu 'hệ thống đang phản hồi chậm hơn bình thường'.");
            sb.AppendLine("Không trả lời cụt kiểu 'không có' nếu vẫn có thể đề xuất phương án gần nhất hoặc phương án thay thế.");
            sb.AppendLine("Với câu hỏi mở, ưu tiên trả lời theo 2-4 lựa chọn khi có dữ kiện đủ.");

            return sb.ToString().Trim();
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
                text.Contains("tầm") ||
                text.Contains("khoảng") ||
                text.Contains("quanh");

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

        private static string BuildDeterministicConsultationReply(
     IReadOnlyList<ProductSummaryDto> items,
     string normalizedMessage,
     ParsedIntent parsedIntent,
     CustomerPreferenceProfile? profile = null,
     Dictionary<string, string>? ragReasonHints = null)
        {
            if (items == null || items.Count == 0)
            {
                return "Mình chưa thấy mẫu nào thật sự phù hợp từ dữ liệu hiện tại. Bạn có thể nói thêm ngân sách hoặc loại xe muốn ưu tiên để mình lọc sát hơn.";
            }

            var text = normalizedMessage.ToLowerInvariant();
            var isOpenQuery = IsOpenConsultationQuery(text);

            var maxItems = isOpenQuery ? 4 : 3;
            var selectedItems = items.Take(Math.Min(maxItems, items.Count)).ToList();

            string intro;
            var hasProfileContext = profile != null &&
                (
                    profile.TargetPrice.HasValue ||
                    profile.PriceMax.HasValue ||
                    profile.HeightCm.HasValue ||
                    profile.WantsLargeStorage ||
                    profile.WantsEasyControl ||
                    profile.ForWork ||
                    profile.ForSchool ||
                    profile.ExcludedCategories.Count > 0
                );

            if (selectedItems.Count == 1)
            {
                intro = hasProfileContext
                    ? "Dựa trên các tiêu chí bạn đã nói từ trước, mình thấy hiện tại có 1 lựa chọn khá phù hợp:"
                    : "Mình thấy hiện tại có 1 lựa chọn khá phù hợp với nhu cầu bạn đang hỏi:";
            }
            else
            {
                intro = hasProfileContext
                    ? $"Dựa trên các tiêu chí bạn đang quan tâm, mình gợi ý {selectedItems.Count} mẫu khá phù hợp để bạn tham khảo:"
                    : $"Mình gợi ý {selectedItems.Count} mẫu khá phù hợp để bạn tham khảo:";
            }

            var lines = new List<string>();

            for (int i = 0; i < selectedItems.Count; i++)
            {
                var item = selectedItems[i];

                var baseReason = BuildProductReason(item, text, parsedIntent, profile);
                var ragHint = string.Empty;

                if (ragReasonHints != null && ragReasonHints.TryGetValue(item.Ten, out var hint))
                {
                    ragHint = hint;
                }

                var finalReason = baseReason;

                if (!string.IsNullOrWhiteSpace(ragHint))
                {
                    finalReason = MergeReason(baseReason, ragHint);
                }

                lines.Add($"{i + 1}. **{item.Ten}** - Giá: {item.Gia:N0} VNĐ - Còn hàng: {item.SoLuong} - {finalReason}");
            }

            var conclusion = BuildConsultationConclusion(selectedItems, text, parsedIntent, profile);
            var followUp = BuildFollowUpQuestion(text, parsedIntent, profile);
            var content = string.Join("\n", lines);

            var parts = new List<string> { intro, content };

            if (!string.IsNullOrWhiteSpace(conclusion))
            {
                parts.Add(conclusion);
            }

            if (!string.IsNullOrWhiteSpace(followUp))
            {
                parts.Add(followUp);
            }

            return string.Join("\n\n", parts);
        }

        private static string BuildRagAdvisoryQuery(
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile,
    IReadOnlyList<ProductSummaryDto> rankedItems)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Tư vấn xe máy theo nhu cầu sau:");

            if (!string.IsNullOrWhiteSpace(profile?.Target))
                sb.AppendLine($"- Đối tượng: {profile.Target}");
            else if (!string.IsNullOrWhiteSpace(parsedIntent.Target))
                sb.AppendLine($"- Đối tượng: {parsedIntent.Target}");

            if (profile?.TargetPrice.HasValue == true)
                sb.AppendLine($"- Ngân sách khoảng: {profile.TargetPrice.Value:N0} VNĐ");
            else if (profile?.PriceMax.HasValue == true)
                sb.AppendLine($"- Ngân sách tối đa: {profile.PriceMax.Value:N0} VNĐ");
            else if (parsedIntent.TargetPrice.HasValue)
                sb.AppendLine($"- Ngân sách khoảng: {parsedIntent.TargetPrice.Value:N0} VNĐ");

            if (profile?.HeightCm.HasValue == true)
                sb.AppendLine($"- Chiều cao khoảng: {profile.HeightCm.Value}cm");

            if (profile?.NeedsLowSeat == true)
                sb.AppendLine("- Ưu tiên dễ chống chân");

            if (profile?.WantsLargeStorage == true)
                sb.AppendLine("- Ưu tiên cốp rộng");

            if (profile?.ForWork == true)
                sb.AppendLine("- Nhu cầu đi làm hàng ngày");

            if (profile?.ForSchool == true)
                sb.AppendLine("- Nhu cầu đi học");

            if (profile?.ExcludedCategories.Count > 0)
                sb.AppendLine($"- Loại trừ: {string.Join(", ", profile.ExcludedCategories)}");

            if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand))
                sb.AppendLine($"- Hãng đang ưu tiên: {profile.PreferredBrand}");

            sb.AppendLine($"- Câu hỏi hiện tại: {normalizedMessage}");

            if (rankedItems != null && rankedItems.Count > 0)
            {
                sb.AppendLine("- Các mẫu đang cân nhắc:");
                foreach (var item in rankedItems.Take(4))
                {
                    sb.AppendLine($"  + {item.Ten}");
                }
            }

            sb.AppendLine("Hãy nêu ngắn gọn điểm mạnh riêng của từng mẫu và trường hợp nên chọn mẫu nào.");

            return sb.ToString().Trim();
        }
        private static Dictionary<string, string> BuildReasonHintsFromRagContext(
    string ragContext,
    IReadOnlyList<ProductSummaryDto> items)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(ragContext) || items == null || items.Count == 0)
                return result;

            var normalizedContext = ragContext.ToLowerInvariant();

            foreach (var item in items)
            {
                var name = item.Ten ?? string.Empty;
                var lowerName = name.ToLowerInvariant();
                var hints = new List<string>();

                bool mentioned =
                    normalizedContext.Contains(lowerName) ||
                    lowerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Any(t => t.Length >= 4 && normalizedContext.Contains(t));

                if (!mentioned)
                    continue;

                if (lowerName.Contains("vision"))
                {
                    if (normalizedContext.Contains("dễ làm quen") || normalizedContext.Contains("dễ đi"))
                        hints.Add("dễ làm quen và hợp đi hằng ngày");
                    if (normalizedContext.Contains("dễ chống chân") || normalizedContext.Contains("yên thấp") || normalizedContext.Contains("gọn"))
                        hints.Add("đáng chú ý hơn nếu bạn ưu tiên dáng gọn và dễ chống chân");
                }

                if (lowerName.Contains("freego"))
                {
                    if (normalizedContext.Contains("cốp rộng") || normalizedContext.Contains("mang đồ"))
                        hints.Add("thiên về nhóm cốp rộng, tiện mang đồ");
                    if (normalizedContext.Contains("thực dụng") || normalizedContext.Contains("đi làm"))
                        hints.Add("khá hợp với nhu cầu đi làm thực dụng hằng ngày");
                }

                if (lowerName.Contains("latte"))
                {
                    if (normalizedContext.Contains("nữ") || normalizedContext.Contains("dáng mềm"))
                        hints.Add("dáng xe mềm và hợp hơn với nhu cầu nữ");
                    if (normalizedContext.Contains("tiện ích") || normalizedContext.Contains("cốp rộng"))
                        hints.Add("cân bằng khá tốt giữa dáng đẹp và tiện ích");
                }

                if (lowerName.Contains("zip"))
                {
                    if (normalizedContext.Contains("yên thấp") || normalizedContext.Contains("dễ chống chân") || normalizedContext.Contains("thấp"))
                        hints.Add("rất đáng cân nhắc nếu bạn ưu tiên yên thấp và dễ chống chân");
                    if (normalizedContext.Contains("gọn") || normalizedContext.Contains("nhỏ con"))
                        hints.Add("dáng xe nhỏ gọn, hợp người có vóc dáng nhỏ");
                }

                if (lowerName.Contains("air blade"))
                {
                    if (normalizedContext.Contains("đầm") || normalizedContext.Contains("mạnh"))
                        hints.Add("hợp hơn nếu bạn muốn cảm giác xe đầm và khỏe hơn");
                    if (normalizedContext.Contains("đi làm"))
                        hints.Add("khá hợp cho nhu cầu đi làm hằng ngày");
                }

                if (lowerName.Contains("future"))
                {
                    if (normalizedContext.Contains("xe số") || normalizedContext.Contains("thực dụng"))
                        hints.Add("thiên về hướng xe số thực dụng và ổn định");
                    if (normalizedContext.Contains("đi làm"))
                        hints.Add("hợp với người đi làm cần xe số bền và tiết kiệm");
                }

                if (lowerName.Contains("wave"))
                {
                    if (normalizedContext.Contains("chi phí thấp") || normalizedContext.Contains("giá thấp"))
                        hints.Add("hợp nếu bạn ưu tiên chi phí sử dụng thấp");
                    if (normalizedContext.Contains("dễ sửa") || normalizedContext.Contains("phụ tùng"))
                        hints.Add("dễ bảo dưỡng và sửa chữa");
                }

                if (lowerName.Contains("sirius"))
                {
                    if (normalizedContext.Contains("sinh viên") || normalizedContext.Contains("đi học"))
                        hints.Add("khá hợp cho sinh viên hoặc đi học đi làm mỗi ngày");
                    if (normalizedContext.Contains("tiết kiệm") || normalizedContext.Contains("dễ bảo dưỡng"))
                        hints.Add("thiên về nhóm xe số tiết kiệm và dễ bảo dưỡng");
                }

                if (lowerName.Contains("address"))
                {
                    if (normalizedContext.Contains("cốp") || normalizedContext.Contains("ga nhỏ gọn"))
                        hints.Add("có lợi thế ở nhóm xe ga nhỏ gọn và tiện ích");
                }

                if (lowerName.Contains("impulse"))
                {
                    if (normalizedContext.Contains("chi phí mềm") || normalizedContext.Contains("dễ cân nhắc"))
                        hints.Add("là phương án ga chi phí mềm khá dễ cân nhắc");
                }

                if (hints.Count > 0)
                {
                    result[name] = string.Join(", ", hints.Distinct().Take(2));
                }
            }

            return result;
        }
        private static string MergeReason(string baseReason, string ragHint)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(baseReason))
                parts.Add(baseReason.Trim());

            if (!string.IsNullOrWhiteSpace(ragHint))
                parts.Add(ragHint.Trim());

            return string.Join(", ", parts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(2));
        }
        private static bool IsOpenConsultationQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.Contains("khoảng")
                || text.Contains("tầm")
                || text.Contains("quanh")
                || text.Contains("tư vấn")
                || text.Contains("gợi ý")
                || text.Contains("phù hợp")
                || text.Contains("nên mua")
                || text.Contains("xe nào")
                || text.Contains("cho nữ")
                || text.Contains("cho nam")
                || text.Contains("cá tính")
                || text.Contains("ca tinh")
                || text.Contains("thể thao")
                || text.Contains("the thao")
                || text.Contains("đi phố")
                || text.Contains("di pho")
                || text.Contains("tiết kiệm xăng")
                || text.Contains("tiet kiem xang");
        }

        private static string BuildProductReason(
    ProductSummaryDto item,
    string text,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            var primaryReasons = new List<string>();
            var secondaryReasons = new List<string>();

            var name = item.Ten ?? string.Empty;
            var category = item.Loai?.Trim() ?? string.Empty;

            // 1. Giá / ngân sách
            if (parsedIntent.TargetPrice.HasValue)
            {
                var diff = Math.Abs(item.Gia - parsedIntent.TargetPrice.Value);

                if (diff <= 2_000_000m)
                    primaryReasons.Add("giá khá sát ngân sách");
                else if (diff <= 4_000_000m)
                    primaryReasons.Add(item.Gia <= parsedIntent.TargetPrice.Value
                        ? "giá vẫn khá gần ngân sách"
                        : "giá nhỉnh hơn ngân sách một chút");
            }
            else if (parsedIntent.PriceMax.HasValue && item.Gia <= parsedIntent.PriceMax.Value)
            {
                primaryReasons.Add("nằm trong tầm giá bạn đang cân nhắc");
            }

            // 2. Loại xe / loại trừ
            if (profile?.ExcludedCategories.Contains("côn tay") == true)
            {
                if (!category.Contains("côn", StringComparison.OrdinalIgnoreCase) &&
                    !category.Contains("con", StringComparison.OrdinalIgnoreCase))
                {
                    secondaryReasons.Add("không thuộc nhóm xe côn tay");
                }
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                category.Contains(parsedIntent.Category, StringComparison.OrdinalIgnoreCase))
            {
                secondaryReasons.Add($"đúng hướng {category.ToLowerInvariant()}");
            }

            // 3. Nữ / nam
            if ((text.Contains("nữ") || text.Contains("nu") || profile?.PrefersFemaleStyle == true) &&
                (name.Contains("Vision", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Latte", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Grande", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Zip", StringComparison.OrdinalIgnoreCase)))
            {
                primaryReasons.Add("dáng xe khá gọn và hợp nhu cầu nữ");
            }

            if ((text.Contains("nam") || profile?.PrefersMaleStyle == true) &&
                (name.Contains("Air Blade", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Vario", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Winner", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Exciter", StringComparison.OrdinalIgnoreCase)))
            {
                primaryReasons.Add("kiểu dáng khá hợp nhu cầu nam");
            }

            // 4. Dễ chống chân / vóc dáng nhỏ
            if (profile?.NeedsLowSeat == true || profile?.HeightCm.HasValue == true)
            {
                if (name.Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Latte", StringComparison.OrdinalIgnoreCase))
                {
                    primaryReasons.Add("hợp hơn nếu bạn ưu tiên dễ chống chân");
                }
            }

            // 5. Cốp rộng
            if (profile?.WantsLargeStorage == true)
            {
                if (name.Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Vision", StringComparison.OrdinalIgnoreCase))
                {
                    primaryReasons.Add("phù hợp hơn với nhu cầu ưu tiên cốp rộng");
                }
            }

            // 6. Đi làm
            if (profile?.ForWork == true)
            {
                if (name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Air Blade", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Future", StringComparison.OrdinalIgnoreCase))
                {
                    primaryReasons.Add("hợp cho nhu cầu đi làm hằng ngày");
                }
            }

            // 7. Hãng ưu tiên
            if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand) &&
                item.ThuongHieu.Equals(profile.PreferredBrand, StringComparison.OrdinalIgnoreCase))
            {
                secondaryReasons.Add($"đúng hãng {profile.PreferredBrand} bạn đang muốn xem");
            }

            // 8. Cá tính / thể thao
            if ((text.Contains("cá tính") || text.Contains("ca tinh") ||
                 text.Contains("thể thao") || text.Contains("the thao")) &&
                (name.Contains("Air Blade", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Vario", StringComparison.OrdinalIgnoreCase)
                 || name.Contains("Exciter", StringComparison.OrdinalIgnoreCase)))
            {
                primaryReasons.Add("kiểu dáng nổi bật hơn");
            }

            // Gộp lý do: ưu tiên lý do “nghe như tư vấn”, tránh lặp máy móc
            var finalReasons = primaryReasons
                .Concat(secondaryReasons)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(2)
                .ToList();
            if (profile?.TargetPrice.HasValue == true)
            {
                var diff = item.Gia - profile.TargetPrice.Value;
                if (diff > 10_000_000m)
                {
                    finalReasons.RemoveAll(x => x.Contains("đúng hãng", StringComparison.OrdinalIgnoreCase));
                }
            }
            if (finalReasons.Count == 0)
            {
                return "là lựa chọn khá đáng cân nhắc trong nhóm đang lọc";
            }

            return string.Join(", ", finalReasons);
        }

        private static string BuildFollowUpQuestion(
    string text,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            // Nếu đã loại côn tay rồi thì đừng hỏi lại côn tay nữa
            if (profile?.ExcludedCategories.Contains("côn tay") == true)
            {
                if (profile.WantsLargeStorage && profile.NeedsLowSeat)
                {
                    return "Nếu muốn mình chốt sát hơn, mình có thể lọc tiếp theo hướng ưu tiên cốp rộng hơn hay ưu tiên dễ chống chân hơn.";
                }

                if (profile.WantsLargeStorage)
                {
                    return "Bạn muốn mình lọc tiếp theo hướng cốp rộng tối đa hay ưu tiên mẫu cân bằng hơn giữa cốp rộng và giá?";
                }

                if (profile.NeedsLowSeat)
                {
                    return "Bạn muốn mình nghiêng hơn về nhóm dễ chống chân hay nhóm đi làm thực dụng hơn?";
                }

                return "Nếu muốn mình lọc sát hơn, mình có thể chốt tiếp theo hướng xe ga dễ đi hoặc xe số thực dụng hơn.";
            }

            if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand))
            {
                return $"Nếu muốn, mình có thể lọc sâu hơn riêng trong nhóm {profile.PreferredBrand} để chọn ra mẫu hợp nhất với nhu cầu hiện tại.";
            }

            if (profile?.ForWork == true && profile?.WantsLargeStorage == true)
            {
                return "Bạn muốn mình ưu tiên hơn về cốp rộng hay ưu tiên cảm giác gọn nhẹ khi đi làm hằng ngày?";
            }

            if (profile?.ForWork == true)
            {
                return "Nếu muốn mình lọc sát hơn, mình có thể nghiêng tiếp theo hướng thực dụng tiết kiệm xăng hoặc kiểu dáng đẹp hơn để đi làm.";
            }

            if (profile?.NeedsLowSeat == true)
            {
                return "Bạn muốn mình lọc tiếp theo hướng dễ chống chân nhất hay cân bằng hơn giữa dáng đẹp và dễ đi?";
            }

            if (profile?.WantsLargeStorage == true)
            {
                return "Bạn muốn mình lọc thêm theo hướng cốp rộng nhất hay ưu tiên mẫu nhìn gọn và nữ tính hơn?";
            }

            if (text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang"))
            {
                return "Bạn muốn mình lọc tiếp theo hướng tiết kiệm xăng nhất hay cân bằng hơn giữa giá và tiện ích?";
            }

            if (text.Contains("cá tính") || text.Contains("ca tinh") ||
                text.Contains("thể thao") || text.Contains("the thao"))
            {
                return "Nếu muốn mình lọc sát hơn, bạn có thể nói thêm muốn thiên về dáng thể thao mạnh hơn hay vẫn ưu tiên dễ đi hằng ngày.";
            }

            if (!parsedIntent.TargetPrice.HasValue && !parsedIntent.PriceMin.HasValue && !parsedIntent.PriceMax.HasValue)
            {
                return "Bạn nói thêm giúp mình tầm giá mong muốn là mình lọc sát hơn ngay.";
            }

            return "Nếu muốn mình lọc sát hơn nữa, bạn cứ nói thêm 1 tiêu chí quan trọng nhất như cốp rộng, dễ chống chân, tiết kiệm xăng hoặc hãng muốn ưu tiên.";
        }

        private static string BuildProductSuggestionContext(IEnumerable<ProductSummaryDto> items)
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

        private sealed class ToolFirstConsultationResult
        {
            public string ToolName { get; set; } = ToolNames.GetProductsByFilters;
            public string EffectivePrompt { get; set; } = string.Empty;
            public string? Reply { get; set; }
        }
        private static string? BuildConsultationConclusion(
    IReadOnlyList<ProductSummaryDto> items,
    string text,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            if (items == null || items.Count == 0)
                return null;

            var top = items.First();

            if (profile?.NeedsLowSeat == true)
            {
                var lowSeatCandidate = items.FirstOrDefault(x =>
                    x.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    x.Ten.Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                    x.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase));

                if (lowSeatCandidate != null)
                {
                    top = lowSeatCandidate;
                }
            }

            if (profile?.WantsLargeStorage == true)
            {
                if (top.Ten.Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    top.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                    top.Ten.Contains("Lead", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Nếu ưu tiên cốp rộng để đi làm hoặc mang đồ hằng ngày, mình thấy **{top.Ten}** là lựa chọn nổi bật hơn.";
                }
            }

            if (profile?.ForWork == true)
            {
                return $"Nếu xét riêng nhu cầu đi làm hằng ngày, mình thấy **{top.Ten}** đang là mẫu nổi bật nhất trong nhóm này.";
            }

            if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand))
            {
                return $"Trong nhóm {profile.PreferredBrand}, mình đang nghiêng hơn về **{top.Ten}** ở thời điểm hiện tại.";
            }

            if (parsedIntent.TargetPrice.HasValue)
            {
                return $"Nếu cần mình chốt nhanh 1 mẫu nổi bật nhất trong tầm này, mình đang nghiêng về **{top.Ten}**.";
            }

            return null;
        }
    }
}
