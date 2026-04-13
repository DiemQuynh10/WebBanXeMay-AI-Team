using System.Diagnostics;
using System.Globalization;
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
        private readonly IChatFlowRouter _chatFlowRouter;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;

        public ChatService(IOpenAIService openAIService, IWebBanXeMayToolClient toolClient, ILogger<ChatService> logger, IQueryNormalizationService queryNormalizationService, IClarificationStateService clarificationStateService, IRagService ragService, IPriceIntentParser priceIntentParser, IIntentParserService intentParserService, IChatFlowRouter chatFlowRouter, IProductRecommendationService productRecommendationService, IConversationPreferenceService conversationPreferenceService)
        {
            _openAIService = openAIService;
            _toolClient = toolClient;
            _logger = logger;
            _queryNormalizationService = queryNormalizationService;
            _clarificationStateService = clarificationStateService;
            _ragService = ragService;
            _priceIntentParser = priceIntentParser;
            _intentParserService = intentParserService;
            _chatFlowRouter = chatFlowRouter;
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
                    else if (ShouldMergeWithPendingClarification(originalMessage, pendingClarification))
                    {
                        originalMessage = MergeClarificationIntoPending(pendingClarification, originalMessage);
                        _clarificationStateService.Clear(conversationId);

                        _logger.LogInformation(
                            "Merged clarification fragment into pending query. ConversationId: {ConversationId}, MergedMessage: {Message}",
                            conversationId,
                            originalMessage);
                    }
                    else if (LooksLikeNewStandaloneQuery(originalMessage))
                    {
                        _logger.LogInformation(
                            "Pending clarification cleared because user sent a new standalone query. ConversationId: {ConversationId}, NewMessage: {Message}",
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
                        originalMessage = MergeClarificationIntoPending(pendingClarification, originalMessage);
                        _clarificationStateService.Clear(conversationId);

                        _logger.LogInformation(
                            "Fallback merged user reply into pending query. ConversationId: {ConversationId}, MergedMessage: {Message}",
                            conversationId,
                            originalMessage);
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

                if (IsBotIdentityQuestion(normalizedMessage))
                {
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Mình là trợ lý tư vấn xe máy của WebBanXeMay. Mình có thể giúp bạn tra giá, kiểm tra tồn kho và gợi ý mẫu xe phù hợp nhu cầu.",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

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

                // Route the flow using parser + profile to get deterministic vs AI decisions
                var routing = _chatFlowRouter.Route(normalizedMessage, parsedIntent, conversationProfile);
                _logger.LogInformation(
                    "Routing result. Flow: {FlowType}, Deterministic: {Deterministic}, ShouldUseRag: {ShouldUseRag}, AiFallback: {AiFallback}, Reason: {Reason}",
                    routing.FlowType,
                    routing.ShouldUseDeterministicFlow,
                    routing.ShouldUseRag,
                    routing.ShouldUseAiFallback,
                    routing.Reason);

                NormalizeRequestMetadata(request);

                _logger.LogInformation(
                    "Processing chat message. ConversationId: {ConversationId}, Channel: {Channel}, UserId: {UserId}",
                    request.ConversationId,
                    request.Channel,
                    request.UserId);

                if (routing.FlowType == ChatFlowType.Greeting)
                {
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Chào bạn, mình là trợ lý tư vấn xe máy của WebBanXeMay. Bạn muốn xem giá, kiểm tra tồn kho hay nhờ mình gợi ý mẫu xe phù hợp?",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                if (routing.FlowType == ChatFlowType.OutOfScope)
                {
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Mình hiện chỉ hỗ trợ các nội dung về xe máy, sản phẩm và đơn hàng trên WebBanXeMay. Bạn cần mình tư vấn mẫu xe hoặc tra giá/tồn kho mẫu nào không?",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

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
                                Products = consultationResponse.Products,
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

                bool useTool = ShouldUseTool(normalizedMessage) || ShouldUseToolAndRag(normalizedMessage) || routing.ShouldUseDeterministicFlow;
                bool useRag = ShouldUseRag(normalizedMessage) || ShouldUseToolAndRag(normalizedMessage) || routing.ShouldUseRag;

                if (useRag)
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

                _logger.LogInformation(
                    "Preparing AI context. ConversationId: {ConversationId}, IntentType: {IntentType}, ParsedRoute: {ParsedRoute}, RoutedFlow: {RoutedFlow}, useTool: {UseTool}, useRag: {UseRag}",
                    conversationId,
                    parsedIntent.IntentType,
                    parsedIntent.RouteFlow,
                    routing.FlowType,
                    useTool,
                    useRag);

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

                if ((string.IsNullOrWhiteSpace(aiResult.Reply) || !aiResult.Success)
                    && !string.IsNullOrWhiteSpace(ragContext))
                {
                    aiResult.Reply = BuildRagOnlyReply(ragContext);
                    aiResult.Success = true;
                    aiResult.UsedAI = false;
                    aiResult.ErrorMessage = null;
                }

                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Reply))
                {
                    aiResult.Reply = BuildAiFailureFallbackReply(normalizedMessage, routing, parsedIntent);
                    aiResult.Success = true;
                    aiResult.UsedAI = false;
                    aiResult.ErrorMessage = null;
                }

                aiResult.ConversationId = conversationId;
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
        private static bool ShouldResetContextForFreshConsultation(
    string message,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile existingProfile)
        {
            if (existingProfile == null)
                return false;

            bool hasOldContext =
                existingProfile.TurnCount > 0 ||
                existingProfile.HasActiveRecommendationContext ||
                !string.IsNullOrWhiteSpace(existingProfile.PreferredBrand) ||
                !string.IsNullOrWhiteSpace(existingProfile.PreferredCategory) ||
                existingProfile.TargetPrice.HasValue ||
                existingProfile.PriceMin.HasValue ||
                existingProfile.PriceMax.HasValue ||
                existingProfile.HeightCm.HasValue;

            if (!hasOldContext)
                return false;

            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            bool looksLikeFollowUp =
                IsFollowUpPreferenceFragment(text) ||
                text.StartsWith("còn ") ||
                text.StartsWith("không thích ") ||
                text.StartsWith("không muốn ") ||
                text.StartsWith("ưu tiên ") ||
                text.StartsWith("né ") ||
                text.StartsWith("con nào ") ||
                parsedIntent.IntentType == "followup" ||
                parsedIntent.IntentType == "refine" ||
                parsedIntent.IntentType == "compare";

            if (looksLikeFollowUp)
                return false;

            bool looksLikeFreshStandalone =
                text.StartsWith("tư vấn") ||
                text.StartsWith("xe ") ||
                text.StartsWith("mình ") ||
                text.StartsWith("cho mình ") ||
                text.StartsWith("tôi ") ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.HeightCm.HasValue ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category);

            return looksLikeFreshStandalone;
        }

        private static string BuildAiFailureFallbackReply(
            string normalizedMessage,
            FlowRoutingResult routing,
            ParsedIntent parsedIntent)
        {
            if (routing.FlowType == ChatFlowType.Greeting)
            {
                return "Chào bạn, mình là trợ lý tư vấn xe máy của WebBanXeMay. Bạn muốn xem giá, kiểm tra tồn kho hay cần mình gợi ý mẫu xe phù hợp?";
            }

            if (routing.FlowType == ChatFlowType.OutOfScope)
            {
                return "Mình hiện chỉ hỗ trợ các nội dung về xe máy, sản phẩm và đơn hàng trên WebBanXeMay. Bạn có thể hỏi mình về giá, tồn kho hoặc mẫu xe phù hợp nhé.";
            }

            if (parsedIntent.IsOrderLookup || IsOrderLookupIntent(normalizedMessage))
            {
                return "Để mình kiểm tra đơn hàng giúp bạn, bạn vui lòng gửi mã đơn và số điện thoại đặt hàng nhé.";
            }

            if (IsConsultationIntent(normalizedMessage)
                || routing.FlowType == ChatFlowType.Recommendation
                || routing.FlowType == ChatFlowType.BrandSwitch
                || routing.FlowType == ChatFlowType.Refinement
                || routing.FlowType == ChatFlowType.RecommendationFollowUp)
            {
                if (!string.IsNullOrWhiteSpace(parsedIntent.Brand))
                {
                    return $"Mình vẫn có thể tư vấn xe {parsedIntent.Brand} cho bạn. Bạn cho mình thêm 1 tiêu chí ngắn như tầm giá hoặc nhu cầu đi học/đi làm, mình sẽ lọc sát hơn ngay.";
                }

                return "Mình vẫn có thể tư vấn mẫu xe phù hợp cho bạn. Bạn cho mình thêm 1 tiêu chí ngắn như hãng muốn ưu tiên, tầm giá hoặc nhu cầu sử dụng để mình gợi ý sát hơn nhé.";
            }

            if (parsedIntent.IsDirectProductLookup || parsedIntent.IsProductSearch || LooksLikeToolQuery(normalizedMessage))
            {
                return "Mình vẫn có thể hỗ trợ tra dữ liệu sản phẩm. Bạn thử gửi câu ngắn như: giá Vision bao nhiêu, tồn kho Wave còn không, hoặc Honda dưới 40 triệu nhé.";
            }

            return "Mình vẫn đang sẵn sàng hỗ trợ. Bạn có thể hỏi về giá xe, tồn kho, tư vấn mẫu phù hợp hoặc tra cứu đơn hàng nhé.";
        }

        private static bool LooksLikeToolQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            string[] toolKeywords =
            {
                "giá", "còn hàng", "tồn kho", "có sẵn", "bao nhiêu",
                "dưới", "trên", "tầm", "khoảng", "quanh", "triệu"
            };

            return toolKeywords.Any(k => text.Contains(k));
        }

        private static string BuildRagOnlyReply(string ragContext)
        {
            var cleaned = ragContext.Trim();
            if (cleaned.Length <= 1200)
            {
                return cleaned;
            }

            return cleaned.Substring(0, 1200).Trim() + "...";
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

                var effectiveBrand = !string.IsNullOrWhiteSpace(parsedIntent.Brand)
                    ? parsedIntent.Brand
                    : conversationProfile.PreferredBrand;

                decimal? toolMinPrice = parsedIntent.PriceMin ?? conversationProfile.PriceMin;
                decimal? toolMaxPrice = parsedIntent.PriceMax ?? conversationProfile.PriceMax;

                if ((parsedIntent.FilterType == PriceFilterType.Around && parsedIntent.TargetPrice.HasValue) ||
                    (conversationProfile.FilterType == PriceFilterType.Around && conversationProfile.TargetPrice.HasValue))
                {
                    var target = parsedIntent.TargetPrice ?? conversationProfile.TargetPrice ?? 0;
                    toolMinPrice = Math.Max(0, target - 10_000_000m);
                    toolMaxPrice = target + 10_000_000m;
                }

                var selectedToolName = ToolNames.GetProductsByFilters;
                // If user mentioned a specific product, prefer searching by keyword
                ProductSearchResponseDto? toolResult = null;

                if (parsedIntent.MentionedProducts != null && parsedIntent.MentionedProducts.Count > 0)
                {
                    var keyword = parsedIntent.MentionedProducts.First();
                    _logger.LogInformation("Tool-first consultation: searching by product keyword. ConversationId: {ConversationId}, Keyword: {Keyword}", conversationId, keyword);
                    toolResult = await _toolClient.SearchProductsAsync(keyword, take);
                    selectedToolName = ToolNames.SearchProducts;
                    if (toolResult == null || toolResult.Items == null || !toolResult.Items.Any())
                    {
                        _logger.LogInformation("SearchProducts returned no data, falling back to filters. ConversationId: {ConversationId}, Keyword: {Keyword}", conversationId, keyword);
                    }
                }

                // If no product-specific results, and there is price filter, call price-range API for better performance
                if ((toolResult == null || toolResult.Items == null || !toolResult.Items.Any())
                    && (toolMinPrice.HasValue || toolMaxPrice.HasValue)
                    && string.IsNullOrWhiteSpace(effectiveBrand))
                {
                    _logger.LogInformation("Tool-first consultation: using price-range API. ConversationId: {ConversationId}, Min: {Min}, Max: {Max}", conversationId, toolMinPrice, toolMaxPrice);
                    toolResult = await _toolClient.GetProductsByPriceRangeAsync(toolMinPrice, toolMaxPrice, take);
                    selectedToolName = ToolNames.GetProductsByPriceRange;
                }

                if (toolResult == null || toolResult.Items == null || !toolResult.Items.Any())
                {
                    toolResult = await _toolClient.GetProductsByFiltersAsync(
                        brand: effectiveBrand,
                        minPrice: toolMinPrice,
                        maxPrice: toolMaxPrice,
                        category: categoryForTool,
                        take: take);
                    selectedToolName = ToolNames.GetProductsByFilters;
                }

                if ((toolResult == null || toolResult.Items == null || !toolResult.Items.Any()) && !string.IsNullOrWhiteSpace(categoryForTool))
                {
                    _logger.LogInformation(
                        "Tool-first consultation retry without category filter. ConversationId: {ConversationId}, Category: {Category}",
                        conversationId,
                        categoryForTool);

                    toolResult = await _toolClient.GetProductsByFiltersAsync(
                        brand: effectiveBrand,
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
                        brand: effectiveBrand,
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
                    ToolName = selectedToolName,
                    EffectivePrompt = effectivePrompt,
                    Reply = deterministicReply,
                    Products = BuildProductCardsForResponse(rankedItems, normalizedMessage)
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

        private static bool LooksLikeNewStandaloneQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            // Những mảnh bổ sung thì KHÔNG tính là query mới
            if (IsFollowUpPreferenceFragment(text))
                return false;

            // Mẫu query mới rõ ràng
            if (Regex.IsMatch(text, @"^(xe|tư vấn|tu van|mình muốn|toi muon|tôi muốn|cho mình|giá|bao nhiêu|còn hàng|so sánh|tra đơn|kiểm tra đơn|đơn hàng)\b",
                RegexOptions.IgnoreCase))
            {
                return true;
            }

            string[] strongKeywords =
            {
        "xe", "honda", "yamaha", "suzuki", "sym", "piaggio",
        "vision", "air blade", "ab", "vario", "janus", "sirius",
        "giá", "bao nhiêu", "còn hàng", "tồn kho",
        "tư vấn", "phù hợp", "nên mua", "đơn hàng", "mã đơn"
    };

            var matched = strongKeywords.Count(k => text.Contains(k));
            if (matched >= 2)
                return true;

            if (text.EndsWith("?") && text.Length >= 10)
                return true;

            return false;
        }
        private static bool IsWeakAmbiguousReply(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return true;

            var text = message.Trim().ToLowerInvariant();

            return text is "?" or "sao" or "gì" or "hả" or "ừm";
        }

        private static bool IsBotIdentityQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return text.Contains("bạn tên gì")
                || text.Contains("ban ten gi")
                || text.Contains("tên bạn là gì")
                || text.Contains("ten ban la gi")
                || text.Contains("bạn là ai")
                || text.Contains("ban la ai")
                || text.Contains("ai vậy")
                || text.Contains("ai vay");
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
                || text.Contains("tu van")
|| text.Contains("phu hop")
|| text.Contains("di lam")
|| text.Contains("di hoc")
|| text.Contains("tiet kiem xang")
|| text.Contains("cop rong")
|| text.Contains("de chong chan")
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
                || text.Contains("côn tay")
            || text.Contains("thanh lich")
|| text.Contains("nu tinh")
|| text.Contains("mem mai");
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
    || LooksLikeBudgetFragment(text)
    || text.Contains("triệu")
    || text.Contains("triêu")
    || text.Contains("trieu")
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
    LooksLikeBudgetFragment(text) ||
    text.Contains("triệu") ||
    text.Contains("triêu") ||
    text.Contains("trieu") ||
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
            // If message contains price/realtime keywords, use tool
            var checkText = message?.ToLowerInvariant() ?? string.Empty;
            string[] toolKeywords = { "giá", "còn hàng", "tồn kho", "có sẵn", "bao nhiêu", "dưới", "trên", "tầm", "khoảng", "quanh", "triệu" };
            if (toolKeywords.Any(k => checkText.Contains(k)))
                return true;

            var safeMessage = message ?? string.Empty;
            var text = safeMessage.ToLowerInvariant();

                bool currentMessageLooksLikeConsultation =
            IsConsultationIntent(safeMessage)
            || IsFollowUpPreferenceFragment(text)
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
            || text.Contains("dễ chống chân")
            || text.Contains("de chong chan")
            || text.Contains("yên thấp")
            || text.Contains("yen thap")
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

            if (text.Contains("tư vấn") || LooksLikeBudgetFragment(text) || text.Contains("khoảng") || text.Contains("tầm") || text.Contains("quanh"))
                return 32;

            if (text.Contains("cá tính") || text.Contains("thể thao") || text.Contains("đi phố") || text.Contains("tiết kiệm xăng"))
                return 32;

            return 24;
        }

        private static int GetRecommendationTake(string message, ParsedIntent parsedIntent)
        {
            var text = message.ToLowerInvariant();

            bool openConsultation =
    LooksLikeBudgetFragment(text) ||
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
    LooksLikeBudgetFragment(text) ||
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
        private static bool ShouldMergeWithPendingClarification(string message, string pendingClarification)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(pendingClarification))
                return false;

            var text = message.Trim().ToLowerInvariant();

            if (IsAffirmative(text) || IsNegative(text) || IsWeakAmbiguousReply(text))
                return false;

            if (IsOrderLookupIntent(text) || ExtractOrderId(text).HasValue || !string.IsNullOrWhiteSpace(ExtractPhone(text)))
                return false;

            if (IsFollowUpPreferenceFragment(text))
                return true;

            // Các mảnh rất ngắn sau câu hỏi làm rõ thường là câu bổ sung, không phải câu mới
            if (text.Length <= 40 && !LooksLikeNewStandaloneQuery(text))
                return true;

            return false;
        }

        private static string MergeClarificationIntoPending(string pendingClarification, string fragment)
        {
            var pending = (pendingClarification ?? string.Empty).Trim().TrimEnd('.', '?', '!', ',');
            var extra = (fragment ?? string.Empty).Trim().TrimStart(',', '.', ' ');

            if (string.IsNullOrWhiteSpace(pending))
                return extra;

            if (string.IsNullOrWhiteSpace(extra))
                return pending;

            return $"{pending}, {extra}";
        }

        private static bool IsFollowUpPreferenceFragment(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            if (Regex.IsMatch(
    text,
    @"\b(tầm|khoảng|quanh)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
    RegexOptions.IgnoreCase))
            {
                return true;
            }

            if (Regex.IsMatch(
                text,
                @"^\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                RegexOptions.IgnoreCase))
            {
                return true;
            }

            // category fragment
            if (text is "xe ga" or "ga" or "xe số" or "xe so" or "số" or "so" or "côn tay" or "xe côn" or "xe con")
                return true;

            // brand fragment
            if (text is "honda" or "yamaha" or "suzuki" or "sym" or "piaggio")
                return true;

            // need fragment
            if (text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("yên thấp") ||
                text.Contains("yen thap") ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("đi học") ||
                text.Contains("di hoc") ||
                text.Contains("nữ") ||
                text.Contains("nam"))
            {
                return true;
            }

            // follow-up brand / preference
            if (text.StartsWith("còn ") ||
                text.StartsWith("ưu tiên ") ||
                text.StartsWith("thích ") ||
                text.StartsWith("không thích ") ||
                text.StartsWith("không muốn ") ||
                text.StartsWith("né "))
            {
                return true;
            }

            return false;
        }
        private static bool LooksLikeBudgetFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(
                       text,
                       @"\b(tầm|khoảng|quanh)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"^\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase);
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
                "ưu nhược điểm", "tiết kiệm xăng", "xe ga", "xe số", "đi học", "đi làm",
                "sinh viên", "đi làm", "nữ", "nam", "cốp rộng", "dễ chống chân",
                "cá tính", "thể thao", "đi phố", "di pho",
                "chính sách", "bảo hiểm", "giao hàng", "đổi trả", "khuyến mãi",
                "bảo dưỡng", "sửa chữa", "đăng ký", "sang tên", "giấy tờ",
                "lãi suất", "thời hạn", "hồ sơ", "quy trình", "điều kiện",
                "phí", "miễn phí", "ưu đãi", "giảm giá", "tặng"
            };

            return ragKeywords.Any(k => text.Contains(k)) && !ShouldUseTool(message);
        }

        private bool ShouldUseTool(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.ToLowerInvariant();

            string[] toolKeywords =
            {
                "giá", "còn hàng", "tồn kho", "có sẵn", "bao nhiêu",
                "dưới", "trên", "tầm", "khoảng", "quanh", "triệu"
            };

            return toolKeywords.Any(k => text.Contains(k));
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
    LooksLikeBudgetFragment(text) ||
    text.Contains("triệu") ||
    text.Contains("triêu") ||
    text.Contains("trieu") ||
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

            var intro = BuildNaturalIntro(text, selectedItems.Count);

            var lines = new List<string>();

            foreach (var item in selectedItems)
            {
                var baseReason = BuildProductReason(item, text, parsedIntent, profile);

                string finalReason = baseReason;
                if (ragReasonHints != null &&
                    ragReasonHints.TryGetValue(item.Ten ?? string.Empty, out var ragHint) &&
                    !string.IsNullOrWhiteSpace(ragHint))
                {
                    finalReason = MergeReason(baseReason, ragHint);
                }

                lines.Add($"- **{item.Ten}** ({item.Gia:N0} VNĐ): {finalReason}");
            }

            var suggestion = BuildSoftSuggestion(selectedItems, text, parsedIntent, profile);
            var followUp = BuildFollowUpQuestion(text, parsedIntent, profile);

            var result = new StringBuilder();
            result.AppendLine(intro);
            result.AppendLine();
            result.AppendLine(string.Join("\n", lines));

            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                result.AppendLine();
                result.AppendLine(suggestion);
            }

            if (!string.IsNullOrWhiteSpace(followUp))
            {
                result.AppendLine();
                result.AppendLine(followUp);
            }

            return result.ToString().Trim();
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
                    if (normalizedContext.Contains("dễ chống chân") || normalizedContext.Contains("yên thấp") || normalizedContext.Contains("gọn"))
                        hints.Add("hợp nếu bạn ưu tiên dáng gọn và dễ chống chân");
                    else if (normalizedContext.Contains("dễ làm quen") || normalizedContext.Contains("dễ đi"))
                        hints.Add("dễ làm quen và hợp đi hằng ngày");
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
                    else if (normalizedContext.Contains("tiện ích") || normalizedContext.Contains("cốp rộng"))
                        hints.Add("cân bằng khá tốt giữa dáng đẹp và tiện ích");
                }

                if (lowerName.Contains("zip"))
                {
                    if (normalizedContext.Contains("yên thấp") || normalizedContext.Contains("dễ chống chân") || normalizedContext.Contains("thấp"))
                        hints.Add("rất đáng cân nhắc nếu bạn ưu tiên yên thấp và dễ chống chân");
                    else if (normalizedContext.Contains("gọn") || normalizedContext.Contains("nhỏ con"))
                        hints.Add("dáng xe nhỏ gọn, hợp người có vóc dáng nhỏ");
                }

                if (lowerName.Contains("air blade"))
                {
                    if (normalizedContext.Contains("đầm") || normalizedContext.Contains("mạnh"))
                        hints.Add("hợp hơn nếu bạn muốn cảm giác xe đầm và khỏe hơn");
                    else if (normalizedContext.Contains("đi làm"))
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
            var finalParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(baseReason))
            {
                finalParts.AddRange(
                    baseReason.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            if (!string.IsNullOrWhiteSpace(ragHint))
            {
                var ragParts = ragHint.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var part in ragParts)
                {
                    var lowerPart = part.ToLowerInvariant();

                    bool duplicated = finalParts.Any(existing =>
                    {
                        var lowerExisting = existing.ToLowerInvariant();

                        return lowerExisting == lowerPart
                            || (lowerExisting.Contains("nữ") && lowerPart.Contains("nữ"))
                            || (lowerExisting.Contains("yên thấp") && lowerPart.Contains("dễ chống chân"))
                            || (lowerExisting.Contains("dễ chống chân") && lowerPart.Contains("yên thấp"))
                            || (lowerExisting.Contains("cốp rộng") && lowerPart.Contains("mang đồ"))
                            || (lowerExisting.Contains("đi làm") && lowerPart.Contains("đi làm"))
                            || (lowerExisting.Contains("đi học") && lowerPart.Contains("sinh viên"))
                            || (lowerExisting.Contains("nhỏ gọn") && lowerPart.Contains("gọn"))
                            || (lowerExisting.Contains("dễ đi") && lowerPart.Contains("dễ làm quen"));
                    });

                    if (!duplicated)
                    {
                        finalParts.Add(part);
                    }

                    if (finalParts.Count >= 2)
                        break;
                }
            }

            return string.Join(", ", finalParts.Take(2));
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
            var name = item.Ten ?? string.Empty;
            var category = item.Loai?.Trim() ?? string.Empty;

            string? budgetReason = null;
            string? signatureReason = null;

            bool asksForFemale = text.Contains("nữ");
            bool asksForMale = text.Contains("nam");
            bool asksForSchool = text.Contains("sinh viên") || text.Contains("đi học");
            bool asksForWork = text.Contains("đi làm");
            bool asksForLowSeat = text.Contains("dễ chống chân") || text.Contains("yên thấp") || text.Contains("người thấp") || text.Contains("nhỏ con");
            bool asksForLargeStorage = text.Contains("cốp rộng") || text.Contains("mang đồ");
            bool asksForFuelSaving = text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang");
            bool asksForSporty = text.Contains("cá tính") || text.Contains("ca tinh") || text.Contains("thể thao") || text.Contains("the thao");

            var targetPrice = parsedIntent.TargetPrice ?? profile?.TargetPrice;

            if (targetPrice.HasValue)
            {
                var diff = Math.Abs(item.Gia - targetPrice.Value);

                if (diff <= 2_000_000m)
                {
                    budgetReason = "giá khá sát ngân sách";
                }
                else if (diff <= 4_000_000m)
                {
                    budgetReason = item.Gia <= targetPrice.Value
                        ? "giá vẫn khá gần ngân sách"
                        : "giá nhỉnh hơn ngân sách một chút";
                }
                else if (diff <= 8_000_000m)
                {
                    budgetReason = item.Gia < targetPrice.Value
                        ? "giá mềm hơn khá nhiều so với mức bạn đang cân nhắc"
                        : "giá cao hơn ngân sách khá rõ";
                }
            }
            else if (parsedIntent.PriceMax.HasValue && item.Gia <= parsedIntent.PriceMax.Value)
            {
                budgetReason = "nằm trong tầm giá bạn đang cân nhắc";
            }

            if (name.Contains("Latte", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForFemale)
                    signatureReason = "dáng xe mềm và khá hợp nhu cầu nữ";
                else if (asksForLargeStorage)
                    signatureReason = "cân bằng khá tốt giữa tiện ích và kiểu dáng";
                else if (asksForLowSeat)
                    signatureReason = "dễ làm quen và khá hợp đi phố";
                else
                    signatureReason = "là mẫu khá dễ đi và thiên về sự thanh lịch";
            }
            else if (name.Contains("Zip", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForLowSeat)
                    signatureReason = "rất đáng cân nhắc nếu bạn ưu tiên yên thấp và dễ chống chân";
                else if (text.Contains("thanh lịch") || text.Contains("thanh lich"))
                    signatureReason = "dáng xe nhỏ gọn và khá hợp nếu bạn thích phong cách thanh lịch";
                else if (asksForFemale)
                    signatureReason = "dáng nhỏ gọn, hợp với người thích xe gọn nhẹ";
                else
                    signatureReason = "dáng xe nhỏ gọn, khá hợp người có vóc dáng nhỏ";
            }
            else if (name.Contains("Attila", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Venus", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForFemale)
                    signatureReason = "hợp nếu bạn thích kiểu dáng nữ tính và thanh lịch";
                else if (asksForWork)
                    signatureReason = "khá hợp đi phố hằng ngày theo hướng nhẹ nhàng và dễ đi";
                else
                    signatureReason = "là mẫu xe ga thiên về sự thanh lịch và dễ đi";
            }
            else if (name.Contains("Shark", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForFemale)
                    signatureReason = "dáng xe gọn và khá hợp nhu cầu nữ đi phố";
                else if (asksForSchool)
                    signatureReason = "khá dễ đi và hợp đi lại hằng ngày";
                else
                    signatureReason = "là mẫu xe ga gọn khá dễ làm quen";
            }
            else if (name.Contains("Grande", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForFemale)
                    signatureReason = "hợp nếu bạn thích kiểu dáng nữ tính và mềm mại hơn";
                else
                    signatureReason = "thiên về cảm giác đi êm và phong cách thanh lịch";
            }
            else if (name.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForLowSeat)
                    signatureReason = "đáng chú ý hơn nếu bạn ưu tiên dáng gọn và dễ chống chân";
                else if (asksForSchool)
                    signatureReason = "dễ đi, gọn và khá hợp đi học hằng ngày";
                else
                    signatureReason = "dễ làm quen và hợp đi hằng ngày";
            }
            else if (name.Contains("Freego", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForLargeStorage)
                    signatureReason = "thiên về nhóm cốp rộng, tiện mang đồ";
                else if (asksForWork)
                    signatureReason = "khá hợp với nhu cầu đi làm thực dụng hằng ngày";
                else
                    signatureReason = "là lựa chọn thực dụng khá dễ cân nhắc";
            }
            else if (name.Contains("Air Blade", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForSporty)
                    signatureReason = "hợp hơn nếu bạn thích kiểu dáng nổi bật và cá tính";
                else if (asksForWork || asksForMale)
                    signatureReason = "khá hợp cho nhu cầu đi làm hằng ngày";
                else
                    signatureReason = "hợp hơn nếu bạn muốn cảm giác xe đầm và khỏe hơn";
            }
            else if (name.Contains("Future", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForWork)
                    signatureReason = "thiên về hướng xe số thực dụng và ổn định";
                else if (asksForFuelSaving)
                    signatureReason = "khá hợp nếu bạn ưu tiên xe số bền và tiết kiệm";
                else
                    signatureReason = "là mẫu xe số thực dụng, dễ dùng lâu dài";
            }
            else if (name.Contains("Wave", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForSchool || asksForFuelSaving)
                    signatureReason = "hợp nếu bạn ưu tiên chi phí sử dụng thấp và dễ đi hằng ngày";
                else
                    signatureReason = "hợp nếu bạn ưu tiên chi phí sử dụng thấp";
            }
            else if (name.Contains("Sirius", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForSchool)
                    signatureReason = "khá hợp cho nhu cầu đi học hằng ngày";
                else if (asksForFuelSaving)
                    signatureReason = "thiên về nhóm xe số tiết kiệm và dễ bảo dưỡng";
                else
                    signatureReason = "là mẫu xe số khá dễ cân nhắc trong tầm phổ thông";
            }
            else if (name.Contains("GD110", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForSchool || asksForFuelSaving)
                    signatureReason = "khá hợp nếu bạn ưu tiên xe số tiết kiệm và dễ dùng hằng ngày";
                else if (asksForWork)
                    signatureReason = "thiên về hướng xe số thực dụng và bền bỉ";
                else
                    signatureReason = "là mẫu xe số thực dụng khá dễ cân nhắc";
            }
            else if (name.Contains("Address", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForLargeStorage)
                    signatureReason = "có lợi thế ở nhóm xe ga gọn và khá tiện mang đồ";
                else
                    signatureReason = "là mẫu xe ga gọn nhẹ khá thực dụng";
            }
            else if (name.Contains("Impulse", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForBudgetFriendly(text: text))
                    signatureReason = "là phương án ga chi phí mềm khá dễ cân nhắc";
                else
                    signatureReason = "giá mềm và khá dễ tiếp cận trong nhóm xe ga";
            }

            if (string.IsNullOrWhiteSpace(signatureReason))
            {
                if (!string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                    category.Contains(parsedIntent.Category, StringComparison.OrdinalIgnoreCase))
                {
                    signatureReason = $"đúng hướng {category.ToLowerInvariant()} bạn đang tìm";
                }
                else if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand) &&
                         string.Equals(item.ThuongHieu, profile.PreferredBrand, StringComparison.OrdinalIgnoreCase))
                {
                    signatureReason = $"đúng hãng {profile.PreferredBrand} bạn đang muốn xem";
                }
            }

            if (string.IsNullOrWhiteSpace(signatureReason))
            {
                if (!asksForWork && profile?.ForWork == true && name.Contains("Air Blade", StringComparison.OrdinalIgnoreCase))
                {
                    signatureReason = "là lựa chọn khá cân bằng cho nhu cầu đi lại hằng ngày";
                }
                else if (!asksForLowSeat && profile?.NeedsLowSeat == true &&
                         (name.Contains("Vision", StringComparison.OrdinalIgnoreCase) || name.Contains("Zip", StringComparison.OrdinalIgnoreCase)))
                {
                    signatureReason = "khá dễ làm quen và hợp đi phố";
                }
                else if (!asksForLargeStorage && profile?.WantsLargeStorage == true &&
                         (name.Contains("Freego", StringComparison.OrdinalIgnoreCase) || name.Contains("Latte", StringComparison.OrdinalIgnoreCase)))
                {
                    signatureReason = "khá tiện nếu bạn cần chở thêm đồ hằng ngày";
                }
            }

            var reasons = new List<string>();

            if (!string.IsNullOrWhiteSpace(budgetReason) &&
                !budgetReason.Contains("cao hơn ngân sách khá rõ", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(budgetReason);
            }

            if (!string.IsNullOrWhiteSpace(signatureReason))
            {
                reasons.Add(signatureReason);
            }

            if (reasons.Count == 0 && !string.IsNullOrWhiteSpace(budgetReason))
            {
                reasons.Add(budgetReason);
            }

            if (reasons.Count == 0)
                return "là lựa chọn khá đáng cân nhắc trong nhóm đang lọc";

            return string.Join(", ", reasons.Take(2));

            static bool asksForBudgetFriendly(string text)
            {
                return text.Contains("rẻ") || text.Contains("giá mềm") || text.Contains("tiết kiệm");
            }
        }
        private static string BuildFollowUpQuestion(
     string text,
     ParsedIntent parsedIntent,
     CustomerPreferenceProfile? profile = null)
        {
            bool asksForFemale = text.Contains("nữ");
            bool asksForMale = text.Contains("nam");
            bool asksForSchool = text.Contains("sinh viên") || text.Contains("đi học");
            bool asksForWork = text.Contains("đi làm");
            bool asksForLowSeat = text.Contains("dễ chống chân") || text.Contains("yên thấp") || text.Contains("người thấp") || text.Contains("nhỏ con");
            bool asksForLargeStorage = text.Contains("cốp rộng") || text.Contains("mang đồ");
            bool asksForFuelSaving = text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang");
            bool asksForSporty = text.Contains("cá tính") || text.Contains("ca tinh") || text.Contains("thể thao") || text.Contains("the thao");
            bool hasBudget =
    parsedIntent.TargetPrice.HasValue ||
    parsedIntent.PriceMin.HasValue ||
    parsedIntent.PriceMax.HasValue ||
    LooksLikeBudgetFragment(text) ||
    text.Contains("tầm") ||
    text.Contains("khoảng") ||
    text.Contains("quanh") ||
    text.Contains("triệu") ||
    text.Contains("triêu") ||
    text.Contains("trieu");

            if (asksForSchool)
            {
                return "Bạn muốn mình lọc kỹ hơn theo hướng tiết kiệm xăng, cốp rộng hay kiểu dáng gọn nhẹ cho dễ đi học?";
            }

            if (asksForWork)
            {
                return "Bạn muốn mình lọc tiếp theo hướng thực dụng tiết kiệm xăng hay ưu tiên dáng đẹp và đi đầm hơn?";
            }

            if (asksForLowSeat)
            {
                return "Bạn muốn mình nghiêng hơn về nhóm dễ chống chân nhất hay nhóm cân bằng hơn giữa dễ đi và kiểu dáng?";
            }

            if (asksForLargeStorage)
            {
                return "Bạn muốn mình ưu tiên cốp rộng tối đa hay cân bằng hơn giữa cốp rộng và kiểu dáng đẹp?";
            }

            if (asksForFuelSaving)
            {
                return "Bạn muốn mình lọc tiếp theo hướng tiết kiệm xăng nhất hay cân bằng hơn giữa tiết kiệm và tiện ích?";
            }

            if (asksForSporty)
            {
                return "Bạn muốn kiểu cá tính rõ hơn hay vẫn giữ tiêu chí dễ đi hằng ngày để mình lọc sát hơn?";
            }

            if (asksForFemale)
            {
                return "Bạn muốn mình nghiêng hơn về dáng gọn dễ đi hay kiểu mềm mại đẹp dáng hơn?";
            }

            if (asksForMale)
            {
                return "Bạn muốn mình lọc tiếp theo hướng đầm chắc hơn hay ưu tiên linh hoạt đi phố hằng ngày?";
            }

            if (!hasBudget)
            {
                return "Bạn nói thêm giúp mình tầm giá mong muốn là mình lọc sát hơn ngay.";
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Brand))
            {
                return $"Nếu muốn, mình có thể lọc tiếp sâu hơn riêng trong nhóm {parsedIntent.Brand} để chọn ra mẫu hợp nhất.";
            }

            if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand))
            {
                return $"Nếu muốn, mình có thể lọc sâu hơn riêng trong nhóm {profile.PreferredBrand} để chọn ra mẫu hợp nhất với nhu cầu hiện tại.";
            }

            if (profile?.ExcludedCategories.Contains("côn tay") == true)
            {
                return "Nếu muốn mình lọc sát hơn nữa, mình có thể chốt tiếp theo hướng xe ga dễ đi hoặc xe số thực dụng hơn.";
            }

            if (profile?.WantsLargeStorage == true)
            {
                return "Nếu muốn mình lọc sát hơn nữa, mình có thể nghiêng tiếp theo hướng cốp rộng hơn hoặc dáng gọn dễ đi hơn.";
            }

            if (profile?.NeedsLowSeat == true)
            {
                return "Nếu muốn mình lọc sát hơn nữa, mình có thể nghiêng tiếp theo hướng dễ chống chân nhất hoặc cân bằng hơn giữa dáng đẹp và dễ đi.";
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
            public List<ChatProductCard>? Products { get; set; }
        }
    //    private static string? BuildConsultationConclusion(
    //IReadOnlyList<ProductSummaryDto> items,
    //string text,
    //ParsedIntent parsedIntent,
    //CustomerPreferenceProfile? profile = null)
    //    {
    //        if (items == null || items.Count == 0)
    //            return null;

    //        var top = items.First();

    //        if (profile?.NeedsLowSeat == true)
    //        {
    //            var lowSeatCandidate = items.FirstOrDefault(x =>
    //                x.Ten.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
    //                x.Ten.Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
    //                x.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase));

    //            if (lowSeatCandidate != null)
    //            {
    //                top = lowSeatCandidate;
    //            }
    //        }

    //        if (profile?.WantsLargeStorage == true)
    //        {
    //            if (top.Ten.Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
    //                top.Ten.Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
    //                top.Ten.Contains("Lead", StringComparison.OrdinalIgnoreCase))
    //            {
    //                return $"Nếu ưu tiên cốp rộng để đi làm hoặc mang đồ hằng ngày, mình thấy **{top.Ten}** là lựa chọn nổi bật hơn.";
    //            }
    //        }

    //        if (profile?.ForWork == true)
    //        {
    //            return $"Nếu xét riêng nhu cầu đi làm hằng ngày, mình thấy **{top.Ten}** đang là mẫu nổi bật nhất trong nhóm này.";
    //        }

    //        if (!string.IsNullOrWhiteSpace(profile?.PreferredBrand))
    //        {
    //            return $"Trong nhóm {profile.PreferredBrand}, mình đang nghiêng hơn về **{top.Ten}** ở thời điểm hiện tại.";
    //        }

    //        if (parsedIntent.TargetPrice.HasValue)
    //        {
    //            return $"Nếu cần mình chốt nhanh 1 mẫu nổi bật nhất trong tầm này, mình đang nghiêng về **{top.Ten}**.";
    //        }

    //        return null;
    //    }
        private static string BuildNaturalIntro(string text, int count)
        {
            if (count == 1)
                return "Mình thấy hiện tại có 1 mẫu khá hợp với nhu cầu bạn đang hỏi:";

            if (text.Contains("sinh viên"))
                return $"Với nhu cầu này, mình thấy có {count} mẫu khá dễ cân nhắc:";

            if (text.Contains("nữ"))
                return $"Mình lọc ra {count} mẫu khá hợp với nhu cầu của bạn:";

            if (LooksLikeBudgetFragment(text) || text.Contains("tầm") || text.Contains("khoảng") || text.Contains("quanh"))
                return $"Trong tầm giá này, mình thấy có {count} mẫu khá ổn để bạn cân nhắc:";

            return $"Mình gợi ý bạn {count} mẫu để tham khảo:";
        }

        private static List<ChatProductCard>? BuildProductCardsForResponse(
            IReadOnlyList<ProductSummaryDto> items,
            string normalizedMessage)
        {
            if (items == null || items.Count == 0)
            {
                return null;
            }

            var text = (normalizedMessage ?? string.Empty).ToLowerInvariant();
            var maxItems = IsOpenConsultationQuery(text) ? 4 : 3;

            return items
                .Take(Math.Min(maxItems, items.Count))
                .Select(item => new ChatProductCard
                {
                    Id = item.Id,
                    Ten = item.Ten,
                    Slug = item.Slug,
                    Gia = item.Gia,
                    SoLuong = item.SoLuong,
                    CC = item.CC?.ToString(CultureInfo.InvariantCulture),
                    ImageUrl = item.ImageUrl,
                    ThuongHieu = item.ThuongHieu,
                    Loai = item.Loai
                })
                .ToList();
        }
        private static string? BuildSoftSuggestion(
    IReadOnlyList<ProductSummaryDto> items,
    string text,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile)
        {
            if (items == null || items.Count == 0)
                return null;

            ProductSummaryDto top = items.First();

            bool asksForLowSeat = text.Contains("dễ chống chân") || text.Contains("yên thấp") || text.Contains("người thấp") || text.Contains("nhỏ con");
            bool asksForSchool = text.Contains("sinh viên") || text.Contains("đi học");
            bool asksForWork = text.Contains("đi làm");
            bool asksForSporty = text.Contains("cá tính") || text.Contains("ca tinh") || text.Contains("thể thao") || text.Contains("the thao");
            bool asksForLargeStorage = text.Contains("cốp rộng") || text.Contains("mang đồ");
            bool asksForElegant =
    text.Contains("thanh lịch") ||
    text.Contains("thanh lich") ||
    text.Contains("nữ tính") ||
    text.Contains("nu tinh") ||
    text.Contains("mềm mại") ||
    text.Contains("mem mai");

            if (asksForLowSeat)
            {
                var lowSeatCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Latte", StringComparison.OrdinalIgnoreCase));

                if (lowSeatCandidate != null)
                    top = lowSeatCandidate;

                return $"Nếu bạn ưu tiên dễ chống chân thì mình nghiêng hơn về **{top.Ten}**.";
            }

            if (asksForLargeStorage)
            {
                var storageCandidate = items.FirstOrDefault(x =>
    (x.Ten ?? "").Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
    (x.Ten ?? "").Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
    (x.Ten ?? "").Contains("Address", StringComparison.OrdinalIgnoreCase));

                if (storageCandidate != null)
                    top = storageCandidate;

                return $"Nếu bạn ưu tiên cốp rộng và sự tiện dụng thì **{top.Ten}** là mẫu đáng để cân nhắc hơn.";
            }
            if (asksForElegant)
            {
                var elegantCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Attila", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Grande", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Zip", StringComparison.OrdinalIgnoreCase));

                if (elegantCandidate != null)
                    top = elegantCandidate;

                return $"Nếu bạn ưu tiên kiểu thanh lịch và dễ đi thì **{top.Ten}** là mẫu đáng để cân nhắc hơn.";
            }
            if (asksForSchool)
            {
                var schoolCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Sirius", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Wave", StringComparison.OrdinalIgnoreCase));

                if (schoolCandidate != null)
                    top = schoolCandidate;

                return $"Nếu chọn theo hướng dễ đi, dễ dùng hằng ngày thì **{top.Ten}** là lựa chọn khá ổn.";
            }

            if (asksForWork)
            {
                var workCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Air Blade", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Future", StringComparison.OrdinalIgnoreCase));

                if (workCandidate != null)
                    top = workCandidate;

                return $"Nếu dùng đi làm hằng ngày thì **{top.Ten}** sẽ hợp hơn.";
            }

            if (asksForSporty)
            {
                var sportyCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Air Blade", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Vario", StringComparison.OrdinalIgnoreCase));

                if (sportyCandidate != null)
                    top = sportyCandidate;

                return $"Nếu bạn thích kiểu nổi bật hơn thì **{top.Ten}** sẽ hợp gu hơn.";
            }

            if (parsedIntent.TargetPrice.HasValue)
            {
                return $"Nếu cần chốt nhanh một mẫu nổi bật trong tầm này thì mình đang nghiêng về **{top.Ten}**.";
            }

            return null;
        }
    }
}
