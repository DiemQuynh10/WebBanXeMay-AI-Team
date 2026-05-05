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
using static Chatbot.API.Models.Intent.ParsedIntent;
using Chatbot.API.Models.Rag;
using System.Text.RegularExpressions;
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
        private readonly IConversationStateService _conversationStateService;
        private readonly ITurnContextBuilder _turnContextBuilder;
        private readonly IRecommendationFollowUpService _recommendationFollowUpService;
        private readonly IRagService _ragService;
        private readonly IIntentRecoveryService _intentRecoveryService;
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
    IRecommendationFollowUpService recommendationFollowUpService,
    ITurnContextBuilder turnContextBuilder,
    IRagService ragService,
    IIntentRecoveryService intentRecoveryService)
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
            _recommendationFollowUpService = recommendationFollowUpService;
            _turnContextBuilder = turnContextBuilder;
            _ragService = ragService;
            _intentRecoveryService = intentRecoveryService;

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
                if (IsResetCommand(request.Message))
                {
                    _clarificationStateService.Clear(conversationId);
                    await _conversationPreferenceService.ClearAsync(conversationId);
                    await _conversationStateService.ClearAsync(conversationId);

                    return new ChatResponse
                    {
                        Success = true,
                        UsedAI = false,
                        ConversationId = conversationId,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                        Reply = "Mình đã reset cuộc hội thoại. Bạn có thể bắt đầu lại nhé!"
                    };
                }
                var pendingResult = await TryHandlePendingClarificationAsync(
     request,
     conversationId,
     stopwatch.ElapsedMilliseconds);

                if (pendingResult.Handled)
                {
                    return pendingResult.Response!;
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
            if (LooksLikeBudgetExpansionAmbiguousAfterCompare(normalizedMessage, existingProfile))
            {
                var clarificationIntent = new ParsedIntent
                {
                    IntentType = "clarification",
                    IsFollowUp = true
                };

                return new ChatOrchestrationContext
                {
                    Request = request,
                    ConversationId = conversationId,
                    OriginalMessage = originalMessage,
                    NormalizedMessage = normalizedMessage,
                    ExistingProfile = existingProfile,
                    State = state,
                    ParsedIntent = clarificationIntent,
                    EffectiveIntent = clarificationIntent,
                    BaseRouting = new FlowRoutingResult
                    {
                        FlowType = ChatFlowType.Unknown,
                        Reason = "Ambiguous budget expansion while compare context is active"
                    },
                    FinalRouting = new FlowRoutingResult
                    {
                        FlowType = ChatFlowType.Unknown,
                        Reason = "Ambiguous budget expansion while compare context is active",
                        ShouldUseAiFallback = false
                    }
                };
            }
            var parsedIntent = await ParseIntentAsync(normalizedMessage, existingProfile);
            if (LooksLikeGlobalProductStatisticQuery(normalizedMessage) &&
     !LooksLikeCheapestQuestionForCurrentList(normalizedMessage))
            {
                parsedIntent.IntentType = "product_search";
                parsedIntent.RouteFlow = ChatFlowType.ProductSearch;
                parsedIntent.IsProductSearch = true;
                parsedIntent.IsFollowUp = false;
                parsedIntent.HasFreshConsultationSignal = false;
                parsedIntent.HasDeterministicProductIntent = true;
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
            }
            _intentRecoveryService.Recover(
                normalizedMessage,
                parsedIntent,
                existingProfile);
            var isStrongStandaloneIntent = IsStrongStandaloneIntent(parsedIntent);
            bool isFreshBrandOnlyRequestByText =
    IsFreshBrandOnlyRequestByText(normalizedMessage, parsedIntent);

            if (isFreshBrandOnlyRequestByText)
            {
                ResetOldRecommendationConstraintsForFreshBrandOnly(parsedIntent);
            }
            if (IsExplicitProductSearchRequest(normalizedMessage, parsedIntent))
            {
                NormalizeExplicitProductSearchIntent(normalizedMessage, parsedIntent, existingProfile);
            }
            var isFreshRecommendationByCurrentMessage =
     !isStrongStandaloneIntent &&
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
            if (!isStrongStandaloneIntent &&
     !LooksLikeCheapestQuestionForCurrentList(normalizedMessage) &&
     MessageAsksForCheaperOption(normalizedMessage) &&
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
            if (LooksLikeGlobalProductStatisticQuery(normalizedMessage) &&
    !LooksLikeCheapestQuestionForCurrentList(normalizedMessage))
            {
                var asksGlobal = MessageAsksGlobalScope(normalizedMessage);
                var explicitBrand = DetectBrandFromText(normalizedMessage);
                var explicitCategory = DetectCategoryFromText(normalizedMessage);

                effectiveIntent = parsedIntent.Clone();

                effectiveIntent.IntentType = "product_search";
                effectiveIntent.RouteFlow = ChatFlowType.ProductSearch;
                effectiveIntent.IsProductSearch = true;
                effectiveIntent.IsFollowUp = false;
                effectiveIntent.HasFreshConsultationSignal = false;
                effectiveIntent.HasDeterministicProductIntent = true;
                effectiveIntent.IsOutOfScope = false;
                effectiveIntent.IsNoise = false;

                effectiveIntent.PriceMin = null;
                effectiveIntent.PriceMax = null;
                effectiveIntent.TargetPrice = null;
                effectiveIntent.FilterType = PriceFilterType.None;

                if (asksGlobal)
                {
                    effectiveIntent.Brand = null;
                    effectiveIntent.Category = null;
                }
                else
                {
                    effectiveIntent.Brand = explicitBrand ?? existingProfile?.PreferredBrand;
                    effectiveIntent.Category = explicitCategory ?? existingProfile?.PreferredCategory;
                }

                parsedIntent = effectiveIntent.Clone();
            }
            if (LooksLikeAmbiguousGenericDislike(normalizedMessage) &&
    existingProfile.HasActiveRecommendationContext)
            {
                effectiveIntent = parsedIntent.Clone();

                effectiveIntent.IntentType = "refine";
                effectiveIntent.RouteFlow = ChatFlowType.Refinement;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "ambiguous_dislike";

                effectiveIntent.Brand = null;
                effectiveIntent.Category = null;
                effectiveIntent.Target = null;

                effectiveIntent.PriceMin = null;
                effectiveIntent.PriceMax = null;
                effectiveIntent.TargetPrice = null;
                effectiveIntent.FilterType = PriceFilterType.None;

                effectiveIntent.HasFreshConsultationSignal = false;
                effectiveIntent.HasNarrowRefinementSignal = true;
                effectiveIntent.IsOpenRecommendation = false;
            }
            if (IsBudgetExpansionIntent(parsedIntent))
            {
                effectiveIntent = parsedIntent.Clone();

                effectiveIntent.IntentType = "refine";
                effectiveIntent.RouteFlow = ChatFlowType.Refinement;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "expand";
                effectiveIntent.HasExpandRecommendationSignal = true;
                effectiveIntent.HasNarrowRefinementSignal = true;

                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsDirectProductLookup = false;
                effectiveIntent.LookupField = null;
                effectiveIntent.MentionedProducts.Clear();

                ClearCompareContextIfNeeded(existingProfile);
            }
            if (IsPolicyIntent(parsedIntent))
            {
                effectiveIntent = parsedIntent.Clone();
                effectiveIntent.IntentType = "rag_policy";
                effectiveIntent.RouteFlow = ChatFlowType.RagPolicy;
                effectiveIntent.IsFollowUp = false;
                effectiveIntent.FollowUpType = null;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.IsOpenRecommendation = false;
                effectiveIntent.HasFreshConsultationSignal = false;
            }
            if (isFreshBrandOnlyRequestByText)
            {
                ResetOldRecommendationConstraintsForFreshBrandOnly(effectiveIntent);
            }
            if (IsExplicitProductSearchRequest(normalizedMessage, effectiveIntent))
            {
                NormalizeExplicitProductSearchIntent(normalizedMessage, effectiveIntent, existingProfile);
                NormalizeExplicitProductSearchIntent(normalizedMessage, parsedIntent, existingProfile);
            }
            else
            {
                ApplyConversationActionRules(effectiveIntent, existingProfile);
            }
            PreserveDeterministicExclusions(parsedIntent, effectiveIntent);
            _logger.LogWarning(
    "AFTER PRESERVE EXCLUSION => ParsedExcludedBrands={ParsedBrands}, ParsedExcludedProducts={ParsedProducts}, EffectiveIntentType={EffectiveIntentType}, EffectiveRouteFlow={EffectiveRouteFlow}, EffectiveFollowUpType={EffectiveFollowUpType}, EffectiveExcludedBrands={EffectiveBrands}, EffectiveExcludedProducts={EffectiveProducts}",
    string.Join(",", parsedIntent.ExcludedBrands),
    string.Join(",", parsedIntent.ExcludedProducts),
    effectiveIntent.IntentType,
    effectiveIntent.RouteFlow,
    effectiveIntent.FollowUpType,
    string.Join(",", effectiveIntent.ExcludedBrands),
    string.Join(",", effectiveIntent.ExcludedProducts)
);
            if (string.Equals(effectiveIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase)
                || effectiveIntent.IsDirectCompare)
            {
                effectiveIntent.PriceMin = null;
                effectiveIntent.PriceMax = null;
                effectiveIntent.TargetPrice = null;

                effectiveIntent.Brand = null;
                effectiveIntent.Category = null;
                effectiveIntent.Target = null;

                effectiveIntent.ForWork = false;
                effectiveIntent.ForSchool = false;
                effectiveIntent.ForCity = false;
                effectiveIntent.ForTour = false;

                effectiveIntent.WantsFuelSaving = false;
                effectiveIntent.WantsLargeStorage = false;
                effectiveIntent.WantsEasyControl = false;
                effectiveIntent.NeedsLowSeat = false;
            }
            if (effectiveIntent.IsBrandSwitch ||
    string.Equals(effectiveIntent.FollowUpType, "switch_brand", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.ComparisonFeature = null;

                ClearCompareContextIfNeeded(existingProfile);
            }
            if (!IsStrongStandaloneIntent(effectiveIntent) &&
     !LooksLikeCheapestQuestionForCurrentList(normalizedMessage) &&
     MessageAsksForCheaperOption(normalizedMessage))
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
                if (isFreshBrandOnlyRequestByText)
                {
                    ResetOldRecommendationConstraintsForFreshBrandOnly(effectiveIntent);
                    effectiveIntent.Brand = parsedIntent.Brand;
                }
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
     !MessageHasExplicitBrand(normalizedMessage) &&
     isFreshRecommendationByCurrentMessage &&
     !existingProfile.HasActiveRecommendationContext)
            {
                mergedProfile.PreferredBrand = null;
                effectiveIntent.Brand = null;
            }
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

            if (string.Equals(context.FinalRouting?.Reason, "Ambiguous exclusion while compare context is active", StringComparison.OrdinalIgnoreCase))
            {
                var snapshot = BuildRecommendationSnapshot(context.ExistingProfile);

                _clarificationStateService.SetPending(
                    context.ConversationId,
                    $"AMBIGUOUS_COMPARE_EXCLUSION::{context.NormalizedMessage}::CTX::{snapshot}");
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Ý bạn là muốn mình tư vấn lại và bỏ hãng/mẫu đó khỏi danh sách, hay bạn muốn tiếp tục so sánh hai mẫu vừa rồi? Bạn có thể trả lời: \"tư vấn lại\" hoặc \"so sánh tiếp\" nhé."
                };
            }

            if (string.Equals(context.FinalRouting?.Reason, "Compare missing second product", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(context.FinalRouting?.Reason, "Explicit compare but only one product resolved", StringComparison.OrdinalIgnoreCase))
            {
                var firstProduct = context.EffectiveIntent?.MentionedProducts?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(firstProduct))
                {
                    _clarificationStateService.SetPending(
                        context.ConversationId,
                        $"COMPARE_MISSING_PRODUCT::{firstProduct}");
                }

                var brand = context.EffectiveIntent?.Brand;

                var brandText = !string.IsNullOrWhiteSpace(brand)
                    ? brand
                    : "mẫu xe";

                var examples = string.Equals(brand, "Yamaha", StringComparison.OrdinalIgnoreCase)
                    ? "Yamaha Freego, Yamaha Grande, Yamaha Latte hoặc Yamaha Jupiter"
                    : "Vision, Latte, Freego hoặc Air Blade";

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = !string.IsNullOrWhiteSpace(firstProduct)
                        ? $"Bạn đang muốn so sánh **{firstProduct}**. Bạn muốn so với mẫu {brandText} nào cụ thể? Ví dụ: {examples}."
                        : $"Bạn muốn so sánh với mẫu {brandText} nào cụ thể? Ví dụ: {examples}."
                };
            }
            if (string.Equals(context.FinalRouting?.Reason, "Ambiguous budget expansion while compare context is active", StringComparison.OrdinalIgnoreCase))
            {
                _clarificationStateService.SetPending(
                    context.ConversationId,
                    $"AMBIGUOUS_BUDGET_EXPAND_AFTER_COMPARE::{context.NormalizedMessage}");

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply =
    "Câu này đang hơi mơ hồ vì mình vừa so sánh hai xe cho bạn.\n\n" +
    "Bạn muốn **nới ngân sách để mình gợi ý thêm mẫu xe khác**, hay muốn **giữ hai mẫu vừa rồi và so sánh thêm tiêu chí khác**?\n\n" +
    "Bạn có thể trả lời: **tư vấn thêm** hoặc **giữ so sánh** nhé."
                };
            }
            _logger.LogWarning(
                "No flow returned a result. ConversationId={ConversationId}, FlowType={FlowType}, Message={Message}",
                context.ConversationId,
                context.FinalRouting?.FlowType ?? ChatFlowType.Unknown,
                context.NormalizedMessage);
            if (string.Equals(context.FinalRouting?.Reason, "Bare compare request with multiple recommendation candidates", StringComparison.OrdinalIgnoreCase))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Mình đang có nhiều mẫu trong danh sách vừa gợi ý. Bạn muốn mình **so sánh 2 xe đầu**, **so sánh cả 3 mẫu**, hay chỉ định rõ 2 mẫu muốn so sánh?"
                };
            }
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
        private async Task<ChatResponse> HandleRagPolicyAsync(ChatOrchestrationContext context)
        {
            var ragContext = await TryGetRagContextAsync(context);

            if (string.IsNullOrWhiteSpace(ragContext))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Mình chưa tìm thấy thông tin chính sách phù hợp trong dữ liệu hiện tại. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ chính xác hơn nhé."
                };
            }

            if (string.IsNullOrWhiteSpace(ragContext))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Mình chưa tìm thấy thông tin chính sách phù hợp trong dữ liệu hiện tại. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ chính xác hơn nhé."
                };
            }

            var prompt =
 $@"Bạn là chatbot hỗ trợ khách hàng cho website bán xe máy.

Chỉ trả lời dựa trên RAG context bên dưới.
Không bịa chính sách, không tự cam kết hoàn tiền/bảo hành nếu context không nói rõ.
Nếu người dùng không nhắc hãng cụ thể, không được tự gán câu trả lời cho Honda, Yamaha hay bất kỳ hãng nào.
Không tự thêm giấy phép lái xe nếu RAG context không nói rõ.
Với câu hỏi giấy tờ mua xe, chỉ nêu CCCD/CMND, thông tin đăng ký xe, và hồ sơ trả góp nếu người dùng hỏi/muốn trả góp.
Nếu khách hỏi “thủ tục như thế nào” sau câu hỏi về giấy tờ/mua xe, hiểu là thủ tục mua xe mới; không dùng quy trình đổi trả, hoàn tiền, mang xe về showroom hoặc kỹ thuật viên kiểm tra trừ khi khách hỏi rõ về đổi trả/bảo hành.
Chỉ gợi ý bấm “Gặp nhân viên” khi câu hỏi liên quan đến kiểm tra hồ sơ cụ thể, xử lý đơn, lỗi thanh toán, đổi trả, bảo hành, hoặc khi context không đủ để trả lời chắc chắn.
Nếu câu hỏi chỉ là thông tin chung như trả góp, giấy tờ, thủ tục mua xe, không cần kết thúc bằng lời mời “Gặp nhân viên”.
Trả lời ngắn gọn, rõ ràng, thân thiện bằng tiếng Việt.
Không nhắc đến từ 'RAG' hoặc 'context'.

Câu hỏi khách hàng: {context.NormalizedMessage}

RAG context:
{ragContext}";

            try
            {
                var aiResponse = await _openAIService.AskAsync(new AIRequestContext
                {
                    ConversationId = context.ConversationId,
                    OriginalUserMessage = context.Request?.Message ?? context.NormalizedMessage,
                    EffectivePrompt = prompt,
                    RagContext = ragContext,
                    Channel = context.Request?.Channel ?? "web",
                    UserId = context.Request?.UserId
                });

                if (aiResponse != null &&
                    aiResponse.Success &&
                    !string.IsNullOrWhiteSpace(aiResponse.Reply))
                {
                    aiResponse.ConversationId ??= context.ConversationId;
                    aiResponse.UsedAI = true;
                    aiResponse.UsedTool = "RAG";
                    return aiResponse;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "RAG policy answer failed. ConversationId={ConversationId}, Message={Message}",
                    context.ConversationId,
                    context.NormalizedMessage);
            }

            return new ChatResponse
            {
                Success = true,
                UsedAI = false,
                ConversationId = context.ConversationId,
                Reply = BuildSimplePolicyReplyFromRag(ragContext)
            };
        }
        private static string BuildSimplePolicyReplyFromRag(string ragContext)
        {
            if (string.IsNullOrWhiteSpace(ragContext))
                return "Mình chưa tìm thấy thông tin phù hợp trong dữ liệu hiện tại.";

            var lines = ragContext
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(6)
                .ToList();

            if (lines.Count == 0)
                return "Mình chưa tìm thấy thông tin phù hợp trong dữ liệu hiện tại.";

            return string.Join("\n", lines);
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
            Console.WriteLine("=== TELEGRAM/CHAT FLOW DEBUG ===");
            Console.WriteLine($"Channel: {context.Request?.Channel}");
            Console.WriteLine($"UserId: {context.Request?.UserId}");
            Console.WriteLine($"ConversationId: {context.ConversationId}");
            Console.WriteLine($"Message: {context.NormalizedMessage}");
            Console.WriteLine($"FlowType: {flowType}");
            Console.WriteLine($"Reason: {context.FinalRouting?.Reason}");
            Console.WriteLine($"IntentType: {context.EffectiveIntent?.IntentType}");
            Console.WriteLine($"IsOpenRecommendation: {context.EffectiveIntent?.IsOpenRecommendation}");
            Console.WriteLine($"HasFreshConsultationSignal: {context.EffectiveIntent?.HasFreshConsultationSignal}");
            Console.WriteLine($"Brand: {context.EffectiveIntent?.Brand}");
            Console.WriteLine($"Category: {context.EffectiveIntent?.Category}");
            Console.WriteLine($"PriceMin: {context.EffectiveIntent?.PriceMin}");
            Console.WriteLine($"PriceMax: {context.EffectiveIntent?.PriceMax}");
            Console.WriteLine($"TargetPrice: {context.EffectiveIntent?.TargetPrice}");
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

            if (LooksLikeHumanSupportOrAfterSalesRequest(context.NormalizedMessage) &&
    !LooksLikeInformationalPolicyQuestion(context.NormalizedMessage))
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
            if (string.Equals(flowType, ChatFlowType.RagPolicy, StringComparison.OrdinalIgnoreCase))
            {
                return await HandleRagPolicyAsync(context);
            }
            if (string.Equals(flowType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
            {
                return await _productLookupFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }
            if (LooksLikeUnknownBrand(context.NormalizedMessage, context.EffectiveIntent))
            {
                _clarificationStateService.SetPending(
                    context.ConversationId,
                    $"UNKNOWN_BRAND::{context.NormalizedMessage}");

                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Mình chưa nhận ra hãng bạn đang nhập 🤔\n\n" +
                            "Bạn kiểm tra lại giúp mình tên hãng nhé.\n" +
                            "Ví dụ: **Honda, Yamaha, Suzuki, SYM, Piaggio**.\n\n" +
                            "Hoặc bạn có thể nhập lại, mình sẽ tư vấn ngay 👍"
                };
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
                if (ShouldAskScopeBeforeRetryAfterExclusion(context))
                {
                    _clarificationStateService.SetPending(
                        context.ConversationId,
                        "RETRY_AFTER_EXCLUSION_NEEDS_SCOPE");

                    return new ChatResponse
                    {
                        Success = true,
                        UsedAI = false,
                        ConversationId = context.ConversationId,
                        Reply =
    "Mình đã đổi sang vài phương án khác ở lượt trước. Nếu tư vấn lại tiếp mà không có tiêu chí mới thì kết quả dễ bị lan sang các mẫu khá xa nhu cầu.\n\n" +
    "Bạn muốn mình xử lý theo hướng nào?\n" +
    "- **Nới rộng tiêu chí** để tìm thêm mẫu khác\n" +
    "- **Giữ danh sách trên** và chọn giúp 1 mẫu phù hợp nhất\n" +
    "- Hoặc bạn nói thêm **hãng, loại xe, tầm giá** muốn ưu tiên"
                    };
                }

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
            if (string.Equals(flowType, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Executing RECOMMENDATION_FOLLOW_UP flow");

                return await _recommendationFollowUpService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
            }
            if (context.FinalRouting?.Reason == "Excluded brand not present in current recommendation list")
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = context.ConversationId,
                    Reply = "Trong các mẫu mình vừa gợi ý hiện chưa có hãng đó. Bạn muốn mình loại thêm hãng nào khác, hay tư vấn lại theo tiêu chí mới?"
                };
            }
            if (string.Equals(flowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Executing RECOMMENDATION flow without RAG context");

                return await _recommendationFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile,
                    ragContext: null);
            }
            return null;
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
        private async Task<(bool Handled, ChatResponse? Response)> TryHandlePendingClarificationAsync(
     ChatRequest request,
     string conversationId,
     long elapsedMs)
        {
            var originalMessage = request.Message?.Trim() ?? string.Empty;
            var pendingClarification = _clarificationStateService.GetPending(conversationId);

            if (string.IsNullOrWhiteSpace(pendingClarification))
                return (false, null);
            if (pendingClarification.StartsWith("UNKNOWN_BRAND::", StringComparison.OrdinalIgnoreCase))
            {
                _clarificationStateService.Clear(conversationId);
                return (false, null);
            }
            if (pendingClarification.StartsWith("COMPARE_MISSING_PRODUCT::", StringComparison.OrdinalIgnoreCase))
            {
                var firstProduct = pendingClarification
                    .Replace("COMPARE_MISSING_PRODUCT::", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                if (string.IsNullOrWhiteSpace(firstProduct))
                {
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                var reply = NormalizeText(originalMessage);

                bool userChangedToLookup =
                    reply.Contains("gia") ||
                    reply.Contains("bao nhieu") ||
                    reply.Contains("con hang") ||
                    reply.Contains("ton kho") ||
                    reply.Contains("chi tiet") ||
                    reply.Contains("thong tin") ||
                    reply.Contains("bao nhieu cc");

                bool userChangedToNewIntent =
                    userChangedToLookup ||
                    reply.Contains("tu van") ||
                    reply.Contains("goi y") ||
                    reply.Contains("don hang") ||
                    reply.Contains("tra cuu");

                if (userChangedToNewIntent)
                {
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                bool userSentFullCompareRequest =
                    reply.Contains("so sanh") ||
                    reply.Contains(" so voi ") ||
                    reply.Contains(" voi ") ||
                    Regex.IsMatch(
                        reply,
                        @"\b(?:xe|mau|con)?\s*(?:thu\s*)?(?:[1-5]|nhat|hai|ba|tu|nam)\b",
                        RegexOptions.IgnoreCase);

                if (userSentFullCompareRequest)
                {
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                request.Message = $"so sánh {firstProduct} với {originalMessage}";
                _clarificationStateService.Clear(conversationId);
                return (false, null);
            }

            if (pendingClarification.StartsWith("AMBIGUOUS_COMPARE_EXCLUSION::", StringComparison.OrdinalIgnoreCase))
            {
                var parts = pendingClarification.Split("::CTX::");

                var originalExcludedMessage = parts[0]
                    .Replace("AMBIGUOUS_COMPARE_EXCLUSION::", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                var previousContext = parts.Length > 1 ? parts[1] : "";
                var reply = NormalizeText(originalMessage);

                if (HasExclusionIntentText(originalMessage))
                {
                    var mergedExcludedMessage = $"{originalExcludedMessage} {originalMessage}".Trim();

                    _clarificationStateService.SetPending(
                        conversationId,
                        $"AMBIGUOUS_COMPARE_EXCLUSION::{mergedExcludedMessage}::CTX::{previousContext}");

                    return (true, new ChatResponse
                    {
                        Success = true,
                        UsedAI = false,
                        ConversationId = conversationId,
                        ElapsedMs = elapsedMs,
                        Reply = "Mình đã ghi nhận thêm điều kiện loại trừ đó. Bạn muốn mình **tư vấn lại** theo các điều kiện này, hay **so sánh tiếp** hai mẫu vừa rồi?"
                    });
                }

                if (reply.Contains("tu van") || reply.Contains("goi y") || reply.Contains("loc lai") || reply.Contains("bo hang") || reply.Contains("bo mau"))
                {
                    request.Message = $"tư vấn lại theo tiêu chí trước đó {previousContext} {originalExcludedMessage}".Trim();
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                if (reply.Contains("so sanh") || reply.Contains("tiep tuc") || reply.Contains("so sanh tiep"))
                {
                    _clarificationStateService.Clear(conversationId);

                    return (true, new ChatResponse
                    {
                        Success = true,
                        UsedAI = false,
                        ConversationId = conversationId,
                        ElapsedMs = elapsedMs,
                        Reply = "Ok, mình sẽ tiếp tục giữ phần so sánh hai mẫu vừa rồi. Bạn muốn so sánh thêm về giá, độ dễ đi, cốp xe hay mức tiết kiệm xăng?"
                    });
                }

                return (true, new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    ElapsedMs = elapsedMs,
                    Reply = "Mình cần bạn chọn rõ một hướng nhé: \"tư vấn lại\" để bỏ hãng/mẫu đó khỏi danh sách, hoặc \"so sánh tiếp\" để tiếp tục so sánh hai xe vừa rồi."
                });
            }

            if (pendingClarification.StartsWith("AMBIGUOUS_BUDGET_EXPAND_AFTER_COMPARE::", StringComparison.OrdinalIgnoreCase))
            {
                var reply = NormalizeText(originalMessage);

                if (reply.Contains("tu van") || reply.Contains("tu van them") ||
    reply.Contains("goi y") || reply.Contains("them mau") ||
    reply.Contains("xe khac") || reply.Contains("mau khac") ||
    reply.Contains("noi ngan sach") || reply.Contains("cao hon") ||
    reply.Contains("xem") || reply.Contains("xem them"))
                {
                    var profile = await _conversationPreferenceService.GetAsync(conversationId);

                    decimal anchorPrice = 30_000_000m;

                    if (profile != null)
                    {
                        if (profile.TargetPrice.HasValue)
                            anchorPrice = profile.TargetPrice.Value;
                        else if (profile.PriceMax.HasValue)
                            anchorPrice = profile.PriceMax.Value;
                        else if (profile.LastRecommendedProducts?.Count > 0)
                            anchorPrice = 30_000_000m;

                        ClearCompareContextIfNeeded(profile);

                        profile.HasActiveRecommendationContext = true;
                        profile.ActiveFlow = ChatFlowType.Recommendation;

                        profile.PriceMin = anchorPrice + 1_000_000m;
                        profile.PriceMax = anchorPrice + 10_000_000m; 
                        profile.TargetPrice = anchorPrice + 5_000_000m;
                        profile.FilterType = PriceFilterType.Range;
                    }

                    request.Message =
                        $"tư vấn thêm mẫu xe khác trong khoảng {(anchorPrice + 1_000_000m):0} đến {(anchorPrice + 15_000_000m):0} VNĐ, không lấy lại các mẫu vừa tư vấn";

                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                if (reply.Contains("so sanh") || reply.Contains("so sanh tiep") ||
                    reply.Contains("tiep tuc") || reply.Contains("giu so sanh"))
                {
                    _clarificationStateService.Clear(conversationId);

                    return (true, new ChatResponse
                    {
                        Success = true,
                        UsedAI = false,
                        ConversationId = conversationId,
                        ElapsedMs = elapsedMs,
                        Reply = "Ok, mình sẽ tiếp tục phần so sánh hai mẫu vừa rồi. Bạn muốn so sánh thêm về **giá**, **độ dễ đi**, **kiểu dáng**, **độ bền** hay **đi phố**?"
                    });
                }

                return (true, new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    ElapsedMs = elapsedMs,
                    Reply = "Mình cần bạn chọn rõ một hướng nhé: **tư vấn thêm** để nới ngân sách tìm xe khác, hoặc **so sánh tiếp** để tiếp tục so sánh hai mẫu vừa rồi."
                });
            }
            if (pendingClarification.StartsWith("RETRY_AFTER_EXCLUSION_NEEDS_SCOPE", StringComparison.OrdinalIgnoreCase))
            {
                var reply = NormalizeText(originalMessage);

                if (reply.Contains("noi rong") ||
                    reply.Contains("tim them") ||
                    reply.Contains("mau khac") ||
                    reply.Contains("xe khac") ||
                    reply.Contains("hang khac") ||
                    reply.Contains("loai khac"))
                {
                    request.Message = "tư vấn thêm mẫu khác, có thể nới rộng tiêu chí nhưng vẫn tránh các hãng hoặc mẫu tôi đã loại";
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                if (reply.Contains("chon giup") ||
                    reply.Contains("chon 1") ||
                    reply.Contains("chot") ||
                    reply.Contains("mau phu hop nhat") ||
                    reply.Contains("xe phu hop nhat") ||
                    reply.Contains("giu danh sach"))
                {
                    request.Message = "chọn giúp 1 xe phù hợp nhất trong danh sách vừa gợi ý";
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                if (reply.Contains("tu dau") || reply.Contains("reset") || reply.Contains("bo tieu chi cu"))
                {
                    request.Message = "tư vấn lại từ đầu";
                    _clarificationStateService.Clear(conversationId);
                    return (false, null);
                }

                _clarificationStateService.Clear(conversationId);
                return (false, null);
            }
            var clarificationResolution = ResolveClarificationReply(originalMessage, pendingClarification);

            if (clarificationResolution.IsConfirmed)
            {
                request.Message = clarificationResolution.ResolvedMessage!;
                _clarificationStateService.Clear(conversationId);
                return (false, null);
            }

            if (clarificationResolution.IsRejected)
            {
                _clarificationStateService.Clear(conversationId);

                return (true, new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    ConversationId = conversationId,
                    ElapsedMs = elapsedMs,
                    Reply = "Không sao nhé. Bạn hãy nhập lại giúp mình tên xe hoặc câu hỏi rõ hơn một chút, ví dụ: \"Honda Vision giá bao nhiêu\" hoặc \"xe ga cho nữ khoảng 40 triệu\"."
                });
            }

            if (!string.IsNullOrWhiteSpace(clarificationResolution.ResolvedMessage))
            {
                request.Message = clarificationResolution.ResolvedMessage!;
                _clarificationStateService.Clear(conversationId);
            }

            return (false, null);
        }
        private async Task<ParsedIntent> ParseIntentAsync(
    string normalizedMessage,
    CustomerPreferenceProfile? existingProfile)
        {
            var parsedIntent = await ParseBaseIntentAsync(normalizedMessage);

            ApplyPriceIntent(parsedIntent, normalizedMessage);
            if (LooksLikeGlobalProductStatisticQuery(normalizedMessage) &&
     !LooksLikeCheapestQuestionForCurrentList(normalizedMessage))
            {
                parsedIntent.IntentType = "product_search";
                parsedIntent.RouteFlow = ChatFlowType.ProductSearch;
                parsedIntent.IsProductSearch = true;
                parsedIntent.IsFollowUp = false;
                parsedIntent.FollowUpType = null;
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                parsedIntent.HasFreshConsultationSignal = false;
                parsedIntent.HasDeterministicProductIntent = true;

                parsedIntent.PriceMin = null;
                parsedIntent.PriceMax = null;
                parsedIntent.TargetPrice = null;
                parsedIntent.FilterType = PriceFilterType.None;

                return parsedIntent;
            }
            if (LooksLikeInstallmentPolicyQuestion(normalizedMessage))
            {
                parsedIntent.IntentType = "rag_policy";
                parsedIntent.RouteFlow = ChatFlowType.RagPolicy;
                parsedIntent.IsFollowUp = false;
                parsedIntent.FollowUpType = null;
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                return parsedIntent;
            }
            if (LooksLikePurchaseDocumentQuestion(normalizedMessage))
            {
                parsedIntent.IntentType = "rag_policy";
                parsedIntent.RouteFlow = ChatFlowType.RagPolicy;
                parsedIntent.IsFollowUp = false;
                parsedIntent.FollowUpType = null;
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                return parsedIntent;
            }
            if (LooksLikeCompareFeatureFollowUp(normalizedMessage, existingProfile))
            {
                parsedIntent.IntentType = "compare";
                parsedIntent.RouteFlow = ChatFlowType.Compare;
                parsedIntent.IsDirectCompare = true;
                parsedIntent.IsFollowUp = true;
                parsedIntent.FollowUpType = "compare_feature";
                parsedIntent.ComparisonFeature = DetectSimpleCompareFeature(normalizedMessage);
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                return parsedIntent;
            }
            if (LooksLikeOrdinalOnlyCompare(normalizedMessage, existingProfile))
            {
                parsedIntent.IntentType = "compare";
                parsedIntent.RouteFlow = ChatFlowType.Compare;
                parsedIntent.IsDirectCompare = true;
                parsedIntent.IsFollowUp = true;
                parsedIntent.FollowUpType = "compare";
                parsedIntent.HasDeterministicProductIntent = true;
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                return parsedIntent;
            }
            if (LooksLikeBudgetExpansionFollowUp(normalizedMessage, existingProfile))
            {
                parsedIntent.IntentType = "refine";
                parsedIntent.IsFollowUp = true;
                parsedIntent.FollowUpType = "expand";
                parsedIntent.HasExpandRecommendationSignal = true;
                parsedIntent.HasNarrowRefinementSignal = true;
                parsedIntent.IsOutOfScope = false;
                parsedIntent.IsNoise = false;
                return parsedIntent;
            }
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
            if (string.Equals(parsedIntent.FollowUpType, "cheapest_in_list", StringComparison.OrdinalIgnoreCase) ||
    LooksLikeCheapestQuestionForCurrentList(normalizedMessage))
            {
                return false;
            }
            if (IsDeterministicIntentConfident(parsedIntent))
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
            if (parsedIntent.ExcludedProducts != null &&
    parsedIntent.ExcludedProducts.Count > 0)
                return false;

            if (parsedIntent.ExcludedBrands != null &&
                parsedIntent.ExcludedBrands.Count > 0)
                return false;

            if (parsedIntent.ExcludedCategories != null &&
                parsedIntent.ExcludedCategories.Count > 0)
                return false;
            if (parsedIntent.IsDirectCompare &&
    string.Equals(parsedIntent.FollowUpType, "compare", StringComparison.OrdinalIgnoreCase) &&
    HasOrdinalReferenceText(normalizedMessage) &&
    parsedIntent.MentionedProducts != null &&
    parsedIntent.MentionedProducts.Count >= 1)
            {
                return false;
            }

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
        private static bool HasExclusionIntentText(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("khong ") ||
                   text.Contains("ko ") ||
                   text.Contains("k ") ||
                   text.Contains("khong thich") ||
                   text.Contains("khong muon") ||
                   text.Contains("khong lay") ||
                   text.Contains("bo ") ||
                   text.Contains("loai ") ||
                   text.Contains("tru ");
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

            if (Enum.TryParse<ConversationAction>(llmIntent.Action, true, out var action))
            {
                parsedIntent.Action = action;
            }

            parsedIntent.KeepConstraints |= llmIntent.KeepConstraints;
            parsedIntent.ExcludePreviousProducts |= llmIntent.ExcludePreviousProducts;
            parsedIntent.ExcludePreviousBrands |= llmIntent.ExcludePreviousBrands;

            if (parsedIntent.Action == ConversationAction.ChangeProduct)
            {
                parsedIntent.IntentType = "refine";
                parsedIntent.IsFollowUp = true;
                parsedIntent.FollowUpType = "change_product";
                parsedIntent.HasNarrowRefinementSignal = true;
                parsedIntent.HasDeterministicProductIntent = true;
            }
        
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
           var isRestartRecommendation =
    string.Equals(
        effectiveIntent.FollowUpType,
        "restart_recommendation",
        StringComparison.OrdinalIgnoreCase);

if (isRestartRecommendation)
{
    await _conversationPreferenceService.ResetForFreshConsultationAsync(conversationId);

    effectiveIntent.IntentType = "recommend";
    effectiveIntent.RouteFlow = ChatFlowType.Recommendation;
    effectiveIntent.IsFollowUp = false;
    effectiveIntent.IsDirectCompare = false;
    effectiveIntent.IsOpenRecommendation = true;
    effectiveIntent.HasFreshConsultationSignal = true;

    effectiveIntent.KeepConstraints = false;
    effectiveIntent.ExcludePreviousProducts = false;
    effectiveIntent.ExcludePreviousBrands = false;

    effectiveIntent.Brand = null;
    effectiveIntent.Category = null;
    effectiveIntent.Target = null;

    effectiveIntent.PriceMin = null;
    effectiveIntent.PriceMax = null;
    effectiveIntent.TargetPrice = null;
    effectiveIntent.FilterType = PriceFilterType.None;

    effectiveIntent.ExcludedBrands.Clear();
    effectiveIntent.ExcludedProducts.Clear();
    effectiveIntent.ExcludedCategories.Clear();

    effectiveIntent.ForWork = false;
    effectiveIntent.ForSchool = false;
    effectiveIntent.ForCity = false;
    effectiveIntent.ForTour = false;

    effectiveIntent.WantsFuelSaving = false;
    effectiveIntent.WantsLargeStorage = false;
    effectiveIntent.WantsEasyControl = false;
    effectiveIntent.NeedsLowSeat = false;

    effectiveIntent.PrefersMaleStyle = false;
    effectiveIntent.PrefersFemaleStyle = false;

    effectiveIntent.RequestedStyles.Clear();
}

var isFreshRecommendation =
    contextDecision == RecommendationContextDecision.New ||
    isFreshRecommendationByCurrentMessage ||
    isRestartRecommendation;

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
            var currentGoalType = NormalizeGoalType(context.FinalRouting?.FlowType, profile);
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
                if (string.Equals(currentGoalType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase) &&
                    profile.LastComparedProducts != null &&
                    profile.LastComparedProducts.Count > 0)
                {
                    mentionedProductNames = profile.LastComparedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else if (profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count > 0)
                {
                    mentionedProductNames = profile.LastRecommendedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else if (profile.LastMentionedProducts != null && profile.LastMentionedProducts.Count > 0)
                {
                    mentionedProductNames = profile.LastMentionedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else if (profile.LastComparedProducts != null && profile.LastComparedProducts.Count > 0)
                {
                    mentionedProductNames = profile.LastComparedProducts
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            if (string.Equals(currentGoalType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase) &&
    mentionedProductNames.Count >= 2)
            {
                profile.HasActiveCompareContext = true;
                profile.LastComparedProducts = mentionedProductNames
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();

                profile.ActiveFlow = ChatFlowType.Compare;
            }
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
        || text.Contains("tư vấn lại")
        || text.Contains("so sánh tiếp")
        || text.Contains("mình cần thêm")
        || text.Contains("nới rộng tiêu chí")
        || text.Contains("giữ danh sách trên")
        || text.Contains("bạn muốn mình xử lý theo hướng nào");
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
    (effectiveIntent.MentionedProducts?.Count > 0) ||
    (effectiveIntent.ExcludedProducts?.Count > 0) ||
    (effectiveIntent.ExcludedBrands?.Count > 0) ||
    (effectiveIntent.ExcludedCategories?.Count > 0);

            if (string.Equals(turnContext.GoalContinuity, "new_goal", StringComparison.OrdinalIgnoreCase)
    && hasDomainSignal
    && !IsStrongStandaloneIntent(effectiveIntent))
            {
                return RecommendationContextDecision.New;
            }

            return RecommendationContextDecision.None;
        }


        private static void PreserveDeterministicExclusions(
     ParsedIntent parsedIntent,
     ParsedIntent effectiveIntent)
        {
            if (parsedIntent == null || effectiveIntent == null)
                return;

            bool hasParsedExclusion =
                parsedIntent.ExcludedProducts.Any() ||
                parsedIntent.ExcludedBrands.Any() ||
                parsedIntent.ExcludedCategories.Any();

            if (!hasParsedExclusion)
                return;

            foreach (var item in parsedIntent.ExcludedProducts)
                effectiveIntent.ExcludedProducts.Add(item);

            foreach (var item in parsedIntent.ExcludedBrands)
                effectiveIntent.ExcludedBrands.Add(item);

            foreach (var item in parsedIntent.ExcludedCategories)
                effectiveIntent.ExcludedCategories.Add(item);

            effectiveIntent.IsOutOfScope = false;
            effectiveIntent.IsNoise = false;
            effectiveIntent.IsAck = false;

            if (string.Equals(effectiveIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
     effectiveIntent.IsDirectCompare ||
     string.Equals(effectiveIntent.RouteFlow, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.IsDirectCompare = false;
                effectiveIntent.RouteFlow = null;
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType ??= "exclude";
                effectiveIntent.HasNarrowRefinementSignal = true;
                effectiveIntent.HasDeterministicProductIntent = true;
                return;
            }

            bool looksLikeFreshRecommendation =
                string.Equals(effectiveIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase) ||
                effectiveIntent.IsOpenRecommendation ||
                effectiveIntent.HasFreshConsultationSignal ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Brand) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Category) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.Target) ||
                effectiveIntent.PriceMin.HasValue ||
                effectiveIntent.PriceMax.HasValue ||
                effectiveIntent.TargetPrice.HasValue ||
                effectiveIntent.ForWork ||
                effectiveIntent.ForSchool ||
                effectiveIntent.ForCity ||
                effectiveIntent.ForTour ||
                effectiveIntent.PrefersMaleStyle ||
                effectiveIntent.PrefersFemaleStyle;

            if (!looksLikeFreshRecommendation)
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType ??= "exclude";
                effectiveIntent.HasDeterministicProductIntent = true;
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
        private static bool IsExplicitProductSearchRequest(string message, ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            if (string.Equals(intent.IntentType, "product_search", StringComparison.OrdinalIgnoreCase) ||
                intent.IsProductSearch ||
                string.Equals(intent.RouteFlow, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var text = NormalizeText(message);

            bool hasListVerb =
                text.Contains("dua ra") ||
                text.Contains("liet ke") ||
                text.Contains("ke ra") ||
                text.Contains("danh sach") ||
                text.Contains("shop co") ||
                text.Contains("cua hang co") ||
                text.Contains("co nhung xe nao") ||
                text.Contains("co xe nao") ||
                text.Contains("nhung xe nao") ||
                text.Contains("tat ca xe") ||
                text.Contains("toan bo xe");

            bool hasVehicleSignal =
                text.Contains("xe") ||
                text.Contains("mau") ||
                text.Contains("san pham");

            return hasListVerb && hasVehicleSignal;
        }
        private static void NormalizeExplicitProductSearchIntent(
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            if (intent == null)
                return;

            intent.IntentType = "product_search";
            intent.IsProductSearch = true;
            intent.IsOpenRecommendation = false;
            intent.IsFollowUp = false;
            intent.FollowUpType = null;

            intent.Action = ConversationAction.None;
            intent.KeepConstraints = false;
            intent.ExcludePreviousProducts = false;
            intent.ExcludePreviousBrands = false;

            intent.HasFreshConsultationSignal = false;
            intent.HasExpandRecommendationSignal = false;
            intent.HasNarrowRefinementSignal = false;

            intent.ExcludedProducts.Clear();

            if (!MessageHasExplicitNegativeBrand(normalizedMessage))
                intent.ExcludedBrands.Clear();

            if (!LooksLikeGlobalProductStatisticQuery(normalizedMessage) &&
     !MessageHasExplicitCategory(normalizedMessage))
            {
                intent.Category = null;
            }

            if (!LooksLikeGlobalProductStatisticQuery(normalizedMessage) &&
                !MessageHasExplicitBrand(normalizedMessage))
            {
                intent.Brand = null;
            }
        }
        private static bool MessageHasExplicitNegativeBrand(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("khong honda") ||
                   text.Contains("khong yamaha") ||
                   text.Contains("khong suzuki") ||
                   text.Contains("khong sym") ||
                   text.Contains("khong piaggio") ||
                   text.Contains("khong thich honda") ||
                   text.Contains("khong thich yamaha") ||
                   text.Contains("khong thich suzuki") ||
                   text.Contains("khong thich sym") ||
                   text.Contains("khong thich piaggio") ||
                   text.Contains("tru honda") ||
                   text.Contains("tru yamaha") ||
                   text.Contains("tru suzuki") ||
                   text.Contains("tru sym") ||
                   text.Contains("tru piaggio");
        }

        private static bool MessageHasExplicitNegativeCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("khong xe ga") ||
                   text.Contains("khong thich xe ga") ||
                   text.Contains("khong xe so") ||
                   text.Contains("khong thich xe so") ||
                   text.Contains("khong con tay") ||
                   text.Contains("khong thich con tay");
        }
        private static bool MessageHasExplicitCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("xe so") ||
                   text.Contains("xe ga") ||
                   text.Contains("con tay") ||
                   text.Contains("tay ga");
        }
        private static bool IsFreshBrandOnlyRequestByText(string message, ParsedIntent intent)
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

            if (!hasFreshPhrase)
                return false;

            if (!MessageHasExplicitBrand(message))
                return false;

            if (MessageHasExplicitCategory(message))
                return false;

            if (MessageHasExplicitPrice(message))
                return false;

            if (text.Contains("cho nam") ||
                text.Contains("cho nu") ||
                text.Contains("cho nữ") ||
                text.Contains("di lam") ||
                text.Contains("di hoc") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("cop rong") ||
                text.Contains("de di") ||
                text.Contains("de lai") ||
                text.Contains("de chong chan"))
            {
                return false;
            }

            return true;
        }
        private static bool LooksLikeInformationalPolicyQuestion(string message)
        {
            var text = NormalizeText(message);

            bool hasPolicyKeyword =
                text.Contains("bao hanh") ||
                text.Contains("tra gop") ||
                text.Contains("bao duong") ||
                text.Contains("giao hang") ||
                text.Contains("dat coc") ||
                text.Contains("doi tra") ||
                text.Contains("giay to") ||
                text.Contains("bien so");

            bool asksInfo =
                text.Contains("bao lau") ||
                text.Contains("nhu nao") ||
                text.Contains("the nao") ||
                text.Contains("ra sao") ||
                text.Contains("may thang") ||
                text.Contains("can gi") ||
                text.Contains("thu tuc") ||
                text.Contains("chinh sach");

            return hasPolicyKeyword && asksInfo;
        }
        private static void ResetOldRecommendationConstraintsForFreshBrandOnly(ParsedIntent intent)
        {
            if (intent == null)
                return;

            intent.Category = null;
            intent.Target = null;

            intent.PriceMin = null;
            intent.PriceMax = null;
            intent.TargetPrice = null;
            intent.FilterType = PriceFilterType.None;

            intent.ForWork = false;
            intent.ForSchool = false;
            intent.ForCity = false;
            intent.ForTour = false;

            intent.WantsFuelSaving = false;
            intent.WantsLargeStorage = false;
            intent.WantsEasyControl = false;
            intent.NeedsLowSeat = false;

            intent.PrefersMaleStyle = false;
            intent.PrefersFemaleStyle = false;

            intent.ComparisonFeature = null;

            intent.RequestedStyles.Clear();
            intent.ExcludedCategories.Clear();
            intent.ExcludedProducts.Clear();

            intent.IsFollowUp = false;
            intent.FollowUpType = null;
            intent.HasNarrowRefinementSignal = false;
            intent.HasExpandRecommendationSignal = false;
            intent.HasFreshConsultationSignal = true;
        }
        private static bool LooksLikeCheapestQuestionForCurrentList(string message)
        {
            var text = NormalizeText(message);

            if (MessageAsksGlobalScope(message))
                return false;

            return text.Contains("trong nhom") ||
                   text.Contains("vua goi y") ||
                   text.Contains("ben tren") ||
                   text.Contains("trong danh sach") ||
                   text.Contains("may mau vua goi y") ||
                   text.Contains("cac mau vua goi y");
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
                   text.Contains("hoda") ||
                   text.Contains("honad") ||
                   text.Contains("hond") ||

                   text.Contains("yamaha") ||
                   text.Contains("yamha") ||
                   text.Contains("yamah") ||
                   text.Contains("yamaa") ||

                   text.Contains("suzuki") ||
                   text.Contains("suzki") ||

                   text.Contains("sym") ||

                   text.Contains("piaggio") ||
                   text.Contains("piago") ||
                   text.Contains("piagio");
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
        private static void ApplyConversationActionRules(
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile? profile)
        {
            if (effectiveIntent == null || profile == null)
                return;

            if (effectiveIntent.Action == ConversationAction.ChangeProduct ||
                string.Equals(effectiveIntent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "refine";
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.FollowUpType = "change_product";
                effectiveIntent.HasNarrowRefinementSignal = true;
                effectiveIntent.HasDeterministicProductIntent = true;
                effectiveIntent.ExcludePreviousProducts = true;
                effectiveIntent.KeepConstraints = true;

                if (profile.LastRecommendedProducts != null)
                {
                    foreach (var productName in profile.LastRecommendedProducts)
                    {
                        if (!string.IsNullOrWhiteSpace(productName))
                            effectiveIntent.ExcludedProducts.Add(productName);
                    }
                }

                if (profile.CurrentRecommendedProducts != null)
                {
                    foreach (var productName in profile.CurrentRecommendedProducts)
                    {
                        if (!string.IsNullOrWhiteSpace(productName))
                            effectiveIntent.ExcludedProducts.Add(productName);
                    }
                }
            }
        }
        private static bool IsResetCommand(string? message)
        {
            var text = NormalizeText(message);
            return text == "reset" ||
                   text == "lam lai" ||
                   text == "làm lại" ||
                   text == "bat dau lai" ||
                   text == "bắt đầu lại";
        }
        private static string BuildRecommendationSnapshot(CustomerPreferenceProfile? profile)
        {
            if (profile == null)
                return "";

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(profile.PreferredCategory))
                parts.Add(profile.PreferredCategory);

            if (!string.IsNullOrWhiteSpace(profile.PreferredBrand))
                parts.Add(profile.PreferredBrand);

            if (profile.TargetPrice.HasValue)
                parts.Add($"khoảng {profile.TargetPrice.Value:0} VNĐ");

            if (profile.PriceMin.HasValue && profile.PriceMax.HasValue)
                parts.Add($"từ {profile.PriceMin.Value:0} đến {profile.PriceMax.Value:0} VNĐ");

            if (!string.IsNullOrWhiteSpace(profile.Target))
                parts.Add(profile.Target);

            return string.Join(", ", parts);
        }
        private static bool LooksLikeHumanSupportOrAfterSalesRequest(string message)
        {
            var text = NormalizeText(message);

            string[] strongHumanSupportKeywords =
            {
        "bao hanh",
        "loi xe",
        "xe bi loi",
        "hong xe",

        "doi tra",
        "doi xe",
        "muon doi xe",
        "hoan tien",
        "khieu nai",

        "loi thanh toan",
        "thanh toan loi",
        "thanh toan bi loi",
        "khong thanh toan duoc",

        "loi chuyen khoan",
        "chuyen khoan loi",
        "chuyen khoan bi loi",
        "khong chuyen khoan duoc",

        "gap nhan vien",
        "muon gap nhan vien",
        "nhan vien tu van",
        "gap admin",
        "admin",
        "ho tro truc tiep"
    };

            return strongHumanSupportKeywords.Any(k => text.Contains(k));
        }
        private static bool IsBudgetExpansionIntent(ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            return string.Equals(intent.IntentType, "refine", StringComparison.OrdinalIgnoreCase) &&
                   (
                       string.Equals(intent.FollowUpType, "expand", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(intent.FollowUpType, "expand_recommendation", StringComparison.OrdinalIgnoreCase) ||
                       intent.HasExpandRecommendationSignal
                   );
        }
        private static string BuildHumanSupportReply(string message)
        {
            var text = NormalizeText(message);

            if (text.Contains("bao hanh") ||
                text.Contains("loi xe") ||
                text.Contains("xe bi loi") ||
                text.Contains("hong xe"))
            {
                return "Rất tiếc vì xe của bạn đang gặp vấn đề. Với trường hợp bảo hành hoặc lỗi xe, nhân viên cần kiểm tra tình trạng xe, thời gian mua và chính sách áp dụng. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ trực tiếp nhé.";
            }

            if (text.Contains("doi tra") ||
                text.Contains("doi xe") ||
                text.Contains("muon doi xe"))
            {
                return "Với yêu cầu đổi xe hoặc đổi trả, nhân viên cần kiểm tra thông tin đơn hàng và chính sách áp dụng. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ chính xác hơn nhé.";
            }

            if (text.Contains("hoan tien"))
            {
                return "Với yêu cầu hoàn tiền, nhân viên cần kiểm tra giao dịch và thông tin đơn hàng cụ thể. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ an toàn và chính xác hơn nhé.";
            }

            if (text.Contains("khieu nai"))
            {
                return "Mình đã ghi nhận bạn muốn khiếu nại. Trường hợp này cần nhân viên kiểm tra trực tiếp thông tin đơn hàng và nội dung khiếu nại. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ nhé.";
            }

            if (text.Contains("loi thanh toan") ||
                text.Contains("thanh toan loi") ||
                text.Contains("thanh toan bi loi") ||
                text.Contains("khong thanh toan duoc") ||
                text.Contains("loi chuyen khoan") ||
                text.Contains("chuyen khoan loi") ||
                text.Contains("chuyen khoan bi loi") ||
                text.Contains("khong chuyen khoan duoc"))
            {
                return "Với sự cố thanh toán hoặc chuyển khoản, nhân viên cần kiểm tra giao dịch cụ thể để hỗ trợ an toàn. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ trực tiếp nhé.";
            }

            if (text.Contains("gap nhan vien") ||
                text.Contains("muon gap nhan vien") ||
                text.Contains("nhan vien tu van") ||
                text.Contains("gap admin") ||
                text.Contains("admin") ||
                text.Contains("ho tro truc tiep"))
            {
                return "Bạn có thể bấm **Gặp nhân viên** để được nhân viên tư vấn hỗ trợ trực tiếp nhé.";
            }

            return "Vấn đề này có thể cần nhân viên kiểm tra trực tiếp. Bạn có thể bấm **Gặp nhân viên** để được hỗ trợ chi tiết hơn nhé.";
        }
        private static bool IsStrongStandaloneIntent(ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            if (intent.IsOrderLookup ||
                string.Equals(intent.IntentType, "order_lookup", StringComparison.OrdinalIgnoreCase))
                return true;

            if ((intent.IsDirectProductLookup ||
                 string.Equals(intent.IntentType, "product_lookup", StringComparison.OrdinalIgnoreCase)) &&
                intent.MentionedProducts != null &&
                intent.MentionedProducts.Count > 0)
                return true;

            if ((intent.IsDirectCompare ||
                 string.Equals(intent.IntentType, "compare", StringComparison.OrdinalIgnoreCase)) &&
                intent.MentionedProducts != null &&
                intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() >= 2)
                return true;

            return false;
        }
        private static string BuildPolicyRagQuery(ChatOrchestrationContext context)
        {
            var message = NormalizeText(context.NormalizedMessage);

            if (!string.Equals(context.FinalRouting?.FlowType, ChatFlowType.RagPolicy, StringComparison.OrdinalIgnoreCase))
                return context.NormalizedMessage;

            bool asksProcedure =
                message.Contains("thu tuc") ||
                message.Contains("quy trinh") ||
                message.Contains("nhu nao") ||
                message.Contains("the nao");

            if (!asksProcedure)
                return context.NormalizedMessage;

            bool asksInstallment =
                message.Contains("tra gop") ||
                message.Contains("vay") ||
                message.Contains("lai suat");

            bool asksPaperwork =
                message.Contains("mua xe") ||
                message.Contains("giay to") ||
                message.Contains("ho so") ||
                message.Contains("dang ky xe") ||
                message.Contains("bien so");

            if (asksInstallment)
                return $"quy trình thủ tục mua xe trả góp hồ sơ vay giấy đề nghị vay vốn CMND CCCD {context.NormalizedMessage}";

            if (asksPaperwork)
                return $"quy trình thủ tục mua xe mới giấy tờ CMND CCCD thông tin đăng ký xe tờ khai đăng ký {context.NormalizedMessage}";

            var lastQuestionType = NormalizeText(context.State?.LastQuestionType);
            var lastIntentType = NormalizeText(context.State?.LastIntentType);
            var turnSummary = NormalizeText(context.State?.TurnSummary);

            var oldContext = $"{lastQuestionType} {lastIntentType} {turnSummary} {NormalizeText(context.State?.LastResolvedReference)}";

            if (oldContext.Contains("tra gop"))
                return $"quy trình thủ tục mua xe trả góp hồ sơ vay giấy đề nghị vay vốn CMND CCCD {context.NormalizedMessage}";
            if (oldContext.Contains("giay to") ||
                oldContext.Contains("ho so") ||
                oldContext.Contains("paperwork") ||
                oldContext.Contains("mua xe"))
                return $"quy trình thủ tục mua xe mới giấy tờ CMND CCCD thông tin đăng ký xe tờ khai đăng ký {context.NormalizedMessage}";

            return $"quy trình thủ tục mua xe mới giấy tờ CMND CCCD thông tin đăng ký xe {context.NormalizedMessage}";
        }
        private async Task<string?> TryGetRagContextAsync(ChatOrchestrationContext context)
        {
            if (context.FinalRouting?.ShouldUseRag != true)
                return null;

            var flowType = context.FinalRouting?.FlowType;

            bool allowed =
                string.Equals(flowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(flowType, ChatFlowType.RagPolicy, StringComparison.OrdinalIgnoreCase);

            if (!allowed)
                return null;

            try
            {
                var ragQuery = BuildPolicyRagQuery(context);

                var ragResponse = await _ragService.QueryAsync(ragQuery);

                if (ragResponse == null ||
                    !ragResponse.Success ||
                    string.IsNullOrWhiteSpace(ragResponse.Context))
                {
                    return null;
                }

                var rawContext = ragResponse.Context.Trim();

                if (string.Equals(flowType, ChatFlowType.RagPolicy, StringComparison.OrdinalIgnoreCase))
                {
                    var filteredContext = RagContextPostProcessor.Filter(
                        rawContext,
                        context.NormalizedMessage,
                        ragQuery,
                        context.EffectiveIntent,
                        context.ExistingProfile,
                        maxChunks: 4);

                    if (!string.IsNullOrWhiteSpace(filteredContext))
                        return filteredContext;
                }

                return rawContext;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "RAG query failed. Continue without RAG. ConversationId={ConversationId}, Message={Message}",
                    context.ConversationId,
                    context.NormalizedMessage);

                return null;
            }
        }
        private static bool IsDeterministicIntentConfident(ParsedIntent intent)
        {
            if (intent == null)
                return false;
            if (string.Equals(intent.FollowUpType, "cheapest_in_list", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (intent.IsGreeting ||
                intent.IsOutOfScope ||
                intent.IsNoise ||
                intent.IsAck ||
                intent.IsOrderLookup)
            {
                return true;
            }

            if (intent.IsDirectProductLookup &&
                intent.MentionedProducts != null &&
                intent.MentionedProducts.Count > 0)
            {
                return true;
            }

            if (intent.IsDirectCompare)
            {
                return true;
            }

            if (intent.IsProductSearch)
            {
                return true;
            }

            if ((intent.ExcludedProducts != null && intent.ExcludedProducts.Count > 0) ||
                (intent.ExcludedBrands != null && intent.ExcludedBrands.Count > 0) ||
                (intent.ExcludedCategories != null && intent.ExcludedCategories.Count > 0))
            {
                return true;
            }

            if (intent.HasFreshConsultationSignal &&
                (
                    intent.TargetPrice.HasValue ||
                    intent.PriceMin.HasValue ||
                    intent.PriceMax.HasValue ||
                    !string.IsNullOrWhiteSpace(intent.Brand) ||
                    !string.IsNullOrWhiteSpace(intent.Category) ||
                    !string.IsNullOrWhiteSpace(intent.Target) ||
                    intent.ForWork ||
                    intent.ForSchool ||
                    intent.ForCity ||
                    intent.ForTour ||
                    intent.PrefersMaleStyle ||
                    intent.PrefersFemaleStyle ||
                    intent.WantsFuelSaving ||
                    intent.WantsLargeStorage ||
                    intent.WantsEasyControl ||
                    intent.NeedsLowSeat
                ))
            {
                return true;
            }
            if (intent.Action == ConversationAction.ChangeProduct ||
    string.Equals(intent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase) ||
    intent.ExcludePreviousProducts)
            {
                return true;
            }
            return false;
        }
        private static bool LooksLikePurchaseDocumentQuestion(string message)
        {
            var text = NormalizeText(message);

            bool asksDocument =
                text.Contains("giay to") ||
                text.Contains("ho so") ||
                text.Contains("can gi") ||
                text.Contains("thu tuc");

            bool purchaseContext =
                text.Contains("mua xe") ||
                text.Contains("khi mua") ||
                text.Contains("dang ky xe") ||
                text.Contains("lay xe");

            return asksDocument && purchaseContext;
        }
        private static bool HasOrdinalReferenceText(string message)
        {
            var text = NormalizeText(message);

            return Regex.IsMatch(text, @"\b(xe|mau|con)\s*(thu\s*)?(1|2|3|4|5)\b", RegexOptions.IgnoreCase)
                || text.Contains("xe dau tien")
                || text.Contains("mau dau tien")
                || text.Contains("con dau tien")
                || text.Contains("xe thu nhat")
                || text.Contains("mau thu nhat")
                || text.Contains("con thu nhat")
                || text.Contains("xe thu hai")
                || text.Contains("mau thu hai")
                || text.Contains("con thu hai")
                || text.Contains("xe thu ba")
                || text.Contains("mau thu ba")
                || text.Contains("con thu ba")
                || text.Contains("xe thu tu")
                || text.Contains("mau thu tu")
                || text.Contains("con thu tu")
                || text.Contains("xe thu nam")
                || text.Contains("mau thu nam")
                || text.Contains("con thu nam");
        }
        private static bool ShouldUseRagForRecommendation(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("tra gop") ||
                   text.Contains("bao hanh") ||
                   text.Contains("bao duong") ||
                   text.Contains("thu tuc") ||
                   text.Contains("giay to") ||
                   text.Contains("ho so") ||
                   text.Contains("chinh sach") ||
                   text.Contains("doi tra") ||
                   text.Contains("giao hang") ||
                   text.Contains("dat coc") ||
                   text.Contains("thanh toan");
        }
        private static bool IsPolicyIntent(ParsedIntent? intent)
        {
            if (intent == null)
                return false;

            return string.Equals(intent.IntentType, "rag_policy", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.IntentType, "policy", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.IntentType, "faq", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.RouteFlow, ChatFlowType.RagPolicy, StringComparison.OrdinalIgnoreCase);
        }
        private static bool LooksLikeBudgetExpansionFollowUp(
     string message,
     CustomerPreferenceProfile? profile)
        {
            bool hasRecommendationListContext =
                profile?.HasActiveRecommendationContext == true ||
                profile?.CurrentRecommendedProducts?.Count > 0 ||
                profile?.LastRecommendedProducts?.Count > 0 ||
                profile?.BaseRecommendedProducts?.Count > 0;

            return hasRecommendationListContext && LooksLikeBudgetExpansionText(message);
        }
        private static bool LooksLikeOrdinalOnlyCompare(
     string message,
     CustomerPreferenceProfile? profile)
        {
            if (profile == null)
                return false;

            bool hasListContext =
                profile.CurrentRecommendedProducts?.Count >= 2 ||
                profile.LastRecommendedProducts?.Count >= 2 ||
                profile.BaseRecommendedProducts?.Count >= 2;

            if (!hasListContext)
                return false;

            var text = NormalizeText(message);

            bool hasCompareSignal =
                text.Contains("so sanh") ||
                text.Contains("so voi") ||
                text.Contains(" voi ") ||
                text.Contains(" va ");

            if (!hasCompareSignal)
                return false;

            var matches = Regex.Matches(
                text,
                @"\b(?:xe|mau|con)?\s*(?:thu\s*)?(?:1|2|3|4|5|nhat|hai|ba|tu|nam)\b",
                RegexOptions.IgnoreCase);

            var ordinalCount = matches
                .Cast<Match>()
                .Select(m => m.Value.Trim())
                .Where(x =>
                    x.Contains("1") || x.Contains("2") || x.Contains("3") || x.Contains("4") || x.Contains("5") ||
                    x.Contains("nhat") || x.Contains("hai") || x.Contains("ba") || x.Contains("tu") || x.Contains("nam"))
                .Count();

            bool hasFirstSpecial =
                text.Contains("xe dau tien") ||
                text.Contains("mau dau tien") ||
                text.Contains("con dau tien");

            if (hasFirstSpecial)
                ordinalCount++;

            return ordinalCount >= 2;
        }
        private static bool LooksLikeBudgetExpansionText(string message)
        {
            var text = NormalizeText(message);

            bool hasExpandSignal =
                text.Contains("cao hon") ||
                text.Contains("dat hon") ||
                text.Contains("them chut") ||
                text.Contains("them mot chut") ||
                text.Contains("hon mot chut") ||
                text.Contains("noi ngan sach") ||
                text.Contains("noi them") ||
                text.Contains("tang ngan sach") ||
                text.Contains("len chut") ||
                text.Contains("len mot chut");

            bool hasBudgetSignal =
                text.Contains("gia") ||
                text.Contains("ngan sach") ||
                text.Contains("tien") ||
                text.Contains("trieu") ||
                text.Contains("cung duoc") ||
                text.Contains("cung dc");

            return hasExpandSignal && hasBudgetSignal;
        }

        private static bool LooksLikeBudgetExpansionAmbiguousAfterCompare(
    string message,
    CustomerPreferenceProfile? profile)
        {
            var text = NormalizeText(message);

            bool userAlreadyChoseRecommendationPath =
    text.Contains("tu van") ||
    text.Contains("goi y") ||
    text.Contains("them mau") ||
    text.Contains("xe khac") ||
    text.Contains("mau khac") ||
    text.Contains("danh sach vua tu van") ||
    text.Contains("mau xe khac");

            if (userAlreadyChoseRecommendationPath)
                return false;

            return profile?.HasActiveCompareContext == true &&
                   profile.LastComparedProducts != null &&
                   profile.LastComparedProducts.Count >= 2 &&
                   LooksLikeBudgetExpansionText(message);
        }
        private static bool LooksLikeCompareFeatureFollowUp(
    string message,
    CustomerPreferenceProfile? profile)
        {
            if (profile?.HasActiveCompareContext != true ||
                profile.LastComparedProducts == null ||
                profile.LastComparedProducts.Count < 2)
            {
                return false;
            }

            var text = NormalizeText(message);

            return text == "ve gia" ||
                   text == "gia" ||
                   text.Contains("ve gia") ||
                   text.Contains("gia ca") ||
                   text.Contains("re hon") ||
                   text.Contains("dat hon") ||
                   text.Contains("do ben") ||
                   text.Contains("ben hon") ||
                   text.Contains("de di") ||
                   text.Contains("kieu dang") ||
                   text.Contains("di pho") ||
                   text.Contains("tiet kiem xang") ||
                   text.Contains("cop");
        }
        private static string? DetectBrandFromText(string message)
        {
            var text = NormalizeText(message);

            if (text.Contains("honda") ||
                text.Contains("hoda") ||
                text.Contains("honad") ||
                text.Contains("hond"))
                return "Honda";

            if (text.Contains("yamaha") ||
                text.Contains("yamha") ||
                text.Contains("yamah") ||
                text.Contains("yamaa"))
                return "Yamaha";

            if (text.Contains("suzuki") ||
                text.Contains("suzki") ||
                text.Contains("szuki"))
                return "Suzuki";

            if (text.Contains("sym"))
                return "SYM";

            if (text.Contains("piaggio") ||
                text.Contains("piago") ||
                text.Contains("piagio"))
                return "Piaggio";

            return null;
        }

        private static string? DetectCategoryFromText(string message)
        {
            var text = NormalizeText(message);

            if (text.Contains("xe ga") || text.Contains("tay ga")) return "xe ga";
            if (text.Contains("xe so")) return "xe số";
            if (text.Contains("con tay")) return "côn tay";

            return null;
        }
        private static bool MessageAsksGlobalScope(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("cua shop") ||
                   text.Contains("toan shop") ||
                   text.Contains("toan bo shop") ||
                   text.Contains("tat ca shop") ||
                   text.Contains("cua hang") ||
                   text.Contains("toan cua hang") ||
                   text.Contains("tat ca xe") ||
                   text.Contains("toan bo xe");
        }
        private static string? DetectSimpleCompareFeature(string message)
        {
            var text = NormalizeText(message);

            if (text.Contains("gia") || text.Contains("re hon") || text.Contains("dat hon"))
                return "price";

            if (text.Contains("do ben") || text.Contains("ben hon"))
                return "durability";

            if (text.Contains("de di") || text.Contains("de dieu khien"))
                return "low_seat";

            if (text.Contains("kieu dang") || text.Contains("dep"))
                return "design_fit";

            if (text.Contains("di pho"))
                return "city_fit";

            if (text.Contains("tiet kiem xang"))
                return "fuel_saving";

            if (text.Contains("cop"))
                return "storage";

            return null;
        }
        private static bool LooksLikeInstallmentPolicyQuestion(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("tra gop") &&
                   (
                       text.Contains("duoc khong") ||
                       text.Contains("co duoc") ||
                       text.Contains("sinh vien") ||
                       text.Contains("hoc sinh") ||
                       text.Contains("can gi") ||
                       text.Contains("thu tuc") ||
                       text.Contains("giay to")
                   );
        }
        private static bool LooksLikeGlobalProductStatisticQuery(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("re nhat") ||
                   text.Contains("dat nhat") ||
                   text.Contains("gia cao nhat") ||
                   text.Contains("gia thap nhat") ||
                   text.Contains("bao nhieu xe") ||
                   text.Contains("co bao nhieu xe") ||
                   text.Contains("tong so xe") ||
                   text.Contains("ban chay nhat") ||
                   text.Contains("nhieu nguoi mua") ||
                   text.Contains("duoc mua nhieu") ||
                   text.Contains("phu hop sinh vien nhat") 
                   || text.Contains("phu hop voi sinh vien")
|| text.Contains("hop voi sinh vien")
|| text.Contains("sinh vien nen mua")
|| text.Contains("xe sinh vien") ||
                   text.Contains("cho sinh vien nhat");
        }
        private static bool LooksLikeAmbiguousGenericDislike(string message)
        {
            var text = NormalizeText(message);

            return text == "khong thich" ||
                   text == "khong ung" ||
                   text == "khong thich lam" ||
                   text == "khong hop" ||
                   text == "khong muon";
        }
        private static bool LooksLikeUnknownBrand(string message, ParsedIntent? intent)
        {
            var text = NormalizeText(message);
            if (intent?.IsFollowUp == true ||
    !string.IsNullOrWhiteSpace(intent?.FollowUpType) ||
    string.Equals(intent?.RouteFlow, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            bool hasRecommend =
                text.Contains("tu van") ||
                text.Contains("goi y") ||
                text.Contains("nen mua") ||
                text.Contains("chon xe");

            if (!hasRecommend)
                return false;

            if (!text.Contains("xe"))
                return false;

            if (!string.IsNullOrWhiteSpace(intent?.Brand))
                return false;

            bool hasOtherSignal =
                intent?.TargetPrice.HasValue == true ||
                intent?.PriceMin.HasValue == true ||
                intent?.PriceMax.HasValue == true ||
                !string.IsNullOrWhiteSpace(intent?.Category) ||
                !string.IsNullOrWhiteSpace(intent?.Target);

            if (hasOtherSignal)
                return false;

            var ignoreWords = new HashSet<string>
    {
        "tu","van","goi","y","nen","mua","chon","xe"
    };

            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Where(x => x.Length >= 3)
                             .Where(x => !ignoreWords.Contains(x))
                             .ToList();

            return tokens.Count > 0;
        }
        private static void RecoverMultiCriteriaRecommendation(string message, ParsedIntent intent)
        {
            if (intent == null || string.IsNullOrWhiteSpace(message))
                return;

            var text = NormalizeText(message);

            bool hasVehicleSignal =
                text.Contains("xe ga") ||
                text.Contains("xe so") ||
                text.Contains("xe côn") ||
                text.Contains("xe con");

            bool hasBrandSignal =
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio");

            bool hasTargetSignal =
                text.Contains("cho nu") ||
                text.Contains("cho nữ") ||
                text.Contains("cho nam") ||
                text.Contains("sinh vien") ||
                text.Contains("hoc sinh");

            bool hasPriceSignal =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                text.Contains("trieu") ||
                text.Contains("triệu") ||
                text.Contains("tr ") ||
                text.Contains("cu") ||
                text.Contains("củ");

            if (!(hasVehicleSignal && (hasBrandSignal || hasTargetSignal || hasPriceSignal)))
                return;

            intent.IntentType = "recommend";
            intent.RouteFlow = ChatFlowType.Recommendation;
            intent.IsOpenRecommendation = true;
            intent.IsFollowUp = false;
            intent.HasFreshConsultationSignal = true;
            intent.IsNoise = false;
            intent.IsOutOfScope = false;

            if (string.IsNullOrWhiteSpace(intent.Category))
            {
                if (text.Contains("xe ga"))
                    intent.Category = "xe ga";
                else if (text.Contains("xe so"))
                    intent.Category = "xe số";
                else if (text.Contains("xe con") || text.Contains("xe côn"))
                    intent.Category = "côn tay";
            }

            if (string.IsNullOrWhiteSpace(intent.Brand))
            {
                if (text.Contains("honda")) intent.Brand = "Honda";
                else if (text.Contains("yamaha")) intent.Brand = "Yamaha";
                else if (text.Contains("suzuki")) intent.Brand = "Suzuki";
                else if (text.Contains("sym")) intent.Brand = "SYM";
                else if (text.Contains("piaggio")) intent.Brand = "Piaggio";
            }

            if (text.Contains("cho nu") || text.Contains("cho nữ"))
            {
                intent.Target = "nữ";
                intent.PrefersFemaleStyle = true;
                intent.WantsEasyControl = true;
            }
        }
        private static bool ShouldAskScopeBeforeRetryAfterExclusion(ChatOrchestrationContext context)
        {
            if (context == null)
                return false;

            var text = NormalizeText(context.NormalizedMessage);

            bool isRetry =
                text.Contains("tu van lai") ||
                text.Contains("goi y lai") ||
                text.Contains("chon lai") ||
                text.Contains("loc lai") ||
                text.Contains("tim lai");

            if (!isRetry)
                return false;

            if (text.Contains("tu dau") ||
                text.Contains("reset") ||
                text.Contains("bo tieu chi cu") ||
                text.Contains("bo dieu kien cu"))
            {
                return false;
            }

            var profile = context.ExistingProfile;

            bool hasExclusion =
                context.EffectiveIntent?.ExcludedBrands?.Any() == true ||
                context.EffectiveIntent?.ExcludedProducts?.Any() == true ||
                context.EffectiveIntent?.ExcludedCategories?.Any() == true ||
                profile?.ExcludedBrands?.Any() == true ||
                profile?.ExcludedProducts?.Any() == true ||
                profile?.ExcludedCategories?.Any() == true;

            if (!hasExclusion)
                return false;

            bool hasCurrentList =
                profile?.CurrentRecommendedProducts?.Count >= 2 ||
                profile?.LastRecommendedProducts?.Count >= 2 ||
                profile?.BaseRecommendedProducts?.Count >= 2;

            return hasCurrentList;
        }
    }
}