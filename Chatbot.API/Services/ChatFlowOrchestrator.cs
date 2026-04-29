using System;
using System.Diagnostics;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Conversation;
using Chatbot.API.Services.Interfaces;
using System.Linq;
using Microsoft.Extensions.Logging;
namespace Chatbot.API.Services
{
    public class ChatFlowOrchestrator : IChatFlowOrchestrator
    {
        private readonly ILogger<ChatFlowOrchestrator> _logger;
        private readonly IClarificationStateService _clarificationStateService;
        private readonly IQueryNormalizationService _queryNormalizationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IPriceIntentParser _priceIntentParser;
        private readonly IIntentParserService _intentParserService;
        private readonly ILLMIntentUnderstandingService _llmIntentUnderstandingService;
        private readonly IChatFlowRouter _chatFlowRouter;
        private readonly IFlowDecisionService _flowDecisionService;
        private readonly IProductLookupFlowService _productLookupFlowService;
        private readonly IProductSearchFlowService _productSearchFlowService;
        private readonly IRefinementService _refinementService;
        private readonly ICompareService _compareService;
        private readonly IOpenAIService _openAIService;
        private readonly IRecommendationFlowService _recommendationFlowService;
        private readonly IOrderLookupFlowService _orderLookupFlowService;
        private readonly IConversationPolicyService _conversationPolicyService;
        private readonly IConversationStateService _conversationStateService;
        private readonly ITurnContextBuilder _turnContextBuilder;
        private readonly IContextualIntentClassifierService _contextualIntentClassifier;
        public ChatFlowOrchestrator(
    ILogger<ChatFlowOrchestrator> logger,
    IClarificationStateService clarificationStateService,
    IQueryNormalizationService queryNormalizationService,
    IConversationPreferenceService conversationPreferenceService,
    IPriceIntentParser priceIntentParser,
    IIntentParserService intentParserService,
    ILLMIntentUnderstandingService llmIntentUnderstandingService,
    IChatFlowRouter chatFlowRouter,
    IFlowDecisionService flowDecisionService,
    IProductLookupFlowService productLookupFlowService,
    IProductSearchFlowService productSearchFlowService,
    IRefinementService refinementService,
    ICompareService compareService,
    IRecommendationFlowService recommendationFlowService,
    IOrderLookupFlowService orderLookupFlowService,
    IOpenAIService openAIService,
    IConversationStateService conversationStateService,
    ITurnContextBuilder turnContextBuilder,
    IContextualIntentClassifierService contextualIntentClassifier,
    IConversationPolicyService conversationPolicyService)
        {
            _logger = logger;
            _clarificationStateService = clarificationStateService;
            _queryNormalizationService = queryNormalizationService;
            _conversationPreferenceService = conversationPreferenceService;
            _priceIntentParser = priceIntentParser;
            _intentParserService = intentParserService;
            _llmIntentUnderstandingService = llmIntentUnderstandingService;
            _chatFlowRouter = chatFlowRouter;
            _flowDecisionService = flowDecisionService;
            _productLookupFlowService = productLookupFlowService;
            _productSearchFlowService = productSearchFlowService;
            _refinementService = refinementService;
            _compareService = compareService;
            _recommendationFlowService = recommendationFlowService;
            _orderLookupFlowService = orderLookupFlowService;
            _openAIService = openAIService;
            _conversationStateService = conversationStateService;
            _turnContextBuilder = turnContextBuilder;
            _contextualIntentClassifier = contextualIntentClassifier;
            _conversationPolicyService = conversationPolicyService;
        }

        public async Task<ChatResponse> HandleAsync(ChatRequest request)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));
                NormalizeRequestMetadata(request);
                var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                    ? Guid.NewGuid().ToString()
                    : request.ConversationId.Trim();

                request.ConversationId = conversationId;

                if (TryHandlePendingClarification(
         request,
         conversationId,
         stopwatch.ElapsedMilliseconds,
         out var earlyResponse))
                {
                    return earlyResponse!;
                }

                var context = await BuildContextAsync(request);

                var response = await ExecuteFlowAsync(context);

                response.ConversationId ??= context.ConversationId;
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;

                await PersistConversationStateAsync(context, response);

                return response;
            }
            catch (ClarificationRequiredException ex)
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = request?.ConversationId,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Reply = ex.Message
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatFlowOrchestrator failed.");

                return new ChatResponse
                {
                    Success = false,
                    UsedAI = false,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Reply = "Xin lỗi, hệ thống đang gặp lỗi tạm thời.",
                    ErrorMessage = "orchestrator_error"
                };
            }
        }

        private async Task<ChatOrchestrationContext> BuildContextAsync(ChatRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Message))
                throw new ArgumentException("Message không được để trống.", nameof(request));

            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString()
                : request.ConversationId.Trim();

            request.ConversationId = conversationId;

            var originalMessage = request.Message.Trim();

            var state = await _conversationStateService.GetAsync(
                conversationId,
                request.Channel,
                request.UserId);

            var existingProfile = await _conversationPreferenceService.GetAsync(conversationId)
                      ?? new CustomerPreferenceProfile();
            HydrateProfileFromState(existingProfile, state);

            var normalizedMessage = NormalizeMessageOrThrowClarification(
                conversationId,
                originalMessage);

            var parsedIntent = await ParseIntentAsync(normalizedMessage, existingProfile);
            var isFreshRecommendationByCurrentMessage =
    IsFreshRecommendationRequest(normalizedMessage, parsedIntent);
            if (isFreshRecommendationByCurrentMessage &&
    !MessageHasExplicitPrice(normalizedMessage))
            {
                parsedIntent.PriceMin = null;
                parsedIntent.PriceMax = null;
                parsedIntent.TargetPrice = null;
                parsedIntent.FilterType = PriceFilterType.None;
            }
            if (isFreshRecommendationByCurrentMessage &&
    MessageHasExplicitCategory(normalizedMessage) &&
    !MessageHasExplicitPrice(normalizedMessage))
            {
                parsedIntent.PriceMin = null;
                parsedIntent.PriceMax = null;
                parsedIntent.TargetPrice = null;
                parsedIntent.FilterType = PriceFilterType.None;
            }
            if (isFreshRecommendationByCurrentMessage)
            {
                if (!MessageMentionsWork(normalizedMessage))
                    parsedIntent.ForWork = false;

                if (!MessageMentionsSchool(normalizedMessage))
                    parsedIntent.ForSchool = false;

                if (!MessageMentionsFuelSaving(normalizedMessage))
                    parsedIntent.WantsFuelSaving = false;

                if (!MessageMentionsLargeStorage(normalizedMessage))
                    parsedIntent.WantsLargeStorage = false;

                if (!MessageMentionsEasyControl(normalizedMessage))
                {
                    parsedIntent.WantsEasyControl = false;
                    parsedIntent.NeedsLowSeat = false;
                }
            }
            if (ShouldRecoverPriceFollowUp(normalizedMessage, parsedIntent, existingProfile))
            {
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                parsedIntent.IntentType = "refine";
                parsedIntent.IsFollowUp = true;
                parsedIntent.HasNarrowRefinementSignal = true;

                ApplyPriceIntent(parsedIntent, normalizedMessage);
            }
            if (MessageAsksForCheaperOption(normalizedMessage) &&
    existingProfile.HasActiveRecommendationContext)
            {
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                parsedIntent.IntentType = "refine";
                parsedIntent.IsFollowUp = true;
                parsedIntent.HasNarrowRefinementSignal = true;
                parsedIntent.ComparisonFeature = "price";
            }
            var turnContext = await _turnContextBuilder.BuildAsync(
     conversationId,
     normalizedMessage,
     parsedIntent,
     existingProfile,
     state);

            var effectiveIntent = turnContext.EffectiveIntent ?? parsedIntent;
            if (MessageAsksForCheaperOption(normalizedMessage))
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.HasNarrowRefinementSignal = true;
                effectiveIntent.ComparisonFeature = "price";
            }
            if (isFreshRecommendationByCurrentMessage)
            {
                effectiveIntent.PriceMin = parsedIntent.PriceMin;
                effectiveIntent.PriceMax = parsedIntent.PriceMax;
                effectiveIntent.TargetPrice = parsedIntent.TargetPrice;
                effectiveIntent.FilterType = parsedIntent.FilterType;

                effectiveIntent.Brand = parsedIntent.Brand;
                effectiveIntent.Category = parsedIntent.Category;
                effectiveIntent.Target = parsedIntent.Target;

                effectiveIntent.ForWork = parsedIntent.ForWork;
                effectiveIntent.ForSchool = parsedIntent.ForSchool;
                effectiveIntent.ForCity = parsedIntent.ForCity;
                effectiveIntent.ForTour = parsedIntent.ForTour;

                effectiveIntent.WantsFuelSaving = parsedIntent.WantsFuelSaving;
                effectiveIntent.WantsLargeStorage = parsedIntent.WantsLargeStorage;
                effectiveIntent.WantsEasyControl = parsedIntent.WantsEasyControl;
                effectiveIntent.NeedsLowSeat = parsedIntent.NeedsLowSeat;

                effectiveIntent.PrefersMaleStyle = parsedIntent.PrefersMaleStyle;
                effectiveIntent.PrefersFemaleStyle = parsedIntent.PrefersFemaleStyle;

                effectiveIntent.ComparisonFeature = parsedIntent.ComparisonFeature;
                effectiveIntent.RequestedStyles = parsedIntent.RequestedStyles;

                effectiveIntent.IsFollowUp = false;
                effectiveIntent.HasNarrowRefinementSignal = false;
                effectiveIntent.HasExpandRecommendationSignal = false;
                effectiveIntent.HasFreshConsultationSignal = true;
            }
            if (effectiveIntent.IsOutOfScope ||
    effectiveIntent.IsNoise ||
    effectiveIntent.IsAck ||
    effectiveIntent.IsGreeting)
            {
                return new ChatOrchestrationContext
                {
                    Request = request,
                    ConversationId = conversationId,
                    OriginalMessage = originalMessage,
                    NormalizedMessage = normalizedMessage,
                    ExistingProfile = existingProfile,
                    State = state,
                    ParsedIntent = parsedIntent,
                    EffectiveIntent = effectiveIntent,
                    BaseRouting = new FlowRoutingResult
                    {
                        FlowType = effectiveIntent.IsGreeting ? ChatFlowType.Greeting :
                                   effectiveIntent.IsOutOfScope ? ChatFlowType.OutOfScope :
                                   ChatFlowType.Unknown,
                        Reason = "EarlyIntentGuard"
                    },
                    FinalRouting = new FlowRoutingResult
                    {
                        FlowType = effectiveIntent.IsGreeting ? ChatFlowType.Greeting :
                                   effectiveIntent.IsOutOfScope ? ChatFlowType.OutOfScope :
                                   ChatFlowType.Unknown,
                        Reason = "EarlyIntentGuard",
                        ShouldUseAiFallback = false
                    }
                };
            }
            var contextDecision = MapTurnContextToRecommendationDecision(turnContext, effectiveIntent);

            var (mergedProfile, baseRouting, finalRouting) = await BuildRoutingAsync(
    conversationId,
    normalizedMessage,
    effectiveIntent,
    contextDecision,
    isFreshRecommendationByCurrentMessage);
            if (effectiveIntent != null &&
    MessageHasExplicitCategory(normalizedMessage) &&
    !MessageHasExplicitPrice(normalizedMessage))
            {
                mergedProfile.PriceMin = null;
                mergedProfile.PriceMax = null;
                mergedProfile.TargetPrice = null;
                mergedProfile.FilterType = PriceFilterType.None;

                effectiveIntent.PriceMin = null;
                effectiveIntent.PriceMax = null;
                effectiveIntent.TargetPrice = null;
                effectiveIntent.FilterType = PriceFilterType.None;
            }
            if (effectiveIntent != null &&
     MessageHasExplicitCategory(normalizedMessage) &&
     !MessageHasExplicitBrand(normalizedMessage) &&
     isFreshRecommendationByCurrentMessage)
            {
                mergedProfile.PreferredBrand = null;
                effectiveIntent.Brand = null;
            }

            finalRouting = ApplyRoutingBiasFromTurnContext(
    finalRouting,
    turnContext,
    effectiveIntent,
    mergedProfile);
            finalRouting = ForceExitCompareContextForRecommendationFollowUp(
    normalizedMessage,
    effectiveIntent,
    mergedProfile,
    finalRouting);

            finalRouting = await ApplyContextualClassifierAsync(
                conversationId,
                normalizedMessage,
                effectiveIntent,
                mergedProfile,
                finalRouting);

            LogContextSummary(
                conversationId,
                parsedIntent,
                effectiveIntent,
                turnContext,
                finalRouting,
                mergedProfile);

            return new ChatOrchestrationContext
            {
                Request = request,
                ConversationId = conversationId,
                OriginalMessage = originalMessage,
                NormalizedMessage = normalizedMessage,
                ExistingProfile = mergedProfile,
                State = state,
                ParsedIntent = parsedIntent,
                EffectiveIntent = effectiveIntent,
                BaseRouting = baseRouting,
                FinalRouting = finalRouting
            };
        }
        private async Task<ChatResponse> ExecuteFlowAsync(ChatOrchestrationContext context)
        {
            var deterministicResult = await ExecuteDeterministicFlowAsync(context);
            if (deterministicResult != null)
                return deterministicResult;

            var forcedCompareResult = await TryHandleForcedCompareFollowUpAsync(context);
            if (forcedCompareResult != null)
                return forcedCompareResult;

            _logger.LogWarning(
     "No flow returned a result. ConversationId={ConversationId}, FlowType={FlowType}, Message={Message}",
     context.ConversationId,
     context.FinalRouting?.FlowType ?? ChatFlowType.Unknown,
     context.NormalizedMessage);

            var aiFallback = await TryAiFallbackAsync(context);
            if (aiFallback != null)
                return aiFallback;

            return new ChatResponse
            {
                Success = true,
                UsedAI = false,
                ConversationId = context.ConversationId,
                Reply = "Mình chưa xử lý trọn vẹn câu này theo ngữ cảnh hiện tại. Bạn thử nói rõ hơn một chút như tên xe đang quan tâm, hãng muốn lọc hoặc tiêu chí muốn ưu tiên nhé."
            };
        }
        private async Task<ChatResponse?> TryAiFallbackAsync(ChatOrchestrationContext context)
        {
            if (!context.FinalRouting.ShouldUseAiFallback)
                return null;

            try
            {
                var aiResponse = await _openAIService.AskAsync(new AIRequestContext
                {
                    ConversationId = context.ConversationId,
                    OriginalUserMessage = context.Request?.Message ?? context.NormalizedMessage,
                    EffectivePrompt = BuildFallbackPrompt(
                        context.NormalizedMessage,
                        context.ExistingProfile),
                    Channel = "web"
                });

                if (aiResponse != null && aiResponse.Success && !string.IsNullOrWhiteSpace(aiResponse.Reply))
                {
                    aiResponse.ConversationId ??= context.ConversationId;
                    aiResponse.UsedAI = true;
                    return aiResponse;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "AI fallback failed. ConversationId={ConversationId}, Message={Message}",
                    context.ConversationId,
                    context.NormalizedMessage);
            }

            return null;
        }
        private async Task<ChatResponse?> ExecuteDeterministicFlowAsync(ChatOrchestrationContext context)
        {
            _logger.LogWarning(
    "FINAL ROUTING => ConversationId={ConversationId}, FlowType={FlowType}, Reason={Reason}, Message={Message}",
    context.ConversationId,
    context.FinalRouting?.FlowType,
    context.FinalRouting?.Reason,
    context.NormalizedMessage);
            var flowType = context.FinalRouting?.FlowType ?? ChatFlowType.Unknown;

            if (string.Equals(flowType, ChatFlowType.Greeting, StringComparison.OrdinalIgnoreCase))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Xin chào 👋 Mình có thể hỗ trợ bạn tra cứu giá xe, kiểm tra tồn kho, tư vấn mẫu xe phù hợp hoặc tra cứu đơn hàng."
                };
            }

            if (LooksLikeHumanSupportOrAfterSalesRequest(context.NormalizedMessage))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = BuildHumanSupportReply(context.NormalizedMessage)
                };
            }
            if (string.Equals(flowType, ChatFlowType.OutOfScope, StringComparison.OrdinalIgnoreCase))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Mình hiện chỉ hỗ trợ về xe máy, sản phẩm trong hệ thống và tra cứu đơn hàng. Bạn cứ hỏi mình về mẫu xe, giá, còn hàng hay tư vấn chọn xe nhé."
                };
            }

            if (context.EffectiveIntent?.IsNoise == true)
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Mình chưa hiểu ý bạn. Bạn thử hỏi rõ hơn như: giá Vision bao nhiêu, còn Air Blade không, hoặc tư vấn xe cho nữ tầm 40 triệu nhé."
                };
            }

            if (context.EffectiveIntent?.IsAck == true)
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Bạn muốn mình hỗ trợ tiếp theo hướng nào: tra giá xe, kiểm tra tồn kho, so sánh xe hay tư vấn mẫu phù hợp?"
                };
            }

            if (string.Equals(flowType, ChatFlowType.OrderLookup, StringComparison.OrdinalIgnoreCase))
            {
                return await _orderLookupFlowService.HandleAsync(
                    context.Request,
                    context.NormalizedMessage);
            }

            if (string.Equals(flowType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
            {
                return await _productLookupFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }

            if (string.Equals(flowType, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Executing PRODUCT_SEARCH flow");

                return await _productSearchFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }

            if (string.Equals(flowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase))
            {
                return await _refinementService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }

            if (string.Equals(flowType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
            {
                return await _compareService.CompareAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }

            if (string.Equals(flowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Executing RECOMMENDATION flow");

                return await _recommendationFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }

            return null;
        }
       
        private async Task<ChatResponse?> TryHandleForcedCompareFollowUpAsync(ChatOrchestrationContext context)
        {
            if (!_conversationPolicyService.ShouldForceCompareFollowUp(
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile))
            {
                return null;
            }

            _logger.LogInformation(
                "Force compare follow-up in orchestrator. ConversationId={ConversationId}, Message={Message}",
                context.ConversationId,
                context.NormalizedMessage);

            return await _compareService.CompareAsync(
                context.ConversationId,
                context.NormalizedMessage,
                context.EffectiveIntent,
                context.ExistingProfile);
        }
        private static bool ShouldUseContextualClassifier(
      string normalizedMessage,
      ParsedIntent intent,
      CustomerPreferenceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage) || intent == null || profile == null)
                return false;

            if (intent.IsGreeting || intent.IsNoise || intent.IsAck || intent.IsOutOfScope)
                return false;

            bool hasContext =
                profile.HasActiveRecommendationContext ||
                profile.HasActiveCompareContext;

            if (!hasContext)
                return false;

            var text = NormalizeText(normalizedMessage);

            bool hasExplicitCompare =
                text.Contains("so sanh") ||
                text.Contains("so voi") ||
                text.Contains("khac nhau") ||
                intent.IsDirectCompare ||
                string.Equals(intent.IntentType, "compare", StringComparison.OrdinalIgnoreCase);

            if (hasExplicitCompare)
                return false;

            return true;
        }
        private static string BuildFallbackPrompt(
    string normalizedMessage,
    CustomerPreferenceProfile? profile)
        {
            var profileSummary = profile == null
                ? "Chưa có hồ sơ hội thoại trước đó."
                : $"Ngữ cảnh trước đó: " +
                  $"PreferredBrand={profile.PreferredBrand ?? "null"}, " +
                  $"PreferredCategory={profile.PreferredCategory ?? "null"}, " +
                  $"Target={profile.Target ?? "null"}, " +
                  $"PriceMin={profile.PriceMin?.ToString() ?? "null"}, " +
                  $"PriceMax={profile.PriceMax?.ToString() ?? "null"}, " +
                  $"TargetPrice={profile.TargetPrice?.ToString() ?? "null"}, " +
                  $"ActiveFlow={profile.ActiveFlow ?? "null"}.";

            return
 $@"Bạn là chatbot tư vấn xe máy cho website bán xe.
Hãy trả lời ngắn gọn, đúng trọng tâm, tự nhiên và chỉ trong phạm vi:
- tra cứu giá xe
- kiểm tra tồn kho
- tư vấn chọn xe
- so sánh xe
- tra cứu đơn hàng
- hướng dẫn gặp nhân viên khi người dùng hỏi về bảo hành, lỗi xe, đổi trả, giao hàng, thanh toán hoặc khiếu nại

Nếu người dùng hỏi về bảo hành, lỗi xe, đổi trả, giao hàng, thanh toán hoặc khiếu nại:
- không được nói “mình không hỗ trợ”
- hãy trả lời đồng cảm, ngắn gọn
- hướng người dùng bấm “Gặp nhân viên” để được kiểm tra chi tiết
- không tự cam kết bảo hành, hoàn tiền hoặc xử lý đơn nếu không có dữ liệu

Nếu tin nhắn thật sự ngoài phạm vi xe máy, sản phẩm, tồn kho, so sánh xe, đơn hàng hoặc hỗ trợ sau bán:
- hãy nói lịch sự rằng bạn chỉ hỗ trợ trong phạm vi website bán xe máy.

Nếu tin nhắn vô nghĩa, quá mơ hồ hoặc không đủ để hiểu:
- không được tự suy diễn theo ngữ cảnh cũ
- hãy yêu cầu người dùng nói lại rõ hơn.

Chỉ dùng ngữ cảnh hội thoại cũ khi tin nhắn hiện tại thật sự là follow-up rõ ràng.
Không bịa thông tin tồn kho, giá hay đơn hàng nếu không chắc.
Ưu tiên trả lời bằng tiếng Việt thân thiện.

{profileSummary}

Tin nhắn người dùng: {normalizedMessage}";
        }
    
        private static ClarificationResolution ResolveClarificationReply(
    string userReply,
    string pendingNormalizedMessage)
        {
            var text = (userReply ?? string.Empty).Trim().ToLowerInvariant();

            var confirmWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "đúng",
        "dung",
        "đúng rồi",
        "dung roi",
        "đúng đó",
        "dung do",
        "phải",
        "phai",
        "phải rồi",
        "phai roi",
        "ok",
        "oke",
        "đúng nhé",
        "dung nhe",
        "chuẩn",
        "chuan"
    };

            var rejectWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "không",
        "khong",
        "không đúng",
        "khong dung",
        "sai",
        "sai rồi",
        "sai roi",
        "không phải",
        "khong phai",
        "không đúng rồi",
        "khong dung roi"
    };

            if (confirmWords.Contains(text))
            {
                return ClarificationResolution.Confirm(pendingNormalizedMessage);
            }

            if (rejectWords.Contains(text))
            {
                return ClarificationResolution.Reject();
            }

            if (!string.IsNullOrWhiteSpace(text) &&
     text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8)
            {
                var merged = $"{pendingNormalizedMessage} {userReply.Trim()}".Trim();
                return ClarificationResolution.Expand(merged);
            }

            return ClarificationResolution.Reject();

        }
        private sealed class ClarificationRequiredException : Exception
        {
            public ClarificationRequiredException(string message) : base(message)
            {
            }
        }
        private sealed class ClarificationResolution
        {
            public bool IsConfirmed { get; private set; }
            public bool IsRejected { get; private set; }
            public string? ResolvedMessage { get; private set; }

            public static ClarificationResolution Confirm(string resolvedMessage)
                => new ClarificationResolution
                {
                    IsConfirmed = true,
                    ResolvedMessage = resolvedMessage
                };

            public static ClarificationResolution Reject()
                => new ClarificationResolution
                {
                    IsRejected = true
                };

            public static ClarificationResolution Expand(string resolvedMessage)
                => new ClarificationResolution
                {
                    ResolvedMessage = resolvedMessage
                };

            public static ClarificationResolution None()
                => new ClarificationResolution();
        }
        private bool TryHandlePendingClarification(
    ChatRequest request,
    string conversationId,
    long elapsedMs,
    out ChatResponse? earlyResponse)
        {
            earlyResponse = null;

            var originalMessage = request.Message?.Trim() ?? string.Empty;
            var pendingClarification = _clarificationStateService.GetPending(conversationId);

            if (string.IsNullOrWhiteSpace(pendingClarification))
                return false;

            var clarificationResolution = ResolveClarificationReply(originalMessage, pendingClarification);

            if (clarificationResolution.IsConfirmed)
            {
                request.Message = clarificationResolution.ResolvedMessage!;
                _clarificationStateService.Clear(conversationId);

                _logger.LogInformation(
                    "Clarification confirmed. ConversationId={ConversationId}, ResolvedMessage={ResolvedMessage}",
                    conversationId,
                    request.Message);

                return false;
            }

            if (clarificationResolution.IsRejected)
            {
                _clarificationStateService.Clear(conversationId);

                earlyResponse = new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    ElapsedMs = elapsedMs,
                    Reply = "Không sao nhé. Bạn hãy nhập lại giúp mình tên xe hoặc câu hỏi rõ hơn một chút, ví dụ: \"Honda Vision giá bao nhiêu\" hoặc \"xe ga cho nữ khoảng 40 triệu\"."
                };

                return true;
            }

            if (!string.IsNullOrWhiteSpace(clarificationResolution.ResolvedMessage))
            {
                request.Message = clarificationResolution.ResolvedMessage!;
                _clarificationStateService.Clear(conversationId);

                _logger.LogInformation(
                    "Clarification expanded by user follow-up. ConversationId={ConversationId}, ResolvedMessage={ResolvedMessage}",
                    conversationId,
                    request.Message);
            }

            return false;
        }
        private async Task<ParsedIntent> ParseIntentAsync(
    string normalizedMessage,
    CustomerPreferenceProfile? existingProfile)
        {
            var parsedIntent = await ParseBaseIntentAsync(normalizedMessage);

            ApplyPriceIntent(parsedIntent, normalizedMessage);

            if (!ShouldCallLlmIntent(parsedIntent, normalizedMessage, existingProfile))
                return parsedIntent;

            return await EnrichIntentWithLlmAsync(parsedIntent, normalizedMessage, existingProfile);
        }
        private async Task<ParsedIntent> ParseBaseIntentAsync(string normalizedMessage)
        {
            return await _intentParserService.ParseAsync(normalizedMessage);
        }
        private bool ShouldCallLlmIntent(
      ParsedIntent parsedIntent,
      string normalizedMessage,
      CustomerPreferenceProfile? existingProfile)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage) || parsedIntent == null)
                return false;

            // Các intent thật sự chắc chắn thì không cần LLM
            if (parsedIntent.IsGreeting ||
                parsedIntent.IsOutOfScope ||
                parsedIntent.IsNoise ||
                parsedIntent.IsAck ||
                LooksLikeThanksIntent(normalizedMessage))
                return false;

            if (parsedIntent.IsOrderLookup)
                return false;

            if (parsedIntent.IsDirectProductLookup &&
                parsedIntent.MentionedProducts != null &&
                parsedIntent.MentionedProducts.Count > 0)
                return false;

            if (parsedIntent.IsDirectCompare &&
                parsedIntent.MentionedProducts != null &&
                parsedIntent.MentionedProducts.Count >= 2)
                return false;

            // Nếu đang có ngữ cảnh tư vấn thì nên cho LLM hiểu câu follow-up
            bool hasRecommendationContext =
                existingProfile?.HasActiveRecommendationContext == true ||
                existingProfile?.LastRecommendedProducts?.Count > 0 ||
                existingProfile?.BaseRecommendedProducts?.Count > 0;

            if (hasRecommendationContext &&
                (
                    parsedIntent.IsFollowUp ||
                    parsedIntent.IsOpenRecommendation ||
                    string.Equals(parsedIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(parsedIntent.IntentType, "refine", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                    parsedIntent.PriceMin.HasValue ||
                    parsedIntent.PriceMax.HasValue ||
                    parsedIntent.TargetPrice.HasValue ||
                    parsedIntent.WantsFuelSaving ||
                    parsedIntent.WantsLargeStorage ||
                    parsedIntent.WantsEasyControl ||
                    parsedIntent.NeedsLowSeat
                ))
            {
                return true;
            }

            if (string.Equals(parsedIntent.IntentType, "unknown", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsAmbiguousContextDependentFollowUp(parsedIntent, normalizedMessage, existingProfile))
                return true;

            if (HasWeakRecommendationUnderstanding(parsedIntent))
                return true;

            if (string.Equals(parsedIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(parsedIntent.IntentType, "refine", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
        private static bool IsDeterministicIntent(
      ParsedIntent parsedIntent,
      string normalizedMessage)
        {
            if (parsedIntent.IsGreeting || parsedIntent.IsOutOfScope || LooksLikeThanksIntent(normalizedMessage))
                return true;

            if (parsedIntent.IsOrderLookup)
                return true;

            if (parsedIntent.IsDirectProductLookup &&
                parsedIntent.MentionedProducts != null &&
                parsedIntent.MentionedProducts.Count > 0)
            {
                return true;
            }

            if (parsedIntent.IsDirectCompare &&
                parsedIntent.MentionedProducts != null &&
                parsedIntent.MentionedProducts.Count >= 2)
            {
                return true;
            }

            if (parsedIntent.IsProductSearch &&
                FlowIntentHeuristics.IsHardFilterOnlySearch(parsedIntent, normalizedMessage))
            {
                return true;
            }

            return false;
        }
        private static bool LooksLikeThanksIntent(string normalizedMessage)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return false;

            var message = normalizedMessage.Trim().ToLowerInvariant();

            return message == "cảm ơn"
                || message == "cam on"
                || message == "cảm ơn nhé"
                || message == "cam on nhe"
                || message == "thanks"
                || message == "thank you"
                || message == "ok cảm ơn"
                || message == "oke cảm ơn";
        }
        private static bool HasWeakRecommendationUnderstanding(ParsedIntent parsedIntent)
        {
            return string.Equals(parsedIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrWhiteSpace(parsedIntent.Target) &&
                   string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                   string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                   !parsedIntent.ForWork &&
                   !parsedIntent.ForSchool &&
                   !parsedIntent.ForCity &&
                   !parsedIntent.ForTour &&
                   !parsedIntent.WantsFuelSaving &&
                   !parsedIntent.WantsLargeStorage &&
                   !parsedIntent.WantsEasyControl &&
                   !parsedIntent.NeedsLowSeat &&
                   !parsedIntent.PrefersMaleStyle &&
                   !parsedIntent.PrefersFemaleStyle &&
                   !parsedIntent.HeightCm.HasValue &&
                   !parsedIntent.PriceMin.HasValue &&
                   !parsedIntent.PriceMax.HasValue &&
                   !parsedIntent.TargetPrice.HasValue &&
                   (parsedIntent.MentionedProducts == null || parsedIntent.MentionedProducts.Count == 0);
        }
        private static bool IsAmbiguousContextDependentFollowUp(
     ParsedIntent parsedIntent,
     string normalizedMessage,
     CustomerPreferenceProfile? existingProfile)
        {
            if (existingProfile == null)
                return false;

            var message = normalizedMessage?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(message))
                return false;

            bool hasConversationContext =
                !string.IsNullOrWhiteSpace(existingProfile.LastResolvedProductName) ||
                (existingProfile.LastLookupCandidateNames?.Count > 0) ||
                existingProfile.LastResolvedOrderId.HasValue ||
                !string.IsNullOrWhiteSpace(existingProfile.LastResolvedOrderPhone) ||
                !string.IsNullOrWhiteSpace(existingProfile.PreferredBrand) ||
                !string.IsNullOrWhiteSpace(existingProfile.PreferredCategory);

            if (!hasConversationContext)
                return false;

            bool isShortMessage = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8;

            bool hasReferenceWords =
                message.Contains("mẫu đó") ||
                message.Contains("xe đó") ||
                message.Contains("con đó") ||
                message.Contains("con kia") ||
                message.Contains("xe kia") ||
                message.Contains("cái đó") ||
                message.Contains("cái kia") ||
                message.Contains("đơn đó") ||
                message.Contains("đơn kia") ||
                message.Contains("cái đầu") ||
                message.Contains("hôm nãy") ||
                message.Contains("lúc nãy") ||
                message.Contains("loại kia") ||
                message.Contains("bản đó");

            bool isHeuristicFollowUp =
                parsedIntent.IsFollowUp ||
                parsedIntent.HasExpandRecommendationSignal ||
                parsedIntent.HasNarrowRefinementSignal ||
                !string.IsNullOrWhiteSpace(parsedIntent.FollowUpType);

            return isShortMessage && (hasReferenceWords || isHeuristicFollowUp);
        }
        private static bool HasNaturalLanguageConsultationSignal(string normalizedMessage)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage))
                return false;

            var message = normalizedMessage.Trim().ToLowerInvariant();

            return
                message.Contains("đi làm") ||
                message.Contains("di lam") ||
                message.Contains("đi học") ||
                message.Contains("di hoc") ||
                message.Contains("đi phố") ||
                message.Contains("di pho") ||
                message.Contains("đi tour") ||
                message.Contains("di tour") ||
                message.Contains("tiết kiệm xăng") ||
                message.Contains("tiet kiem xang") ||
                message.Contains("cốp rộng") ||
                message.Contains("cop rong") ||
                message.Contains("dễ chạy") ||
                message.Contains("de chay") ||
                message.Contains("dễ lái") ||
                message.Contains("de lai") ||
                message.Contains("chống chân") ||
                message.Contains("chong chan") ||
                message.Contains("cho nữ") ||
                message.Contains("cho nu") ||
                message.Contains("cho nam") ||
                message.Contains("phù hợp") ||
                message.Contains("phu hop") ||
                message.Contains("nên mua") ||
                message.Contains("nen mua") ||
                message.Contains("tư vấn") ||
                message.Contains("tu van") ||
                message.Contains("gợi ý") ||
                message.Contains("goi y");
        }
        private async Task<ParsedIntent> EnrichIntentWithLlmAsync(
    ParsedIntent parsedIntent,
    string normalizedMessage,
    CustomerPreferenceProfile? existingProfile)
        {
            var llmIntent = await _llmIntentUnderstandingService.UnderstandAsync(
                normalizedMessage,
                existingProfile);

            if (llmIntent == null || string.IsNullOrWhiteSpace(llmIntent.IntentType))
            {
                return parsedIntent;
            }

            bool acceptLlm =
                (string.Equals(llmIntent.IntentType, "out_of_scope", StringComparison.OrdinalIgnoreCase) && llmIntent.Confidence >= 0.65) ||
                (string.Equals(llmIntent.IntentType, "unknown", StringComparison.OrdinalIgnoreCase) && llmIntent.Confidence >= 0.60) ||
                (llmIntent.Confidence >= 0.80);

            if (!acceptLlm)
            {
                return parsedIntent;
            }

            parsedIntent.IntentType = llmIntent.IntentType;
            parsedIntent.IsFollowUp |= llmIntent.IsFollowUp;

            if (string.IsNullOrWhiteSpace(parsedIntent.FollowUpType) && !string.IsNullOrWhiteSpace(llmIntent.FollowUpType))
                parsedIntent.FollowUpType = llmIntent.FollowUpType;

            if (string.IsNullOrWhiteSpace(parsedIntent.Brand) && !string.IsNullOrWhiteSpace(llmIntent.Brand))
                parsedIntent.Brand = llmIntent.Brand;

            if (string.IsNullOrWhiteSpace(parsedIntent.Category) && !string.IsNullOrWhiteSpace(llmIntent.Category))
                parsedIntent.Category = llmIntent.Category;

            if (string.IsNullOrWhiteSpace(parsedIntent.Target) && !string.IsNullOrWhiteSpace(llmIntent.Target))
                parsedIntent.Target = llmIntent.Target;

            if (!parsedIntent.PriceMin.HasValue && llmIntent.PriceMin.HasValue)
                parsedIntent.PriceMin = llmIntent.PriceMin;

            if (!parsedIntent.PriceMax.HasValue && llmIntent.PriceMax.HasValue)
                parsedIntent.PriceMax = llmIntent.PriceMax;

            if (!parsedIntent.TargetPrice.HasValue && llmIntent.TargetPrice.HasValue)
                parsedIntent.TargetPrice = llmIntent.TargetPrice;

            if (parsedIntent.FilterType == PriceFilterType.None &&
                TryMapPriceFilterType(llmIntent.PriceFilterType, out var mappedFilterType))
            {
                parsedIntent.FilterType = mappedFilterType;
            }

            parsedIntent.ForWork |= llmIntent.ForWork;
            parsedIntent.ForSchool |= llmIntent.ForSchool;
            parsedIntent.ForCity |= llmIntent.ForCity;
            parsedIntent.ForTour |= llmIntent.ForTour;
            parsedIntent.WantsFuelSaving |= llmIntent.WantsFuelSaving;
            parsedIntent.WantsLargeStorage |= llmIntent.WantsLargeStorage;
            parsedIntent.WantsEasyControl |= llmIntent.WantsEasyControl;
            parsedIntent.NeedsLowSeat |= llmIntent.NeedsLowSeat;

            if (!parsedIntent.HeightCm.HasValue && llmIntent.HeightCm.HasValue)
                parsedIntent.HeightCm = llmIntent.HeightCm;

            if (string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature) && !string.IsNullOrWhiteSpace(llmIntent.ComparisonFeature))
                parsedIntent.ComparisonFeature = llmIntent.ComparisonFeature;

            if ((parsedIntent.MentionedProducts == null || parsedIntent.MentionedProducts.Count == 0) &&
                llmIntent.MentionedProducts != null &&
                llmIntent.MentionedProducts.Count > 0)
            {
                parsedIntent.MentionedProducts = llmIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if ((parsedIntent.ExcludedBrands == null || parsedIntent.ExcludedBrands.Count == 0) &&
                llmIntent.ExcludedBrands != null &&
                llmIntent.ExcludedBrands.Count > 0)
            {
                parsedIntent.ExcludedBrands = new HashSet<string>(
                    llmIntent.ExcludedBrands.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if ((parsedIntent.ExcludedCategories == null || parsedIntent.ExcludedCategories.Count == 0) &&
                llmIntent.ExcludedCategories != null &&
                llmIntent.ExcludedCategories.Count > 0)
            {
                parsedIntent.ExcludedCategories = new HashSet<string>(
                    llmIntent.ExcludedCategories.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if ((parsedIntent.RequestedStyles == null || parsedIntent.RequestedStyles.Count == 0) &&
                llmIntent.RequestedStyles != null &&
                llmIntent.RequestedStyles.Count > 0)
            {
                parsedIntent.RequestedStyles = new HashSet<string>(
                    llmIntent.RequestedStyles.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (!parsedIntent.IsDirectProductLookup && llmIntent.IsDirectLookup)
                parsedIntent.IsDirectProductLookup = true;

            if (!parsedIntent.IsProductSearch && string.Equals(llmIntent.IntentType, "product_search", StringComparison.OrdinalIgnoreCase))
                parsedIntent.IsProductSearch = true;

            if (!parsedIntent.IsOpenRecommendation && string.Equals(llmIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase))
                parsedIntent.IsOpenRecommendation = true;

            if (!parsedIntent.IsDirectCompare && string.Equals(llmIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase))
                parsedIntent.IsDirectCompare = true;

            if (llmIntent.IsFollowUp)
            {
                if (string.Equals(llmIntent.FollowUpType, "expand", StringComparison.OrdinalIgnoreCase))
                    parsedIntent.HasExpandRecommendationSignal = true;

                if (string.Equals(llmIntent.FollowUpType, "refine", StringComparison.OrdinalIgnoreCase))
                    parsedIntent.HasNarrowRefinementSignal = true;
            }

            if (string.Equals(llmIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) && llmIntent.IsFreshSearch)
                parsedIntent.HasFreshConsultationSignal = true;

            _logger.LogInformation(
                "Merged LLM intent into parsed intent. IntentType={IntentType}, Brand={Brand}, Category={Category}, Target={Target}, Confidence={Confidence}",
                parsedIntent.IntentType,
                parsedIntent.Brand,
                parsedIntent.Category,
                parsedIntent.Target,
                llmIntent.Confidence);

            return parsedIntent;
        }
        private void ApplyPriceIntent(
    ParsedIntent parsedIntent,
    string normalizedMessage)
        {
            var priceIntent = _priceIntentParser.Parse(normalizedMessage);

            parsedIntent.PriceMin = priceIntent.MinPrice ?? parsedIntent.PriceMin;
            parsedIntent.PriceMax = priceIntent.MaxPrice ?? parsedIntent.PriceMax;
            parsedIntent.TargetPrice = priceIntent.TargetPrice ?? parsedIntent.TargetPrice;

            if (priceIntent.FilterType != PriceFilterType.None)
                parsedIntent.FilterType = priceIntent.FilterType;
        }
        private static bool TryMapPriceFilterType(
    string? priceFilterType,
    out PriceFilterType filterType)
        {
            filterType = PriceFilterType.None;

            if (string.IsNullOrWhiteSpace(priceFilterType))
                return false;

            switch (priceFilterType.Trim().ToLowerInvariant())
            {
                case "maxonly":
                case "max_only":
                case "max":
                case "under":
                case "duoi":
                case "below":
                    filterType = PriceFilterType.MaxOnly;
                    return true;

                case "minonly":
                case "min_only":
                case "min":
                case "above":
                case "tren":
                case "over":
                    filterType = PriceFilterType.MinOnly;
                    return true;

                case "range":
                case "between":
                    filterType = PriceFilterType.Range;
                    return true;

                case "around":
                case "approx":
                case "gan dung":
                    filterType = PriceFilterType.Around;
                    return true;

                default:
                    return false;
            }
        }
        private string NormalizeMessageOrThrowClarification(
    string conversationId,
    string originalMessage)
        {
            var normalizationResult = _queryNormalizationService.Analyze(originalMessage);
            var normalizedMessage = normalizationResult.NormalizedText;

            if (normalizationResult.NeedsConfirmation)
            {
                _clarificationStateService.SetPending(conversationId, normalizedMessage);

                _logger.LogInformation(
                    "Clarification required. ConversationId={ConversationId}, OriginalMessage={OriginalMessage}, NormalizedMessage={NormalizedMessage}",
                    conversationId,
                    originalMessage,
                    normalizedMessage);

                throw new ClarificationRequiredException(
                    $"Bạn muốn hỏi: \"{normalizedMessage}\" đúng không?");
            }

            return normalizedMessage;
        }
        private async Task<(CustomerPreferenceProfile MergedProfile, FlowRoutingResult BaseRouting, FlowRoutingResult FinalRouting)>
 BuildRoutingAsync(
     string conversationId,
     string normalizedMessage,
     ParsedIntent effectiveIntent,
     RecommendationContextDecision contextDecision,
     bool isFreshRecommendationByCurrentMessage)
        {
            var isFreshRecommendation =
      contextDecision == RecommendationContextDecision.StartFreshRecommendation ||
      isFreshRecommendationByCurrentMessage;
            var mergedProfile = await _conversationPreferenceService.MergeAsync(
    conversationId,
    effectiveIntent,
    isFreshRecommendation
) ?? new CustomerPreferenceProfile();

            if (effectiveIntent.PriceMin.HasValue)
                mergedProfile.PriceMin = effectiveIntent.PriceMin;

            if (effectiveIntent.PriceMax.HasValue)
                mergedProfile.PriceMax = effectiveIntent.PriceMax;

            if (effectiveIntent.TargetPrice.HasValue)
                mergedProfile.TargetPrice = effectiveIntent.TargetPrice;

            if (isFreshRecommendationByCurrentMessage &&
     !effectiveIntent.PriceMin.HasValue &&
     !effectiveIntent.PriceMax.HasValue &&
     !effectiveIntent.TargetPrice.HasValue)
            {
                mergedProfile.PriceMin = null;
                mergedProfile.PriceMax = null;
                mergedProfile.TargetPrice = null;
                mergedProfile.FilterType = PriceFilterType.None;
            }

            var baseRouting = _chatFlowRouter.Route(
                normalizedMessage,
                effectiveIntent,
                mergedProfile);

            var finalRouting = _flowDecisionService.ResolveFinalRouting(
                normalizedMessage,
                effectiveIntent,
                mergedProfile,
                contextDecision,
                baseRouting);

            mergedProfile.ActiveFlow = finalRouting.FlowType;
            mergedProfile.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Profile flow updated. ConversationId={ConversationId}, ActiveFlow={ActiveFlow}",
                conversationId,
                mergedProfile.ActiveFlow);

            return (mergedProfile, baseRouting, finalRouting);
        }
        private void LogContextSummary(
    string conversationId,
    ParsedIntent parsedIntent,
    ParsedIntent effectiveIntent,
    TurnContextBuildResult turnContext,
    FlowRoutingResult finalRouting,
    CustomerPreferenceProfile mergedProfile)
        {
            _logger.LogInformation(
                "ConversationId={ConversationId} | ParsedIntent: IntentType={IntentType}, Brand={Brand}, Category={Category}, Target={Target}, PriceMin={PriceMin}, PriceMax={PriceMax}, TargetPrice={TargetPrice}, FilterType={FilterType} | EffectiveIntent: Brand={EffectiveBrand}, Category={EffectiveCategory}, Target={EffectiveTarget}, PriceMin={EffectivePriceMin}, PriceMax={EffectivePriceMax}, TargetPrice={EffectiveTargetPrice}, FilterType={EffectiveFilterType} | GoalContinuity={GoalContinuity} | IsFollowUp={IsFollowUp} | IsGoalSwitch={IsGoalSwitch} | Reason={Reason} | FinalFlow={FinalFlow}",
                conversationId,
                parsedIntent.IntentType,
                parsedIntent.Brand,
                parsedIntent.Category,
                parsedIntent.Target,
                parsedIntent.PriceMin,
                parsedIntent.PriceMax,
                parsedIntent.TargetPrice,
                parsedIntent.FilterType,
                effectiveIntent.Brand,
                effectiveIntent.Category,
                effectiveIntent.Target,
                effectiveIntent.PriceMin,
                effectiveIntent.PriceMax,
                effectiveIntent.TargetPrice,
                effectiveIntent.FilterType,
               turnContext.GoalContinuity,
turnContext.IsFollowUp,
turnContext.IsGoalSwitch,
turnContext.Reason,
                finalRouting.FlowType);

            _logger.LogInformation(
                "Profile snapshot. ConversationId={ConversationId}, Target={Target}, PreferredBrand={PreferredBrand}, PreferredCategory={PreferredCategory}, PriceMin={PriceMin}, PriceMax={PriceMax}, TargetPrice={TargetPrice}, ForWork={ForWork}, ForSchool={ForSchool}, WantsFuelSaving={WantsFuelSaving}, WantsLargeStorage={WantsLargeStorage}, NeedsLowSeat={NeedsLowSeat}, ActiveFlow={ActiveFlow}",
                conversationId,
                mergedProfile.Target,
                mergedProfile.PreferredBrand,
                mergedProfile.PreferredCategory,
                mergedProfile.PriceMin,
                mergedProfile.PriceMax,
                mergedProfile.TargetPrice,
                mergedProfile.ForWork,
                mergedProfile.ForSchool,
                mergedProfile.WantsFuelSaving,
                mergedProfile.WantsLargeStorage,
                mergedProfile.NeedsLowSeat,
                mergedProfile.ActiveFlow);
        }
        private void HydrateProfileFromState(
    CustomerPreferenceProfile profile,
    ConversationState? state)
        {
            if (profile == null || state == null)
                return;
            var constraints = state.Constraints ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            // Nếu profile trong RAM đã có context tốt rồi thì ưu tiên giữ nguyên.
            bool hasLiveRecommendationContext =
                profile.HasActiveRecommendationContext &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0;

            bool hasLiveLookupContext =
                !string.IsNullOrWhiteSpace(profile.LastLookupProductName) ||
                profile.LastLookupProductId.HasValue;

            bool hasLiveCompareContext =
                profile.HasActiveCompareContext &&
                profile.LastComparedProducts != null &&
                profile.LastComparedProducts.Count >= 2;

            if (hasLiveRecommendationContext || hasLiveLookupContext || hasLiveCompareContext)
                return;

            profile.ActiveFlow ??= state.CurrentGoalType;
            profile.LastIntentType ??= state.LastIntentType;
            profile.UpdatedAtUtc = state.UpdatedAtUtc;

            if (constraints.TryGetValue("brand", out var brand) &&
                !string.IsNullOrWhiteSpace(brand) &&
                string.IsNullOrWhiteSpace(profile.PreferredBrand))
            {
                profile.PreferredBrand = brand;
            }

            if (constraints.TryGetValue("category", out var category) &&
                !string.IsNullOrWhiteSpace(category) &&
                string.IsNullOrWhiteSpace(profile.PreferredCategory))
            {
                profile.PreferredCategory = category;
            }

            if (constraints.TryGetValue("target", out var target) &&
                !string.IsNullOrWhiteSpace(target) &&
                string.IsNullOrWhiteSpace(profile.Target))
            {
                profile.Target = target;
            }

            if (constraints.TryGetValue("priceMin", out var priceMinText) &&
                decimal.TryParse(priceMinText, out var priceMin) &&
                !profile.PriceMin.HasValue)
            {
                profile.PriceMin = priceMin;
            }

            if (constraints.TryGetValue("priceMax", out var priceMaxText) &&
                decimal.TryParse(priceMaxText, out var priceMax) &&
                !profile.PriceMax.HasValue)
            {
                profile.PriceMax = priceMax;
            }

            if (constraints.TryGetValue("targetPrice", out var targetPriceText) &&
                decimal.TryParse(targetPriceText, out var targetPrice) &&
                !profile.TargetPrice.HasValue)
            {
                profile.TargetPrice = targetPrice;
            }

            if (constraints.TryGetValue("filterType", out var filterTypeText) &&
                Enum.TryParse<PriceFilterType>(filterTypeText, true, out var filterType) &&
                profile.FilterType == PriceFilterType.None)
            {
                profile.FilterType = filterType;
            }

            if (constraints.TryGetValue("forWork", out var forWorkText) &&
                bool.TryParse(forWorkText, out var forWork) &&
                !profile.ForWork)
            {
                profile.ForWork = forWork;
            }

            if (constraints.TryGetValue("forSchool", out var forSchoolText) &&
                bool.TryParse(forSchoolText, out var forSchool) &&
                !profile.ForSchool)
            {
                profile.ForSchool = forSchool;
            }

            if (constraints.TryGetValue("forCity", out var forCityText) &&
                bool.TryParse(forCityText, out var forCity) &&
                !profile.ForCity)
            {
                profile.ForCity = forCity;
            }
                
            if (constraints.TryGetValue("forTour", out var forTourText) &&
                bool.TryParse(forTourText, out var forTour) &&
                !profile.ForTour)
            {
                profile.ForTour = forTour;
            }

            if (constraints.TryGetValue("wantsFuelSaving", out var fuelText) &&
                bool.TryParse(fuelText, out var fuelSaving) &&
                !profile.WantsFuelSaving)
            {
                profile.WantsFuelSaving = fuelSaving;
            }

            if (constraints.TryGetValue("wantsLargeStorage", out var storageText) &&
                bool.TryParse(storageText, out var largeStorage) &&
                !profile.WantsLargeStorage)
            {
                profile.WantsLargeStorage = largeStorage;
            }

            if (constraints.TryGetValue("wantsEasyControl", out var easyText) &&
                bool.TryParse(easyText, out var easyControl) &&
                !profile.WantsEasyControl)
            {
                profile.WantsEasyControl = easyControl;
            }

            if (constraints.TryGetValue("needsLowSeat", out var lowSeatText) &&
                bool.TryParse(lowSeatText, out var needsLowSeat) &&
                !profile.NeedsLowSeat)
            {
                profile.NeedsLowSeat = needsLowSeat;
            }
                    
            if (constraints.TryGetValue("prefersMaleStyle", out var maleText) &&
                bool.TryParse(maleText, out var prefersMale) &&
                !profile.PrefersMaleStyle)
            {
                profile.PrefersMaleStyle = prefersMale;
            }

            if (constraints.TryGetValue("prefersFemaleStyle", out var femaleText) &&
                bool.TryParse(femaleText, out var prefersFemale) &&
                !profile.PrefersFemaleStyle)
            {
                profile.PrefersFemaleStyle = prefersFemale;
            }

            if (state.MentionedProductNames != null && state.MentionedProductNames.Count > 0)
            {
                profile.LastMentionedProducts = state.MentionedProductNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (string.Equals(state.CurrentGoalType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state.CurrentGoalType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase))
            {
                if (state.MentionedProductNames != null && state.MentionedProductNames.Count > 0)
                {
                    profile.LastRecommendedProducts = state.MentionedProductNames
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (state.MentionedProductIds != null && state.MentionedProductIds.Count > 0)
                {
                    profile.LastRecommendedProductIds = state.MentionedProductIds
                        .Distinct()
                        .ToList();
                }

                profile.HasActiveRecommendationContext =
                    profile.LastRecommendedProducts != null &&
                    profile.LastRecommendedProducts.Count > 0;
            }

            if (string.Equals(state.CurrentGoalType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
            {
                profile.LastComparedProducts = state.MentionedProductNames?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList() ?? new List<string>();

                profile.HasActiveCompareContext = profile.LastComparedProducts.Count >= 2;
            }

            if (string.Equals(state.CurrentGoalType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
            {
                profile.LastLookupProductName = state.LastResolvedReference;

                if (state.MentionedProductIds != null && state.MentionedProductIds.Count > 0)
                {
                    profile.LastLookupProductId = state.MentionedProductIds.First();
                }
            }
        }

        private async Task PersistConversationStateAsync(
     ChatOrchestrationContext context,
     ChatResponse response)
        {
            if (context == null)
                return;

            var profile = await _conversationPreferenceService.GetAsync(context.ConversationId);
            var safeProfile = profile ?? new CustomerPreferenceProfile();

            var patch = BuildStatePatch(context, response, safeProfile);
            await _conversationStateService.ApplyPatchAsync(
                context.ConversationId,
                patch,
                context.Request?.Channel,
                context.Request?.UserId);
        }
        private ConversationStatePatch BuildStatePatch(
            ChatOrchestrationContext context,
            ChatResponse response,
            CustomerPreferenceProfile profile)
        {
            var constraints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["brand"] = profile.PreferredBrand,
                ["category"] = profile.PreferredCategory,
                ["target"] = profile.Target,
                ["priceMin"] = profile.PriceMin?.ToString(),
                ["priceMax"] = profile.PriceMax?.ToString(),
                ["targetPrice"] = profile.TargetPrice?.ToString(),
                ["filterType"] = profile.FilterType.ToString(),
                ["forWork"] = profile.ForWork.ToString(),
                ["forSchool"] = profile.ForSchool.ToString(),
                ["forCity"] = profile.ForCity.ToString(),
                ["forTour"] = profile.ForTour.ToString(),
                ["wantsFuelSaving"] = profile.WantsFuelSaving.ToString(),
                ["wantsLargeStorage"] = profile.WantsLargeStorage.ToString(),
                ["wantsEasyControl"] = profile.WantsEasyControl.ToString(),
                ["needsLowSeat"] = profile.NeedsLowSeat.ToString(),
                ["prefersMaleStyle"] = profile.PrefersMaleStyle.ToString(),
                ["prefersFemaleStyle"] = profile.PrefersFemaleStyle.ToString()
            };

            var mentionedProductIds = new List<int>();
            var mentionedProductNames = new List<string>();

            if (response?.Products != null && response.Products.Count > 0)
            {
                mentionedProductIds = response.Products
                    .Select(x => x.Id)
                    .Distinct()
                    .ToList();

                mentionedProductNames = response.Products
                    .Select(x => x.Ten)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                if (profile.LastRecommendedProductIds != null && profile.LastRecommendedProductIds.Count > 0)
                    mentionedProductIds = profile.LastRecommendedProductIds.Distinct().ToList();
                else if (profile.LastSearchProductIds != null && profile.LastSearchProductIds.Count > 0)
                    mentionedProductIds = profile.LastSearchProductIds.Distinct().ToList();

                if (profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count > 0)
                    mentionedProductNames = profile.LastRecommendedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                else if (profile.LastMentionedProducts != null && profile.LastMentionedProducts.Count > 0)
                    mentionedProductNames = profile.LastMentionedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                else if (profile.LastComparedProducts != null && profile.LastComparedProducts.Count > 0)
                    mentionedProductNames = profile.LastComparedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }

            var currentGoalType = NormalizeGoalType(context.FinalRouting?.FlowType, profile);
            var isAwaitingClarification = LooksLikeClarificationResponse(response?.Reply);
            var currentGoalStatus = isAwaitingClarification ? "pending_clarification" : "active";

            return new ConversationStatePatch
            {
                CurrentDomain = ResolveCurrentDomain(context.EffectiveIntent, currentGoalType),
                CurrentGoalType = currentGoalType,
                CurrentGoalStatus = currentGoalStatus,
                LastIntentType = context.EffectiveIntent?.IntentType,
                LastQuestionType = ResolveLastQuestionType(context.EffectiveIntent, currentGoalType),
                LastBotQuestionType = ResolveLastBotQuestionType(response),
                LastResolvedReference = ResolveLastResolvedReference(profile, mentionedProductNames),
                Constraints = constraints,
                CandidateProductIds = mentionedProductIds,
                MentionedProductIds = mentionedProductIds,
                MentionedProductNames = mentionedProductNames,
                TurnSummary = BuildTurnSummary(context, response, currentGoalType, mentionedProductNames),
                CarryForwardConfidence = ResolveCarryForwardConfidence(context, profile),
                IsAwaitingClarification = isAwaitingClarification
            };
        }

        private static string ResolveCurrentDomain(ParsedIntent? intent, string? currentGoalType)
        {
            if (string.Equals(currentGoalType, ChatFlowType.OrderLookup, StringComparison.OrdinalIgnoreCase))
                return "don_hang";

            if (intent?.IsOutOfScope == true)
                return "out_of_scope";

            return "xe_may";
        }

        private static string ResolveLastQuestionType(ParsedIntent? intent, string? currentGoalType)
        {
            if (intent == null)
                return currentGoalType ?? "unknown";

            if (string.Equals(currentGoalType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(intent.LookupField) ? "product_lookup" : $"lookup_{intent.LookupField}";

            if (string.Equals(currentGoalType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(intent.ComparisonFeature) ? "compare" : $"compare_{intent.ComparisonFeature}";

            if (string.Equals(currentGoalType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase))
                return "refinement";

            if (string.Equals(currentGoalType, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase))
                return "product_search";

            if (string.Equals(currentGoalType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase))
                return "recommendation";

            return intent.IntentType ?? "unknown";
        }

        private static string? ResolveLastBotQuestionType(ChatResponse? response)
        {
            if (response == null || string.IsNullOrWhiteSpace(response.Reply))
                return null;

            var reply = response.Reply.Trim().ToLowerInvariant();

            if (reply.Contains("nói rõ hơn"))
                return "clarify_more";

            if (reply.Contains("hãng"))
                return "clarify_brand";

            if (reply.Contains("mức giá") || reply.Contains("ngân sách"))
                return "clarify_budget";

            if (reply.Contains("nhu cầu"))
                return "clarify_use_case";

            return null;
        }

        private static string? ResolveLastResolvedReference(
            CustomerPreferenceProfile profile,
            List<string> mentionedProductNames)
        {
            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                return profile.LastLookupProductName;

            if (mentionedProductNames.Count > 0)
                return mentionedProductNames.First();

            if (profile.LastMentionedProducts != null && profile.LastMentionedProducts.Count > 0)
                return profile.LastMentionedProducts.First();

            return null;
        }

        private static string BuildTurnSummary(
            ChatOrchestrationContext context,
            ChatResponse? response,
            string? currentGoalType,
            List<string> mentionedProductNames)
        {
            var productText = mentionedProductNames.Count > 0
                ? string.Join(", ", mentionedProductNames.Take(3))
                : "không có sản phẩm cụ thể";

            return $"Flow={currentGoalType ?? "unknown"}; Intent={context.EffectiveIntent?.IntentType ?? "unknown"}; Products={productText}; ReplyOk={(response?.Success == true)}";
        }

        private static decimal ResolveCarryForwardConfidence(
            ChatOrchestrationContext context,
            CustomerPreferenceProfile profile)
        {
            if (string.Equals(context.FinalRouting?.FlowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(context.FinalRouting?.FlowType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(context.FinalRouting?.FlowType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
            {
                return 0.95m;
            }

            if (profile.HasActiveRecommendationContext &&
                profile.LastRecommendedProducts != null &&
                profile.LastRecommendedProducts.Count > 0)
            {
                return 0.85m;
            }

            return 0.60m;
        }

        private static bool LooksLikeClarificationResponse(string? reply)
        {
            if (string.IsNullOrWhiteSpace(reply))
                return false;

            var text = reply.Trim().ToLowerInvariant();

            return text.Contains("bạn thử nói rõ hơn")
                   || text.Contains("bạn cứ nói thêm")
                   || text.Contains("cho mình biết thêm")
                   || text.Contains("hãy nói rõ")
                   || text.Contains("mình cần thêm");
        }

        private static string? NormalizeGoalType(string? flowType, CustomerPreferenceProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(flowType) &&
                !string.Equals(flowType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                return flowType;
            }

            if (!string.IsNullOrWhiteSpace(profile.ActiveFlow))
                return profile.ActiveFlow;

            if (profile.HasActiveCompareContext)
                return ChatFlowType.Compare;

            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                return ChatFlowType.ProductLookup;

            if (profile.HasActiveRecommendationContext)
                return ChatFlowType.Recommendation;

            return ChatFlowType.Unknown;
        }
        private static RecommendationContextDecision MapTurnContextToRecommendationDecision(
     TurnContextBuildResult? turnContext,
     ParsedIntent? effectiveIntent)
        {
            if (turnContext == null || effectiveIntent == null)
                return RecommendationContextDecision.None;

            if (effectiveIntent.IsOutOfScope ||
                effectiveIntent.IsNoise ||
                effectiveIntent.IsAck ||
                effectiveIntent.IsGreeting)
            {
                return RecommendationContextDecision.None;
            }

            bool hasDomainSignal =
                effectiveIntent.IsDirectProductLookup ||
                effectiveIntent.IsProductSearch ||
                effectiveIntent.IsOpenRecommendation ||
                effectiveIntent.IsDirectCompare ||
                effectiveIntent.IsOrderLookup ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Brand) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Category) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Target) ||
                effectiveIntent.PriceMin.HasValue ||
                effectiveIntent.PriceMax.HasValue ||
                effectiveIntent.TargetPrice.HasValue ||
                (effectiveIntent.MentionedProducts?.Count > 0);

            if (string.Equals(turnContext.GoalContinuity, "new_goal", StringComparison.OrdinalIgnoreCase)
                && hasDomainSignal)
            {
                return RecommendationContextDecision.StartFreshRecommendation;
            }

            return RecommendationContextDecision.None;
        }
        private static FlowRoutingResult ApplyRoutingBiasFromTurnContext(
    FlowRoutingResult finalRouting,
    TurnContextBuildResult turnContext,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile mergedProfile)
        {
            if (finalRouting == null)
                return new FlowRoutingResult();

            if (turnContext == null)
                return finalRouting;

            // Nếu là refine thì ưu tiên refinement/recommendation thay vì rơi sang unknown hoặc search mơ hồ
            if (string.Equals(turnContext.GoalContinuity, "refine", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(finalRouting.FlowType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(finalRouting.FlowType, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase))
                {
                    finalRouting.FlowType = ChatFlowType.Refinement;
                    finalRouting.Reason = "turn_context_refine_bias";
                    finalRouting.ShouldUseAiFallback = false;
                    return finalRouting;
                }
            }

            if (string.Equals(turnContext.GoalContinuity, "continue", StringComparison.OrdinalIgnoreCase))
            {
                bool hasCurrentTurnDomainSignal =
                    effectiveIntent.IsDirectProductLookup ||
                    effectiveIntent.IsProductSearch ||
                    effectiveIntent.IsOpenRecommendation ||
                    effectiveIntent.IsDirectCompare ||
                    effectiveIntent.IsOrderLookup ||
                    !string.IsNullOrWhiteSpace(effectiveIntent.Brand) ||
                    !string.IsNullOrWhiteSpace(effectiveIntent.Category) ||
                    !string.IsNullOrWhiteSpace(effectiveIntent.Target) ||
                    effectiveIntent.PriceMin.HasValue ||
                    effectiveIntent.PriceMax.HasValue ||
                    effectiveIntent.TargetPrice.HasValue ||
                    (effectiveIntent.MentionedProducts?.Count > 0);

                if (mergedProfile.HasActiveRecommendationContext &&
                    string.Equals(finalRouting.FlowType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase) &&
                    hasCurrentTurnDomainSignal &&
                    !effectiveIntent.IsOutOfScope &&
                    !effectiveIntent.IsNoise &&
                    !effectiveIntent.IsAck)
                {
                    finalRouting.FlowType = ChatFlowType.Recommendation;
                    finalRouting.Reason = "turn_context_continue_bias";
                    finalRouting.ShouldUseAiFallback = false;
                    return finalRouting;
                }
            }

            // Nếu là pivot thì giữ flow mới nếu đã rõ; chỉ cứu khi nó rơi về unknown
            if (string.Equals(turnContext.GoalContinuity, "pivot", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(finalRouting.FlowType, ChatFlowType.Unknown, StringComparison.OrdinalIgnoreCase))
                {
                    if (effectiveIntent.IsDirectCompare)
                    {
                        finalRouting.FlowType = ChatFlowType.Compare;
                        finalRouting.Reason = "turn_context_pivot_compare_bias";
                        finalRouting.ShouldUseAiFallback = false;
                        return finalRouting;
                    }

                    if (effectiveIntent.IsDirectProductLookup)
                    {
                        finalRouting.FlowType = ChatFlowType.ProductLookup;
                        finalRouting.Reason = "turn_context_pivot_lookup_bias";
                        finalRouting.ShouldUseAiFallback = false;
                        return finalRouting;
                    }

                    if (effectiveIntent.IsProductSearch)
                    {
                        finalRouting.FlowType = ChatFlowType.ProductSearch;
                        finalRouting.Reason = "turn_context_pivot_search_bias";
                        finalRouting.ShouldUseAiFallback = false;
                        return finalRouting;
                    }
                }
            }

            return finalRouting;
        }
        private async Task<FlowRoutingResult> ApplyContextualClassifierAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile,
    FlowRoutingResult finalRouting)
        {
            if ((effectiveIntent.IsDirectProductLookup || !string.IsNullOrWhiteSpace(effectiveIntent.LookupField)) &&
    effectiveIntent.MentionedProducts != null &&
    effectiveIntent.MentionedProducts.Count > 0)
            {
                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.ProductLookup,
                    Reason = "explicit_product_lookup_guard",
                    ShouldUseAiFallback = false
                };
            }
            _logger.LogWarning(
    "CONTEXTUAL CLASSIFIER CHECK => Message={Message}, ShouldUse={ShouldUse}, ActiveRec={ActiveRec}, ActiveCompare={ActiveCompare}",
    normalizedMessage,
    ShouldUseContextualClassifier(normalizedMessage, effectiveIntent, profile),
    profile.HasActiveRecommendationContext,
    profile.HasActiveCompareContext);
            if (!ShouldUseContextualClassifier(normalizedMessage, effectiveIntent, profile))
                return finalRouting;

            var decision = await _contextualIntentClassifier.ClassifyAsync(
     normalizedMessage,
     effectiveIntent,
     profile);

            if (decision == null || decision.Confidence < 0.70)
            {
                var fallbackRouting = TryBuildDeterministicContextualFallback(
                    normalizedMessage,
                    effectiveIntent,
                    profile,
                    finalRouting);

                return fallbackRouting ?? finalRouting;
            }
            _logger.LogInformation(
                "Contextual classifier decision. ConversationId={ConversationId}, Flow={Flow}, Action={Action}, Confidence={Confidence}, Reason={Reason}",
                conversationId,
                decision.Flow,
                decision.Action,
                decision.Confidence,
                decision.Reason);

            ApplyContextualDecisionToIntent(effectiveIntent, decision);

            if (string.Equals(decision.Flow, "refinement", StringComparison.OrdinalIgnoreCase))
            {
                ClearCompareContextIfNeeded(profile);
                if (string.Equals(decision.Action, "switch_brand", StringComparison.OrdinalIgnoreCase))
                {
                    var text = NormalizeText(normalizedMessage);

                    bool messageMentionsFeature =
                        text.Contains("cop rong") ||
                        text.Contains("tiet kiem xang") ||
                        text.Contains("de chong chan") ||
                        text.Contains("yen thap") ||
                        text.Contains("de di");

                    bool isStrongDirectionChange =
                        text.Contains("honda") ||
                        text.Contains("yamaha") ||
                        text.Contains("suzuki") ||
                        text.Contains("sym") ||
                        text.Contains("piaggio");
                    bool shouldKeepFeature =
                        messageMentionsFeature
                        || (!isStrongDirectionChange && decision.ShouldKeepPreviousFeature == true);

                    if (!shouldKeepFeature)
                    {
                        profile.WantsLargeStorage = false;
                        profile.WantsFuelSaving = false;
                        profile.NeedsLowSeat = false;
                        profile.WantsEasyControl = false;

                        effectiveIntent.WantsLargeStorage = false;
                        effectiveIntent.WantsFuelSaving = false;
                        effectiveIntent.NeedsLowSeat = false;
                        effectiveIntent.WantsEasyControl = false;
                        effectiveIntent.ComparisonFeature = null;
                    }
                }
                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Refinement,
                    Reason = $"ContextualLLM:{decision.Action}",
                    ShouldUseAiFallback = false
                };
            }

            if (string.Equals(decision.Flow, "compare", StringComparison.OrdinalIgnoreCase))
            {
                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Compare,
                    Reason = $"ContextualLLM:{decision.Action}",
                    ShouldUseAiFallback = false
                };
            }

            if (string.Equals(decision.Flow, "recommendation", StringComparison.OrdinalIgnoreCase))
            {
                ClearCompareContextIfNeeded(profile);

                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Recommendation,
                    Reason = $"ContextualLLM:{decision.Action}",
                    ShouldUseAiFallback = false
                };
            }

            if (string.Equals(decision.Flow, "out_of_scope", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IsOutOfScope = true;

                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.OutOfScope,
                    Reason = "ContextualLLM:out_of_scope",
                    ShouldUseAiFallback = false
                };
            }

            return finalRouting;
        }
        private static void ApplyContextualDecisionToIntent(
    ParsedIntent intent,
    ContextualIntentDecision decision)
        {
            if (intent == null || decision == null)
                return;

            intent.IsFollowUp = true;

            switch (decision.Action)
            {
                case "refine_feature":
                    intent.ComparisonFeature ??= "feature";
                    break;

                case "alternative":
                    intent.ComparisonFeature = "alternative";
                    intent.HasExpandRecommendationSignal = true;
                    break;

                case "decide_best":
                    intent.ComparisonFeature = "decide_best";
                    intent.HasNarrowRefinementSignal = true;
                    break;

                case "switch_brand":
                    intent.HasNarrowRefinementSignal = true;

                    if (decision.ShouldKeepPreviousFeature == false)
                    {
                        intent.WantsLargeStorage = false;
                        intent.WantsFuelSaving = false;
                        intent.NeedsLowSeat = false;
                        intent.WantsEasyControl = false;
                        intent.ComparisonFeature = null;
                    }

                    break;
                case "switch_category":
                    intent.HasNarrowRefinementSignal = true;
                    break;

                case "compare_products":
                    intent.IsDirectCompare = true;
                    break;

                case "out_of_scope":
                    intent.IsOutOfScope = true;
                    break;
            }
        }

        private static void ClearCompareContextIfNeeded(CustomerPreferenceProfile profile)
        {
            if (profile == null || !profile.HasActiveCompareContext)
                return;

            profile.HasActiveCompareContext = false;
            profile.LastComparedProducts?.Clear();
            profile.LastComparisonFeature = null;
            profile.ActiveFlow = ChatFlowType.Recommendation;
        }
        private static FlowRoutingResult ForceExitCompareContextForRecommendationFollowUp(
    string normalizedMessage,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile,
    FlowRoutingResult finalRouting)
        {
            if (profile == null || finalRouting == null)
                return finalRouting;

            if (!profile.HasActiveRecommendationContext || !profile.HasActiveCompareContext)
                return finalRouting;

            var text = NormalizeText(normalizedMessage);

            bool hasExplicitCompare =
                text.Contains("so sanh") ||
                text.Contains("so voi") ||
                text.Contains("khac nhau") ||
                (
                    effectiveIntent.MentionedProducts != null &&
                    effectiveIntent.MentionedProducts.Count >= 2
                );

            if (hasExplicitCompare)
                return finalRouting;

            bool looksLikeRecommendationFollowUp =
                text.Contains("cop rong") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("de chong chan") ||
                text.Contains("mau khac") ||
                text.Contains("xe khac") ||
                text.Contains("khac di") ||
                text.Contains("xe nao on") ||
                text.Contains("mau nao on") ||
                text.Contains("chon xe nao") ||
                text.Contains("honda di") ||
                text.Contains("yamaha di") ||
                text.Contains("suzuki di") ||
                text.Contains("sym di") ||
                text.Contains("piaggio di") ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Brand);

            if (!looksLikeRecommendationFollowUp)
                return finalRouting;

            ClearCompareContextIfNeeded(profile);

            effectiveIntent.IsDirectCompare = false;
            effectiveIntent.IsFollowUp = true;

            if (text.Contains("mau khac") || text.Contains("xe khac") || text.Contains("khac di"))
                effectiveIntent.ComparisonFeature = "alternative";

            if (text.Contains("xe nao on") || text.Contains("mau nao on") || text.Contains("chon xe nao"))
                effectiveIntent.ComparisonFeature = "decide_best";

            if (text.Contains("cop rong"))
            {
                effectiveIntent.WantsLargeStorage = true;
                effectiveIntent.ComparisonFeature = "storage";
            }

            return new FlowRoutingResult
            {
                FlowType = ChatFlowType.Refinement,
                Reason = "force_exit_compare_context_for_recommendation_followup",
                ShouldUseAiFallback = false
            };
        }
        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim().ToLowerInvariant();

            text = text
                .Replace('à', 'a').Replace('á', 'a').Replace('ạ', 'a').Replace('ả', 'a').Replace('ã', 'a')
                .Replace('â', 'a').Replace('ầ', 'a').Replace('ấ', 'a').Replace('ậ', 'a').Replace('ẩ', 'a').Replace('ẫ', 'a')
                .Replace('ă', 'a').Replace('ằ', 'a').Replace('ắ', 'a').Replace('ặ', 'a').Replace('ẳ', 'a').Replace('ẵ', 'a')
                .Replace('è', 'e').Replace('é', 'e').Replace('ẹ', 'e').Replace('ẻ', 'e').Replace('ẽ', 'e')
                .Replace('ê', 'e').Replace('ề', 'e').Replace('ế', 'e').Replace('ệ', 'e').Replace('ể', 'e').Replace('ễ', 'e')
                .Replace('ì', 'i').Replace('í', 'i').Replace('ị', 'i').Replace('ỉ', 'i').Replace('ĩ', 'i')
                .Replace('ò', 'o').Replace('ó', 'o').Replace('ọ', 'o').Replace('ỏ', 'o').Replace('õ', 'o')
                .Replace('ô', 'o').Replace('ồ', 'o').Replace('ố', 'o').Replace('ộ', 'o').Replace('ổ', 'o').Replace('ỗ', 'o')
                .Replace('ơ', 'o').Replace('ờ', 'o').Replace('ớ', 'o').Replace('ợ', 'o').Replace('ở', 'o').Replace('ỡ', 'o')
                .Replace('ù', 'u').Replace('ú', 'u').Replace('ụ', 'u').Replace('ủ', 'u').Replace('ũ', 'u')
                .Replace('ư', 'u').Replace('ừ', 'u').Replace('ứ', 'u').Replace('ự', 'u').Replace('ử', 'u').Replace('ữ', 'u')
                .Replace('ỳ', 'y').Replace('ý', 'y').Replace('ỵ', 'y').Replace('ỷ', 'y').Replace('ỹ', 'y')
                .Replace('đ', 'd');

            return text;
        }
        private static FlowRoutingResult? TryBuildDeterministicContextualFallback(
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    FlowRoutingResult currentRouting)
        {
            var text = NormalizeText(normalizedMessage);

            if (profile == null || !profile.HasActiveRecommendationContext)
                return null;

            bool hasExplicitCompare =
                text.Contains("so sanh") ||
                text.Contains("so voi") ||
                text.Contains("khac nhau") ||
                intent.IsDirectCompare ||
                string.Equals(intent.IntentType, "compare", StringComparison.OrdinalIgnoreCase);

            if (hasExplicitCompare)
                return null;

            if (text.Contains("cop rong"))
            {
                intent.IsFollowUp = true;
                intent.WantsLargeStorage = true;
                intent.ComparisonFeature = "storage";

                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Refinement,
                    Reason = "fallback_contextual_refine_storage",
                    ShouldUseAiFallback = false
                };
            }

            if (text.Contains("mau khac") || text.Contains("xe khac") || text.Contains("khac di"))
            {
                intent.IsFollowUp = true;
                intent.ComparisonFeature = "alternative";
                intent.HasExpandRecommendationSignal = true;

                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Refinement,
                    Reason = "fallback_contextual_alternative",
                    ShouldUseAiFallback = false
                };
            }

            if (text.Contains("xe nao on") || text.Contains("mau nao on") || text.Contains("chon xe nao"))
            {
                intent.IsFollowUp = true;
                intent.ComparisonFeature = "decide_best";
                intent.HasNarrowRefinementSignal = true;

                return new FlowRoutingResult
                {
                    FlowType = ChatFlowType.Refinement,
                    Reason = "fallback_contextual_decide_best",
                    ShouldUseAiFallback = false
                };
            }

            return null;
        }
        private static bool IsFreshRecommendationRequest(string message, ParsedIntent intent)
        {
            if (intent == null)
                return false;

            var text = NormalizeText(message);

            bool hasFreshPhrase =
                text.Contains("tu van") ||
                text.Contains("goi y") ||
                text.Contains("nen mua") ||
                text.Contains("chon xe") ||
                text.Contains("tim xe");

            bool hasNewNeed =
    !string.IsNullOrWhiteSpace(intent.Target) ||
    intent.TargetPrice.HasValue ||
    intent.PriceMin.HasValue ||
    intent.PriceMax.HasValue ||
    !string.IsNullOrWhiteSpace(intent.Category) ||
    !string.IsNullOrWhiteSpace(intent.Brand) ||
    intent.PrefersMaleStyle ||
    intent.PrefersFemaleStyle ||
    intent.ForWork ||
    intent.ForSchool ||
    intent.ForCity ||
    intent.ForTour ||
    intent.WantsFuelSaving ||
    intent.WantsLargeStorage ||
    intent.WantsEasyControl ||
    intent.NeedsLowSeat ||
    !string.IsNullOrWhiteSpace(intent.ComparisonFeature) ||
    (intent.RequestedStyles != null && intent.RequestedStyles.Count > 0);
            bool textHasNewNeed =
    text.Contains("xe so") ||
    text.Contains("xe ga") ||
    text.Contains("con tay") ||
    text.Contains("ben") ||
    text.Contains("bền") ||
    text.Contains("tiet kiem xang") ||
    text.Contains("cop rong") ||
    text.Contains("di lam") ||
    text.Contains("di hoc") ||
    text.Contains("de chong chan") ||
    text.Contains("de di");

            return hasFreshPhrase && (hasNewNeed || textHasNewNeed);
        }
        private static bool MessageHasExplicitPrice(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("trieu") ||
                   text.Contains("k") ||
                   text.Contains("vnd") ||
                   text.Contains("vnđ") ||
                   text.Any(char.IsDigit);
        }
        private static bool MessageHasExplicitCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("xe so") ||
                   text.Contains("xe ga") ||
                   text.Contains("con tay") ||
                   text.Contains("tay ga");
        }
        private static bool MessageAsksForCheaperOption(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("re hon") ||
                   text.Contains("mem hon") ||
                   text.Contains("gia thap hon") ||
                   text.Contains("thap hon") ||
                   text.Contains("it tien hon") ||
                   text.Contains("tiet kiem hon") ||
                   text.Contains("co mau nao re hon") ||
                   text.Contains("mau nao re hon") ||
                   text.Contains("xe nao re hon");
        }
        private static bool MessageHasExplicitBrand(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("honda") ||
                   text.Contains("yamaha") ||
                   text.Contains("suzuki") ||
                   text.Contains("sym") ||
                   text.Contains("piaggio");
        }
        private static bool ShouldRecoverPriceFollowUp(
    string message,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            if (intent == null || profile == null)
                return false;

            if (!profile.HasActiveRecommendationContext)
                return false;

            var text = NormalizeText(message);

            bool hasPricePhrase =
                text.Contains("trieu") ||
                text.Contains("tam") ||
                text.Contains("khoang") ||
                text.Contains("duoi") ||
                text.Contains("tren");

            return hasPricePhrase && (intent.IsOutOfScope || intent.IsNoise || intent.IntentType == "out_of_scope");
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
        private static bool MessageMentionsWork(string message)
        {
            var text = NormalizeText(message);
            return text.Contains("di lam") || text.Contains("cong so");
        }

        private static bool MessageMentionsSchool(string message)
        {
            var text = NormalizeText(message);
            return text.Contains("di hoc") || text.Contains("sinh vien") || text.Contains("hoc sinh");
        }

        private static bool MessageMentionsFuelSaving(string message)
        {
            var text = NormalizeText(message);
            return text.Contains("tiet kiem xang") || text.Contains("it hao xang") || text.Contains("hao xang");
        }

        private static bool MessageMentionsLargeStorage(string message)
        {
            var text = NormalizeText(message);
            return text.Contains("cop rong") || text.Contains("de do") || text.Contains("chua do");
        }

        private static bool MessageMentionsEasyControl(string message)
        {
            var text = NormalizeText(message);
            return text.Contains("de di") || text.Contains("de lai") || text.Contains("de chay") ||
                   text.Contains("chong chan") || text.Contains("yen thap");
        }
        private static bool LooksLikeHumanSupportOrAfterSalesRequest(string message)
        {
            var text = NormalizeText(message);

            string[] keywords =
            {
        "bao hanh",
        "loi xe",
        "xe bi loi",
        "hong xe",
        "doi tra",
        "hoan tien",
        "khieu nai",
        "giao hang",
        "van chuyen",
        "thanh toan",
        "chuyen khoan",
        "don hang",
        "ma don",
        "gap nhan vien",
        "nhan vien tu van",
        "admin",
        "ho tro truc tiep"
    };

            return keywords.Any(k => text.Contains(k));
        }

        private static string BuildHumanSupportReply(string message)
        {
            var text = NormalizeText(message);

            if (text.Contains("bao hanh") || text.Contains("loi xe") || text.Contains("xe bi loi") || text.Contains("hong xe"))
            {
                return "Rất tiếc vì xe của bạn đang gặp vấn đề. Với trường hợp bảo hành hoặc lỗi xe, nhân viên cần kiểm tra tình trạng xe, thời gian mua và chính sách áp dụng. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ trực tiếp nhé.";
            }

            if (text.Contains("don hang") || text.Contains("ma don") || text.Contains("giao hang") || text.Contains("van chuyen"))
            {
                return "Để kiểm tra thông tin đơn hàng hoặc giao hàng, bạn cần đăng nhập để hệ thống bảo vệ thông tin cá nhân. Sau đó bạn có thể bấm **Gặp nhân viên** để được hỗ trợ chi tiết nhé.";
            }

            if (text.Contains("thanh toan") || text.Contains("chuyen khoan") || text.Contains("hoan tien"))
            {
                return "Với vấn đề thanh toán hoặc hoàn tiền, nhân viên cần kiểm tra giao dịch cụ thể. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ an toàn và chính xác hơn nhé.";
            }

            return "Vấn đề này có thể cần nhân viên kiểm tra trực tiếp. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ chi tiết hơn nhé.";
        }
    }
}