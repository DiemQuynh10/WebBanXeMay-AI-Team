using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Conversation;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;

namespace Chatbot.API.Services
{
    public class ChatServiceLegacy : IChatService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly ILogger<ChatServiceLegacy> _logger;
        private readonly IQueryNormalizationService _queryNormalizationService;
        private readonly IClarificationStateService _clarificationStateService;
        private readonly IRagService _ragService;
        private readonly IPriceIntentParser _priceIntentParser;
        private readonly IIntentParserService _intentParserService;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ICompareService _compareService;
        private readonly IRecommendationFollowUpService _recommendationFollowUpService;
        private readonly IRefinementService _refinementService;
        private readonly IChatFlowRouter _chatFlowRouter;
        private readonly IProductLookupFlowService _productLookupFlowService;
        private readonly IProductSearchFlowService _productSearchFlowService;
        private readonly ILLMIntentUnderstandingService _llmIntentUnderstandingService;
        private readonly IConversationContextResolver _conversationContextResolver;
        private readonly IFlowDecisionService _flowDecisionService;
        private readonly IRecommendationClarificationService _recommendationClarificationService;
        public ChatServiceLegacy(
     IOpenAIService openAIService,
     IWebBanXeMayToolClient toolClient,
     ILogger<ChatServiceLegacy> logger,
     IQueryNormalizationService queryNormalizationService,
     IClarificationStateService clarificationStateService,
     IRagService ragService,
     IPriceIntentParser priceIntentParser,
     IIntentParserService intentParserService,
     IProductRecommendationService productRecommendationService,
     IConversationPreferenceService conversationPreferenceService,
     ICompareService compareService,
     IRecommendationFollowUpService recommendationFollowUpService,
     IRefinementService refinementService,
     IChatFlowRouter chatFlowRouter,
IProductLookupFlowService productLookupFlowService,
ILLMIntentUnderstandingService llmIntentUnderstandingService,
IConversationContextResolver conversationContextResolver,
IFlowDecisionService flowDecisionService,
IRecommendationClarificationService recommendationClarificationService,
IProductSearchFlowService productSearchFlowService)
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
            _compareService = compareService;
            _recommendationFollowUpService = recommendationFollowUpService;
            _refinementService = refinementService;
            _chatFlowRouter = chatFlowRouter;
            _productLookupFlowService = productLookupFlowService;
            _llmIntentUnderstandingService = llmIntentUnderstandingService;
            _productSearchFlowService = productSearchFlowService;
            _conversationContextResolver = conversationContextResolver;
            _recommendationClarificationService = recommendationClarificationService;
            _flowDecisionService = flowDecisionService;
        }
        private static readonly Random _rand = new Random();

        private static string Pick(params string[] options)
        {
            return options[_rand.Next(options.Length)];
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

                var existingProfileBeforeConfirmation = await _conversationPreferenceService.GetAsync(conversationId);

                bool skipNormalizationConfirmationForBudgetPivot =
                    normalizationResult.NeedsConfirmation &&
                    existingProfileBeforeConfirmation != null &&
                    existingProfileBeforeConfirmation.HasActiveRecommendationContext &&
                    RecommendationConversationRules.LooksLikeBudgetPivotFollowUp(originalMessage);

                if (normalizationResult.NeedsConfirmation && !skipNormalizationConfirmationForBudgetPivot)
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

                if (skipNormalizationConfirmationForBudgetPivot)
                {
                    _logger.LogInformation(
                        "Skip normalization confirmation because current turn looks like budget pivot follow-up. ConversationId: {ConversationId}, Original: {Original}, Normalized: {Normalized}",
                        conversationId,
                        normalizationResult.OriginalText,
                        normalizationResult.NormalizedText);
                }
                var normalizedMessage = normalizationResult.NormalizedText;
                var effectivePrompt = normalizedMessage;

                var priceRange = _priceIntentParser.Parse(normalizedMessage);

                _logger.LogInformation(
    "Price parsed. Message: {Message}, FilterType: {FilterType}, TargetPrice: {TargetPrice}, Min: {Min}, Max: {Max}",
    normalizedMessage,
    priceRange.FilterType,
    priceRange.TargetPrice,
    priceRange.MinPrice,
    priceRange.MaxPrice);

                var existingProfile = await _conversationPreferenceService.GetAsync(conversationId);

                var parsedIntent = await _intentParserService.ParseAsync(normalizedMessage);

                parsedIntent.PriceMin = priceRange.MinPrice ?? parsedIntent.PriceMin;
                parsedIntent.PriceMax = priceRange.MaxPrice ?? parsedIntent.PriceMax;
                parsedIntent.FilterType = priceRange.FilterType;
                parsedIntent.TargetPrice = priceRange.TargetPrice;

                _logger.LogInformation(
    "Pre-LLM parsed intent. IntentType: {IntentType}, Target: {Target}, Brand: {Brand}, Category: {Category}, " +
    "ForWork: {ForWork}, ForSchool: {ForSchool}, ForCity: {ForCity}, ForTour: {ForTour}, " +
    "WantsFuelSaving: {WantsFuelSaving}, WantsLargeStorage: {WantsLargeStorage}, WantsEasyControl: {WantsEasyControl}, NeedsLowSeat: {NeedsLowSeat}, " +
    "PriceMin: {PriceMin}, PriceMax: {PriceMax}, TargetPrice: {TargetPrice}, FilterType: {FilterType}, " +
    "ExcludedBrandsCount: {ExcludedBrandsCount}, ExcludedCategoriesCount: {ExcludedCategoriesCount}, RequestedStylesCount: {RequestedStylesCount}",
    parsedIntent.IntentType,
    parsedIntent.Target,
    parsedIntent.Brand,
    parsedIntent.Category,
    parsedIntent.ForWork,
    parsedIntent.ForSchool,
    parsedIntent.ForCity,
    parsedIntent.ForTour,
    parsedIntent.WantsFuelSaving,
    parsedIntent.WantsLargeStorage,
    parsedIntent.WantsEasyControl,
    parsedIntent.NeedsLowSeat,
    parsedIntent.PriceMin,
    parsedIntent.PriceMax,
    parsedIntent.TargetPrice,
    parsedIntent.FilterType,
    parsedIntent.ExcludedBrands.Count,
    parsedIntent.ExcludedCategories.Count,
    parsedIntent.RequestedStyles.Count);
                bool preserveBudgetOnlyForLlmMerge =
    parsedIntent.TargetPrice.HasValue ||
    parsedIntent.PriceMin.HasValue ||
    parsedIntent.PriceMax.HasValue ||
    parsedIntent.FilterType != PriceFilterType.None;
                var llmIntent = await _llmIntentUnderstandingService.UnderstandAsync(
    normalizedMessage,
    existingProfile);

                if (llmIntent != null)
                {
                    _logger.LogInformation(
                        "LLM intent understanding. IntentType: {IntentType}, IsFollowUp: {IsFollowUp}, ResetContext: {ResetContext}, FollowUpType: {FollowUpType}, Reason: {Reason}",
                        llmIntent.IntentType,
                        llmIntent.IsFollowUp,
                        llmIntent.ResetContext,
                        llmIntent.FollowUpType,
                        llmIntent.Reason);
                    bool looksGeneralBudgetConsultation =
        (
            parsedIntent.PriceMin.HasValue ||
            parsedIntent.PriceMax.HasValue ||
            parsedIntent.TargetPrice.HasValue ||
            parsedIntent.FilterType != PriceFilterType.None
        ) &&
        (
            normalizedMessage.Contains("tư vấn") ||
            normalizedMessage.Contains("tu van") ||
            normalizedMessage.Contains("xe") ||
            normalizedMessage.Contains("khoảng") ||
            normalizedMessage.Contains("khoang") ||
            normalizedMessage.Contains("tầm") ||
            normalizedMessage.Contains("tam") ||
            normalizedMessage.Contains("quanh")
        );

                    bool hasEnoughConsultationSignalsForDirectAnswer =
                        looksGeneralBudgetConsultation &&
                        (
                            !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                            parsedIntent.ForWork ||
                            parsedIntent.ForSchool ||
                            parsedIntent.ForCity ||
                            parsedIntent.ForTour ||
                            !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                            !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                            parsedIntent.WantsFuelSaving ||
                            parsedIntent.WantsLargeStorage ||
                            parsedIntent.WantsEasyControl ||
                            parsedIntent.NeedsLowSeat ||
                            normalizedMessage.Contains("tư vấn") ||
                            normalizedMessage.Contains("tu van") ||
                            normalizedMessage.Contains("xe tầm") ||
                            normalizedMessage.Contains("xe tam") ||
                            normalizedMessage.Contains("xe khoảng") ||
                            normalizedMessage.Contains("xe khoang") ||
                            normalizedMessage.Contains("gợi ý") ||
                            normalizedMessage.Contains("goi y")
                        );

                    bool shouldHonorLlmClarification =
                        llmIntent.ShouldAskClarification &&
                        !string.IsNullOrWhiteSpace(llmIntent.ClarificationQuestion) &&
                        !hasEnoughConsultationSignalsForDirectAnswer;

                    if (shouldHonorLlmClarification)
                    {
                        stopwatch.Stop();
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = llmIntent.ClarificationQuestion,
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }

                    if (llmIntent.ShouldAskClarification &&
                        !string.IsNullOrWhiteSpace(llmIntent.ClarificationQuestion) &&
                        hasEnoughConsultationSignalsForDirectAnswer)
                    {
                        _logger.LogInformation(
                            "Skip LLM clarification because current consultation already has enough signals. ConversationId: {ConversationId}, Message: {Message}",
                            conversationId,
                            normalizedMessage);
                    }
                    if (!string.IsNullOrWhiteSpace(llmIntent.IntentType) &&
    llmIntent.IntentType != "unknown" &&
    !string.Equals(parsedIntent.IntentType, llmIntent.IntentType, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "Intent conflict detected. RuleIntent: {RuleIntent}, LlmIntent: {LlmIntent}, Confidence: {Confidence}, Message: {Message}",
                            parsedIntent.IntentType,
                            llmIntent.IntentType,
                            llmIntent.Confidence,
                            normalizedMessage);
                    }

                    if (!string.IsNullOrWhiteSpace(llmIntent.IntentType) &&
                        llmIntent.IntentType != "unknown" &&
                        llmIntent.Confidence >= 0.85)
                    {
                        parsedIntent.IntentType = llmIntent.IntentType;
                    }

                    if (llmIntent.IsFollowUp)
                    {
                        parsedIntent.IsFollowUp = true;
                    }

                    if (!string.IsNullOrWhiteSpace(llmIntent.FollowUpType) &&
                        llmIntent.FollowUpType != "none")
                    {
                        parsedIntent.FollowUpType = llmIntent.FollowUpType;
                    }
                    // Merge semantic constraints từ LLM khi đủ tin cậy
                    if (llmIntent.Confidence >= 0.75)
                    {
                        if (string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                            !string.IsNullOrWhiteSpace(llmIntent.Brand))
                        {
                            parsedIntent.Brand = llmIntent.Brand;
                        }

                        if (string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                            !string.IsNullOrWhiteSpace(llmIntent.Category))
                        {
                            parsedIntent.Category = llmIntent.Category;
                        }

                        if (!preserveBudgetOnlyForLlmMerge &&
    string.IsNullOrWhiteSpace(parsedIntent.Target) &&
    !string.IsNullOrWhiteSpace(llmIntent.Target))
                        {
                            parsedIntent.Target = llmIntent.Target;
                        }
                        if (!parsedIntent.PriceMin.HasValue && llmIntent.PriceMin.HasValue)
                        {
                            parsedIntent.PriceMin = llmIntent.PriceMin;
                        }

                        if (!parsedIntent.PriceMax.HasValue && llmIntent.PriceMax.HasValue)
                        {
                            parsedIntent.PriceMax = llmIntent.PriceMax;
                        }

                        if (!parsedIntent.TargetPrice.HasValue && llmIntent.TargetPrice.HasValue)
                        {
                            parsedIntent.TargetPrice = llmIntent.TargetPrice;
                        }

                        if (parsedIntent.ForWork || MessageExplicitlyMentionsUseCase(normalizedMessage, "work"))
                        {
                            parsedIntent.ForWork = parsedIntent.ForWork || llmIntent.ForWork;
                        }

                        if (parsedIntent.ForSchool || MessageExplicitlyMentionsUseCase(normalizedMessage, "school"))
                        {
                            parsedIntent.ForSchool = parsedIntent.ForSchool || llmIntent.ForSchool;
                        }

                        if (parsedIntent.ForCity || MessageExplicitlyMentionsUseCase(normalizedMessage, "city"))
                        {
                            parsedIntent.ForCity = parsedIntent.ForCity || llmIntent.ForCity;
                        }

                        if (parsedIntent.ForTour || MessageExplicitlyMentionsUseCase(normalizedMessage, "tour"))
                        {
                            parsedIntent.ForTour = parsedIntent.ForTour || llmIntent.ForTour;
                        }

                        parsedIntent.WantsFuelSaving = parsedIntent.WantsFuelSaving || llmIntent.WantsFuelSaving;
                        parsedIntent.WantsLargeStorage = parsedIntent.WantsLargeStorage || llmIntent.WantsLargeStorage;
                        parsedIntent.WantsEasyControl = parsedIntent.WantsEasyControl || llmIntent.WantsEasyControl;
                        parsedIntent.NeedsLowSeat = parsedIntent.NeedsLowSeat || llmIntent.NeedsLowSeat;

                        if (!parsedIntent.HeightCm.HasValue && llmIntent.HeightCm.HasValue)
                        {
                            parsedIntent.HeightCm = llmIntent.HeightCm;
                        }

                        if (!string.IsNullOrWhiteSpace(llmIntent.ComparisonFeature) &&
                            string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature))
                        {
                            parsedIntent.ComparisonFeature = llmIntent.ComparisonFeature;
                        }

                        foreach (var product in llmIntent.MentionedProducts.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            parsedIntent.MentionedProducts.Add(product);
                        }

                        foreach (var brand in llmIntent.ExcludedBrands.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            parsedIntent.ExcludedBrands.Add(brand);
                        }

                        foreach (var category in llmIntent.ExcludedCategories.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            parsedIntent.ExcludedCategories.Add(category);
                        }

                        foreach (var style in llmIntent.RequestedStyles.Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            parsedIntent.RequestedStyles.Add(style);
                        }
                    }
                    _logger.LogInformation(
    "LLM semantic merge applied. Brand: {Brand}, Category: {Category}, Target: {Target}, PriceMin: {PriceMin}, PriceMax: {PriceMax}, TargetPrice: {TargetPrice}, ForWork: {ForWork}, ForSchool: {ForSchool}, WantsLargeStorage: {WantsLargeStorage}, NeedsLowSeat: {NeedsLowSeat}, ComparisonFeature: {ComparisonFeature}",
    parsedIntent.Brand,
    parsedIntent.Category,
    parsedIntent.Target,
    parsedIntent.PriceMin,
    parsedIntent.PriceMax,
    parsedIntent.TargetPrice,
    parsedIntent.ForWork,
    parsedIntent.ForSchool,
    parsedIntent.WantsLargeStorage,
    parsedIntent.NeedsLowSeat,
    parsedIntent.ComparisonFeature);
                    if (llmIntent.ResetContext && !preserveBudgetOnlyForLlmMerge)
                    {
                        await _conversationPreferenceService.ResetForFreshConsultationAsync(conversationId);
                        existingProfile = await _conversationPreferenceService.GetAsync(conversationId);

                        _logger.LogInformation(
                            "Conversation context reset by LLM intent understanding. ConversationId: {ConversationId}",
                            conversationId);
                    }
                    else if (llmIntent.ResetContext && preserveBudgetOnlyForLlmMerge)
                    {
                        _logger.LogInformation(
                            "Skip LLM-based context reset because current turn is only a price change. ConversationId: {ConversationId}",
                            conversationId);
                    }
                }

                switch (parsedIntent.IntentType)
                {
                    case "product_lookup":
                        parsedIntent.IsProductSearch = false;
                        parsedIntent.IsOpenRecommendation = false;
                        parsedIntent.RouteFlow = ChatFlowType.ProductLookup;
                        parsedIntent.HasDeterministicProductIntent = true;
                        break;

                    case "product_search":
                        parsedIntent.IsProductSearch = true;
                        parsedIntent.IsOpenRecommendation = false;
                        parsedIntent.RouteFlow = ChatFlowType.ProductSearch;
                        parsedIntent.HasDeterministicProductIntent = true;
                        break;

                    case "recommend":
                        parsedIntent.IsProductSearch = false;
                        parsedIntent.IsOpenRecommendation = true;
                        parsedIntent.RouteFlow = ChatFlowType.Recommendation;
                        parsedIntent.HasDeterministicProductIntent = false;
                        break;

                    case "refine":
                        parsedIntent.IsProductSearch = false;
                        parsedIntent.IsOpenRecommendation = false;
                        parsedIntent.RouteFlow = ChatFlowType.Refinement;
                        parsedIntent.HasDeterministicProductIntent = true;
                        break;

                    case "compare":
                        parsedIntent.IsDirectCompare = true;
                        parsedIntent.IsProductSearch = false;
                        parsedIntent.IsOpenRecommendation = false;
                        parsedIntent.RouteFlow = ChatFlowType.Compare;
                        parsedIntent.HasDeterministicProductIntent = true;
                        break;
                }
                bool hasConsultativeNeed =
    !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
    parsedIntent.ForSchool ||
    parsedIntent.ForWork ||
    parsedIntent.ForCity ||
    parsedIntent.ForTour ||
    parsedIntent.WantsEasyControl ||
    parsedIntent.WantsFuelSaving ||
    parsedIntent.WantsLargeStorage ||
    parsedIntent.NeedsLowSeat ||
    parsedIntent.RequestedStyles.Count > 0;

                bool hasPriceOrHardConstraint =
                    parsedIntent.PriceMin.HasValue ||
                    parsedIntent.PriceMax.HasValue ||
                    parsedIntent.TargetPrice.HasValue ||
                    parsedIntent.FilterType != PriceFilterType.None ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                    !string.IsNullOrWhiteSpace(parsedIntent.Category);
                bool looksGeneralBudgetConsultationForRouting =
    hasPriceOrHardConstraint &&
    (
        normalizedMessage.Contains("tư vấn") ||
        normalizedMessage.Contains("tu van") ||
        normalizedMessage.Contains("khoảng") ||
        normalizedMessage.Contains("khoang") ||
        normalizedMessage.Contains("tầm") ||
        normalizedMessage.Contains("tam") ||
        normalizedMessage.Contains("quanh") ||
        normalizedMessage.Contains("xe")
    );

                if ((parsedIntent.IntentType == "product_search" || parsedIntent.IntentType == "unknown") &&
     looksGeneralBudgetConsultationForRouting)
                {
                    MarkAsRecommendation(parsedIntent);

                    _logger.LogInformation(
                        "Marked as recommendation by general budget consultation. ConversationId: {ConversationId}, Message: {Message}",
                        conversationId,
                        normalizedMessage);
                }

                if ((parsedIntent.IntentType == "product_search" || parsedIntent.IntentType == "unknown") &&
    hasConsultativeNeed &&
    hasPriceOrHardConstraint)
                {
                    MarkAsRecommendation(parsedIntent);

                    _logger.LogInformation(
                        "Marked as recommendation by consultative need + constraints. ConversationId: {ConversationId}, Message: {Message}",
                        conversationId,
                        normalizedMessage);
                }
                _logger.LogInformation(
    "Intent after price merge. IntentType: {IntentType}, IsProductSearch: {IsProductSearch}, RouteFlow: {RouteFlow}, FilterType: {FilterType}, TargetPrice: {TargetPrice}, PriceMin: {PriceMin}, PriceMax: {PriceMax}",
    parsedIntent.IntentType,
    parsedIntent.IsProductSearch,
    parsedIntent.RouteFlow,
    parsedIntent.FilterType,
    parsedIntent.TargetPrice,
    parsedIntent.PriceMin,
    parsedIntent.PriceMax);
                if (LookupConversationRules.LooksLikeDirectProductLookup(normalizedMessage, parsedIntent))
                {
                    parsedIntent.IsDirectProductLookup = true;
                    parsedIntent.IntentType = "product_lookup";
                    parsedIntent.HasDeterministicProductIntent = true;

                    _logger.LogInformation(
                        "Marked as direct product lookup. ConversationId: {ConversationId}, Message: {Message}",
                        conversationId,
                        normalizedMessage);
                }
                if (CompareConversationRules.LooksLikeDirectCompareRequest(normalizedMessage, parsedIntent) &&
     parsedIntent.MentionedProducts.Count >= 2)
                {
                    parsedIntent.IsDirectCompare = true;
                    parsedIntent.IntentType = "compare";
                    parsedIntent.HasDeterministicProductIntent = true;

                    _logger.LogInformation(
                        "Marked as direct compare. ConversationId: {ConversationId}, Message: {Message}, MentionedProducts: {Products}",
                        conversationId,
                        normalizedMessage,
                        string.Join(" | ", parsedIntent.MentionedProducts));
                }
                bool hasPriceSignals =
                    parsedIntent.PriceMin.HasValue ||
                    parsedIntent.PriceMax.HasValue ||
                    parsedIntent.TargetPrice.HasValue ||
                    parsedIntent.FilterType != PriceFilterType.None;

                bool looksLikeFreshPriceRestart =
     normalizedMessage.StartsWith("h t muốn", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.StartsWith("vậy h t muốn", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.StartsWith("vay h t muon", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.StartsWith("ý là h t muốn", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.StartsWith("y la h t muon", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.StartsWith("đổi ý", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.StartsWith("doi y", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.Contains("chứ không phải", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.Contains("chu khong phai", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.Contains("không phải 45 triệu nữa", StringComparison.OrdinalIgnoreCase) ||
     normalizedMessage.Contains("khong phai 45 trieu nua", StringComparison.OrdinalIgnoreCase);

                if (parsedIntent.IntentType == "unknown" && hasPriceSignals)
                {
                    bool hasActiveRecommendationContext =
                        existingProfile != null &&
                        existingProfile.HasActiveRecommendationContext &&
                        existingProfile.LastRecommendedProducts != null &&
                        existingProfile.LastRecommendedProducts.Count > 0;

                    if (!looksLikeFreshPriceRestart && hasActiveRecommendationContext)
                    {
                        parsedIntent.IntentType = "refine";
                        parsedIntent.IsFollowUp = true;
                        parsedIntent.FollowUpType = "refine";
                        parsedIntent.RouteFlow = ChatFlowType.Refinement;
                        parsedIntent.HasDeterministicProductIntent = true;
                    }
                    else
                    {
                        // KHÔNG ép product_search quá sớm nếu chưa qua context resolver
                        parsedIntent.IntentType = "unknown";
                        parsedIntent.IsProductSearch = false;
                        parsedIntent.HasDeterministicProductIntent = false;
                    }
                }
                var previousActiveFlow = existingProfile.ActiveFlow;

                var contextResolution = _conversationContextResolver.Resolve(
                    normalizedMessage,
                    parsedIntent,
                    existingProfile,
                    previousActiveFlow);

                var contextDecision = contextResolution.ContextDecision;
                var effectiveIntent = contextResolution.EffectiveIntent;
                var shouldPreserveContextForBudgetOnly = contextResolution.ShouldPreserveBudgetOnlyContext;

                _logger.LogInformation(
                    "Recommendation context decision. ConversationId: {ConversationId}, Decision: {Decision}, Hint: {Hint}, IntentType: {IntentType}",
                    conversationId,
                    contextDecision,
                    parsedIntent.RecommendationContextActionHint,
                    parsedIntent.IntentType);

                if (contextResolution.ShouldResetContext)
                {
                    await _conversationPreferenceService.ResetForFreshConsultationAsync(conversationId);
                    existingProfile = await _conversationPreferenceService.GetAsync(conversationId);

                    _logger.LogInformation(
                        "Context reset after recommendation context decision. ConversationId: {ConversationId}, Decision: {Decision}",
                        conversationId,
                        contextDecision);
                }
                else if (shouldPreserveContextForBudgetOnly)
                {
                    _logger.LogInformation(
                        "Skip context-decision reset because current turn is only a price change. ConversationId: {ConversationId}, Decision: {Decision}",
                        conversationId,
                        contextDecision);
                }

                var conversationProfile = await _conversationPreferenceService.MergeAsync(conversationId, effectiveIntent);
                _logger.LogInformation(
    "ConversationProfile after merge. ConversationId: {ConversationId}, Target: {Target}, PrefersMaleStyle: {PrefersMaleStyle}, PrefersFemaleStyle: {PrefersFemaleStyle}, ForWork: {ForWork}, ForSchool: {ForSchool}, PreferredBrand: {PreferredBrand}, PreferredCategory: {PreferredCategory}, TargetPrice: {TargetPrice}, PriceMin: {PriceMin}, PriceMax: {PriceMax}",
    conversationId,
    conversationProfile.Target,
    conversationProfile.PrefersMaleStyle,
    conversationProfile.PrefersFemaleStyle,
    conversationProfile.ForWork,
    conversationProfile.ForSchool,
    conversationProfile.PreferredBrand,
    conversationProfile.PreferredCategory,
    conversationProfile.TargetPrice,
    conversationProfile.PriceMin,
    conversationProfile.PriceMax);

                var baseRouting = _chatFlowRouter.Route(
    normalizedMessage,
    effectiveIntent,
    conversationProfile);

                var routing = _flowDecisionService.ResolveFinalRouting(
                    normalizedMessage,
                    effectiveIntent,
                    conversationProfile,
                    contextDecision,
                    baseRouting);
                _logger.LogInformation(
                    "Flow routed. ConversationId: {ConversationId}, FlowType: {FlowType}, Reason: {Reason}",
                    conversationId,
                    routing.FlowType,
                    routing.Reason);
                if (effectiveIntent.MentionedProducts.Any())
                {
                    await _conversationPreferenceService.SetMentionedProductsAsync(
                        conversationId,
                        effectiveIntent.MentionedProducts);
                }

                await _conversationPreferenceService.SetLastIntentTypeAsync(
    conversationId,
    effectiveIntent.IntentType);

                _logger.LogInformation(
                    "Conversation profile merged. ConversationId: {ConversationId}, ProfileSummary: {ProfileSummary}",
                    conversationId,
                    _conversationPreferenceService.BuildProfileSummary(conversationProfile));
                _logger.LogInformation(
    "Effective intent. Category: {Category}, Brand: {Brand}, Target: {Target}, PriceMin: {PriceMin}, PriceMax: {PriceMax}, FilterType: {FilterType}, TargetPrice: {TargetPrice}, ForWork: {ForWork}, ForSchool: {ForSchool}, WantsLargeStorage: {WantsLargeStorage}",
    effectiveIntent.Category,
    effectiveIntent.Brand,
    effectiveIntent.Target,
    effectiveIntent.PriceMin,
    effectiveIntent.PriceMax,
    effectiveIntent.FilterType,
    effectiveIntent.TargetPrice,
    effectiveIntent.ForWork,
    effectiveIntent.ForSchool,
    effectiveIntent.WantsLargeStorage);

                if (ShouldRunForcedPriceRefinement(
     normalizedMessage,
     parsedIntent,
     effectiveIntent,
     conversationProfile,
     contextDecision,
     routing))
                {
                    _logger.LogInformation(
                        "Hard override force price refinement. ConversationId: {ConversationId}, Message: {Message}, FilterType: {FilterType}, Min: {Min}, Max: {Max}, Target: {Target}",
                        conversationId,
                        normalizedMessage,
                        parsedIntent.FilterType,
                        parsedIntent.PriceMin,
                        parsedIntent.PriceMax,
                        parsedIntent.TargetPrice);

                    var forcedRefineResponse = await _refinementService.HandleAsync(
                        conversationId,
                        normalizedMessage,
                        parsedIntent,
                        conversationProfile);

                    if (forcedRefineResponse != null)
                    {
                        stopwatch.Stop();
                        forcedRefineResponse.ConversationId = conversationId;
                        forcedRefineResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        forcedRefineResponse.UsedAI = false;
                        return forcedRefineResponse;
                    }

                    stopwatch.Stop();
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Mình đã hiểu mức giá bạn muốn lọc thêm, nhưng trong nhóm đang gợi ý hiện chưa đủ dữ liệu để chốt chính xác hơn.",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }
                NormalizeRequestMetadata(request);
                if (conversationProfile.HasPendingOrderLookup)
                {
                    var extractedOrderId = ExtractOrderId(normalizedMessage);
                    var extractedPhone = ExtractPhone(normalizedMessage);

                    if (extractedOrderId.HasValue && !conversationProfile.PendingOrderId.HasValue)
                    {
                        conversationProfile.PendingOrderId = extractedOrderId.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(extractedPhone) && string.IsNullOrWhiteSpace(conversationProfile.PendingOrderPhone))
                    {
                        conversationProfile.PendingOrderPhone = extractedPhone;
                    }

                    if (conversationProfile.PendingOrderId.HasValue &&
                        !string.IsNullOrWhiteSpace(conversationProfile.PendingOrderPhone))
                    {
                        var pendingRequest = new ChatRequest
                        {
                            ConversationId = conversationId,
                            Channel = request.Channel,
                            UserId = request.UserId,
                            Message = normalizedMessage,
                            Phone = conversationProfile.PendingOrderPhone
                        };

                        var orderResult = await HandleOrderLookupAsync(
                            pendingRequest,
                            $"mã đơn {conversationProfile.PendingOrderId.Value} {conversationProfile.PendingOrderPhone}");

                        await ClearPendingOrderLookupAsync(conversationId);

                        stopwatch.Stop();
                        orderResult.ConversationId = conversationId;
                        orderResult.UsedAI = false;
                        orderResult.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        return orderResult;
                    }

                    if (!conversationProfile.PendingOrderId.HasValue)
                    {
                        stopwatch.Stop();
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = "Mình đã nhận được thông tin rồi. Bạn gửi thêm mã đơn hàng giúp mình nhé.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }

                    if (string.IsNullOrWhiteSpace(conversationProfile.PendingOrderPhone))
                    {
                        stopwatch.Stop();
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = $"Mình đã nhận được mã đơn {conversationProfile.PendingOrderId.Value}. Bạn gửi thêm số điện thoại dùng khi đặt hàng giúp mình nhé.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                }
                if (string.Equals(routing.FlowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase))
                {
                    bool currentTurnHasExplicitBrand = !string.IsNullOrWhiteSpace(parsedIntent.Brand);
                    bool currentTurnHasExplicitCategory = !string.IsNullOrWhiteSpace(parsedIntent.Category);
                    bool currentTurnHasExplicitBudget =
                        parsedIntent.TargetPrice.HasValue ||
                        parsedIntent.PriceMin.HasValue ||
                        parsedIntent.PriceMax.HasValue;

                    bool currentTurnLooksFreshRecommendation =
                        parsedIntent.IntentType == "recommend" &&
                        !parsedIntent.IsFollowUp &&
                        !parsedIntent.IsBrandSwitch;

                    bool previousFlowWasLookupOrSearch =
                        string.Equals(previousActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(previousActiveFlow, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase);

                    if (currentTurnLooksFreshRecommendation && previousFlowWasLookupOrSearch)
                    {
                        if (!currentTurnHasExplicitBrand)
                            conversationProfile.PreferredBrand = null;

                        if (!currentTurnHasExplicitCategory)
                            conversationProfile.PreferredCategory = null;

                        if (!currentTurnHasExplicitBudget)
                        {
                            conversationProfile.PriceMin = null;
                            conversationProfile.PriceMax = null;
                            conversationProfile.TargetPrice = null;
                            conversationProfile.FilterType = PriceFilterType.None;
                        }

                        // Dọn context search cũ
                        conversationProfile.LastSearchProductNames.Clear();
                        conversationProfile.LastSearchProductIds.Clear();

                        // Dọn luôn recommendation context cũ nếu vòng hiện tại là recommendation mới
                        conversationProfile.HasActiveRecommendationContext = false;
                        conversationProfile.LastRecommendedProducts.Clear();
                        conversationProfile.LastRecommendedProductIds.Clear();
                        conversationProfile.LastAnswerMode = null;

                        // Dọn compare context cũ để tránh lẫn sang flow mới
                        conversationProfile.HasActiveCompareContext = false;
                        conversationProfile.LastComparedProducts.Clear();
                        conversationProfile.LastComparisonFeature = null;

                        _logger.LogInformation(
                            "Fresh recommendation cleanup applied strongly. ConversationId: {ConversationId}",
                            conversationId);
                    }
                }


                _logger.LogInformation(
                    "Processing chat message. ConversationId: {ConversationId}, Channel: {Channel}, UserId: {UserId}",
                    request.ConversationId,
                    request.Channel,
                    request.UserId);

                // ===== FLOW-BASED ROUTING =====

                // 1. Greeting
                if (string.Equals(routing.FlowType, ChatFlowType.Greeting, StringComparison.OrdinalIgnoreCase))
                {
                    stopwatch.Stop();
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Xin chào 👋 Mình có thể hỗ trợ bạn tra cứu giá xe, kiểm tra tồn kho, tư vấn mẫu xe phù hợp hoặc tra cứu đơn hàng.",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // 2. Out of scope
                if (string.Equals(routing.FlowType, ChatFlowType.OutOfScope, StringComparison.OrdinalIgnoreCase))
                {
                    stopwatch.Stop();
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Mình hiện chỉ hỗ trợ về xe máy, sản phẩm trong hệ thống và tra cứu đơn hàng. Bạn cứ hỏi mình về mẫu xe, giá, còn hàng hay tư vấn chọn xe nhé.",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // 3. Order lookup
                if (string.Equals(routing.FlowType, ChatFlowType.OrderLookup, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Order lookup flow detected. ConversationId: {ConversationId}, Message: {Message}",
                        request.ConversationId,
                        normalizedMessage);

                    var result = await HandleOrderLookupAsync(request, normalizedMessage);
                    stopwatch.Stop();

                    result.ConversationId = conversationId;
                    result.UsedAI = false;
                    result.ElapsedMs = stopwatch.ElapsedMilliseconds;
                    return result;
                }

                // 4. Direct product lookup
                if (string.Equals(routing.FlowType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
                {
                    var lookupResponse = await _productLookupFlowService.HandleAsync(
     conversationId,
     normalizedMessage,
     effectiveIntent,
     conversationProfile);

                    if (lookupResponse != null)
                    {
                        stopwatch.Stop();
                        lookupResponse.ConversationId = conversationId;
                        lookupResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        lookupResponse.UsedAI = false;
                        return lookupResponse;
                    }
                }

                if (string.Equals(routing.FlowType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
                {
                    var compareResponse = await _compareService.CompareAsync(
                        conversationId,
                        normalizedMessage,
                        effectiveIntent,
                        conversationProfile);

                    if (compareResponse != null)
                    {
                        var comparedTargets = effectiveIntent.MentionedProducts.Any()
                            ? effectiveIntent.MentionedProducts
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Take(2)
                                .ToList()
                            : conversationProfile.LastComparedProducts
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Take(2)
                                .ToList();

                        await _conversationPreferenceService.SetComparedProductsAsync(
                            conversationId,
                            comparedTargets);

                        conversationProfile.LastComparedProducts.Clear();
                        conversationProfile.LastComparedProducts.AddRange(comparedTargets);
                        conversationProfile.HasActiveCompareContext = true;
                        conversationProfile.ActiveFlow = ChatFlowType.Compare;
                        conversationProfile.HasActiveRecommendationContext = false;

                        stopwatch.Stop();
                        compareResponse.ConversationId = conversationId;
                        compareResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        compareResponse.UsedAI = false;
                        return compareResponse;
                    }

                    if (conversationProfile.HasActiveCompareContext &&
                        conversationProfile.LastComparedProducts.Count >= 2)
                    {
                        stopwatch.Stop();
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = $"Mình đang hiểu bạn muốn so tiếp giữa **{conversationProfile.LastComparedProducts[0]}** và **{conversationProfile.LastComparedProducts[1]}**, nhưng hiện chưa đủ dữ liệu để kết luận rõ hơn theo tiêu chí này.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }
                }
                if (!conversationProfile.HasActiveCompareContext &&
     CompareConversationRules.LooksLikeOrphanCompareFollowUp(normalizedMessage))
                {
                    stopwatch.Stop();
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Mình cần biết bạn đang muốn so sánh cặp xe nào. Bạn có thể hỏi kiểu như: \"Vision với Latte, con nào cốp rộng hơn?\"",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }
                // 6. Recommendation follow-up
                if (contextDecision != RecommendationContextDecision.ExpandFromCurrentGoal &&
 contextDecision != RecommendationContextDecision.StartFreshRecommendation &&
 string.Equals(routing.FlowType, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase))
                {
                    var followUpResponse = await _recommendationFollowUpService.HandleAsync(
    conversationId,
    normalizedMessage,
    effectiveIntent,
    conversationProfile);

                    if (followUpResponse != null)
                    {
                        stopwatch.Stop();
                        followUpResponse.ConversationId = conversationId;
                        followUpResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        followUpResponse.UsedAI = false;
                        return followUpResponse;
                    }
                }

                // 7. Brand switch within compare context
                if (string.Equals(routing.FlowType, ChatFlowType.BrandSwitch, StringComparison.OrdinalIgnoreCase) &&
                    conversationProfile.HasActiveCompareContext &&
                    conversationProfile.LastComparedProducts.Count >= 2)
                {
                    var compareBrandSwitchResponse = await HandleBrandSwitchWithinCompareContextAsync(
                        conversationId,
                        parsedIntent,
                        conversationProfile);

                    if (compareBrandSwitchResponse != null)
                    {
                        stopwatch.Stop();
                        compareBrandSwitchResponse.ConversationId = conversationId;
                        compareBrandSwitchResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        compareBrandSwitchResponse.UsedAI = false;
                        return compareBrandSwitchResponse;
                    }
                }
                bool asksAlternativeChoice =
    normalizedMessage.Contains("loại khác", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("loai khac", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("xe khác", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("xe khac", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("mẫu khác", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("mau khac", StringComparison.OrdinalIgnoreCase);
                bool looksBudgetPivot =
    RecommendationConversationRules.LooksLikeBudgetPivotFollowUp(normalizedMessage);
                if (!looksBudgetPivot &&
(
    (contextDecision == RecommendationContextDecision.NarrowWithinCurrentSet &&
     string.Equals(routing.FlowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase))
    ||
    (asksAlternativeChoice && conversationProfile.HasActiveRecommendationContext)
))
                {

                    var refineResponse = await _refinementService.HandleAsync(
    conversationId,
    normalizedMessage,
    effectiveIntent,
    conversationProfile);

                    if (refineResponse != null)
                    {
                        conversationProfile.HasActiveCompareContext = false;
                        conversationProfile.LastComparedProducts.Clear();
                        conversationProfile.LastComparisonFeature = null;
                        stopwatch.Stop();
                        refineResponse.ConversationId = conversationId;
                        refineResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        refineResponse.UsedAI = false;
                        return refineResponse;
                    }
                    if (asksAlternativeChoice)
                    {
                        stopwatch.Stop();
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = "Mình đang hiểu bạn muốn xem phương án khác trong cùng nhóm gợi ý. Bạn có thể nói rõ hơn như xe ga, xe số hoặc ưu tiên cốp rộng / tiết kiệm xăng để mình lọc sát hơn nhé.",
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }

                    stopwatch.Stop();
                    return new ChatResponse
                    {
                        Success = true,
                        Reply = "Mình đã hiểu tiêu chí lọc thêm của bạn, nhưng hiện chưa đủ dữ liệu để lọc chính xác hơn ở bước này. Bạn có thể nói rõ hơn một chút như hãng muốn ưu tiên, mức giá hoặc mẫu đang phân vân nhé.",
                        ConversationId = conversationId,
                        UsedAI = false,
                        ElapsedMs = stopwatch.ElapsedMilliseconds
                    };
                }

                // 8. Product search
                if (string.Equals(routing.FlowType, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase))
                {
                    var searchResponse = await _productSearchFlowService.HandleAsync(
    conversationId,
    normalizedMessage,
    effectiveIntent,
    conversationProfile);

                    if (searchResponse != null)
                    {
                        conversationProfile.ActiveFlow = ChatFlowType.ProductSearch;
                        conversationProfile.HasActiveCompareContext = false;
                        stopwatch.Stop();
                        searchResponse.ConversationId = conversationId;
                        searchResponse.ElapsedMs = stopwatch.ElapsedMilliseconds;
                        searchResponse.UsedAI = false;
                        return searchResponse;
                    }
                }

                // 9. Recommendation
                string? forcedToolName = null;
                bool hasPreparedToolPrompt = false;
                bool wantedToolFirstConsultation =
                    string.Equals(routing.FlowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase);
                if (wantedToolFirstConsultation)
                {
                    bool hasEnoughSignalsForDirectRecommendation =
    _recommendationClarificationService.HasEnoughSignalsForDirectRecommendation(
        normalizedMessage,
        effectiveIntent,
        conversationProfile);

                    bool looksBudgetOnlyRecommendation =
    (effectiveIntent.PriceMin.HasValue ||
     effectiveIntent.PriceMax.HasValue ||
     effectiveIntent.TargetPrice.HasValue ||
     effectiveIntent.FilterType != PriceFilterType.None) &&
    string.IsNullOrWhiteSpace(effectiveIntent.Target) &&
    string.IsNullOrWhiteSpace(effectiveIntent.Brand) &&
    string.IsNullOrWhiteSpace(effectiveIntent.Category) &&
    !effectiveIntent.ForWork &&
    !effectiveIntent.ForSchool &&
    !effectiveIntent.ForCity &&
    !effectiveIntent.ForTour &&
    !effectiveIntent.WantsFuelSaving &&
    !effectiveIntent.WantsLargeStorage &&
    !effectiveIntent.WantsEasyControl &&
    !effectiveIntent.NeedsLowSeat;

                    bool shouldClarifyRecommendation =
    !hasEnoughSignalsForDirectRecommendation &&
    !looksBudgetOnlyRecommendation &&
    _recommendationClarificationService.NeedsClarificationForConsultation(
        normalizedMessage,
        effectiveIntent,
        conversationProfile) &&
    !RecommendationConversationRules.LooksLikeRecommendationFollowUp(
        normalizedMessage,
        effectiveIntent,
        conversationProfile);
                    _logger.LogInformation(
    "Recommendation clarification gate. ConversationId: {ConversationId}, HasEnoughSignals: {HasEnoughSignals}, ShouldClarify: {ShouldClarify}, Message: {Message}",
    conversationId,
    hasEnoughSignalsForDirectRecommendation,
    shouldClarifyRecommendation,
    normalizedMessage);
                    if (shouldClarifyRecommendation)
                    {
                        _logger.LogInformation(
                            "Recommendation clarification triggered. ConversationId: {ConversationId}, ContextDecision: {ContextDecision}, IntentType: {IntentType}, Message: {Message}",
                            conversationId,
                            contextDecision,
                            parsedIntent.IntentType,
                            normalizedMessage);

                        stopwatch.Stop();
                        return new ChatResponse
                        {
                            Success = true,
                            Reply = _recommendationClarificationService.BuildClarificationQuestion(
    normalizedMessage,
    effectiveIntent,
    conversationProfile),
                            ConversationId = conversationId,
                            UsedAI = false,
                            ElapsedMs = stopwatch.ElapsedMilliseconds
                        };
                    }

                    _logger.LogInformation(
                        "Recommendation execution mode. ConversationId: {ConversationId}, ContextDecision: {ContextDecision}, IntentType: {IntentType}, Message: {Message}",
                        conversationId,
                        contextDecision,
                        parsedIntent.IntentType,
                        normalizedMessage);

                    var consultationResponse = await TryBuildToolFirstConsultationAsync(
                        request,
                        conversationId,
                        normalizedMessage,
                        effectiveIntent,
                        conversationProfile);

                    if (consultationResponse != null)
                    {
                        if (!string.IsNullOrWhiteSpace(consultationResponse.Reply))
                        {
                            conversationProfile.ActiveFlow = ChatFlowType.Recommendation;
                            conversationProfile.HasActiveCompareContext = false;
                            conversationProfile.LastComparedProducts.Clear();
                            conversationProfile.LastComparisonFeature = null;
                            stopwatch.Stop();

                            return new ChatResponse
                            {
                                Success = true,
                                Reply = consultationResponse.Reply,
                                ConversationId = conversationId,
                                UsedAI = false,
                                UsedTool = consultationResponse.ToolName,
                                ElapsedMs = stopwatch.ElapsedMilliseconds,
                                Products = consultationResponse.Products
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

                bool useRag = routing.ShouldUseRag || ShouldUseRag(normalizedMessage);

                if (useRag)
                {
                    try
                    {
                        var ragResult = await _ragService.QueryAsync(normalizedMessage, topK: 8);

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
    effectiveIntent,
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

                if ((string.IsNullOrWhiteSpace(aiResult.Reply) || !aiResult.Success)
                    && !string.IsNullOrWhiteSpace(ragContext))
                {
                    aiResult.Reply = BuildRagOnlyReply(ragContext);
                    aiResult.Success = true;
                    aiResult.UsedAI = false;
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
        private static bool ShouldRunForcedPriceRefinement(
    string normalizedMessage,
    ParsedIntent parsedIntent,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile conversationProfile,
    RecommendationContextDecision contextDecision,
    FlowRoutingResult routing)
        {
            return
                !LookupConversationRules.LooksLikeDirectProductLookup(normalizedMessage, parsedIntent) &&
                contextDecision == RecommendationContextDecision.NarrowWithinCurrentSet &&
                string.Equals(routing.FlowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase) &&
                ShouldForcePriceRefinement(parsedIntent, conversationProfile) &&
                !RecommendationConversationRules.LooksLikeBudgetPivotFollowUp(normalizedMessage) &&
                !string.Equals(effectiveIntent.IntentType, "recommend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MessageExplicitlyMentionsUseCase(string message, string useCase)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(useCase))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return useCase switch
            {
                "work" => text.Contains("đi làm") || text.Contains("di lam"),
                "school" => text.Contains("đi học") || text.Contains("di hoc") || text.Contains("sinh viên") || text.Contains("sinh vien"),
                "city" => text.Contains("đi phố") || text.Contains("di pho") || text.Contains("trong phố") || text.Contains("trong pho"),
                "tour" => text.Contains("đường dài") || text.Contains("duong dai") || text.Contains("đi tour") || text.Contains("di tour"),
                _ => false
            };
        }
        private static bool LooksLikePureBudgetChangeText(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasBudgetSignal =
                text.Contains("không phải") ||
                text.Contains("khong phai") ||
                text.Contains("giờ") ||
                text.Contains("gio") ||
                text.Contains("xuống") ||
                text.Contains("xuong") ||
                text.Contains("tầm") ||
                text.Contains("tam") ||
                text.Contains("khoảng") ||
                text.Contains("khoang") ||
                text.Contains("quanh") ||
                Regex.IsMatch(text, @"\b\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b", RegexOptions.IgnoreCase);

            bool hasExplicitGoalChange =
                text.Contains("cho nữ") ||
                text.Contains("cho nu") ||
                text.Contains("cho nam") ||
                text.Contains("xe ga") ||
                text.Contains("xe số") ||
                text.Contains("xe so") ||
                text.Contains("côn tay") ||
                text.Contains("con tay") ||
                text.Contains("đi làm") ||
                text.Contains("di lam") ||
                text.Contains("đi học") ||
                text.Contains("di hoc") ||
                text.Contains("honda") ||
                text.Contains("yamaha") ||
                text.Contains("suzuki") ||
                text.Contains("sym") ||
                text.Contains("piaggio");

            return hasBudgetSignal && !hasExplicitGoalChange;
        }
        private static void MarkAsRecommendation(ParsedIntent intent)
        {
            intent.IntentType = "recommend";
            intent.IsOpenRecommendation = true;
            intent.IsProductSearch = false;
            intent.RouteFlow = ChatFlowType.Recommendation;
            intent.HasDeterministicProductIntent = false;
        }
        private static bool HasExplicitFemaleSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = text.Trim().ToLowerInvariant();

            return
                Regex.IsMatch(value, @"(^|\s)(nữ|nu)(\s|$)", RegexOptions.IgnoreCase) ||
                value.Contains("cho nữ") ||
                value.Contains("cho nu") ||
                value.Contains("xe nữ") ||
                value.Contains("xe nu") ||
                value.Contains("hợp nữ") ||
                value.Contains("hop nu") ||
                value.Contains("nữ tính") ||
                value.Contains("nu tinh") ||
                value.Contains("phù hợp cho nữ") ||
                value.Contains("phu hop cho nu");
        }

        private static bool HasExplicitMaleSignal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = text.Trim().ToLowerInvariant();

            return
                Regex.IsMatch(value, @"(^|\s)nam(\s|$)", RegexOptions.IgnoreCase) ||
                value.Contains("cho nam") ||
                value.Contains("xe nam") ||
                value.Contains("hợp nam") ||
                value.Contains("hop nam") ||
                value.Contains("phù hợp cho nam") ||
                value.Contains("phu hop cho nam") ||
                value.Contains("nam tính") ||
                value.Contains("manly");
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

                decimal? toolMinPrice = parsedIntent.PriceMin ?? conversationProfile.PriceMin;
                decimal? toolMaxPrice = parsedIntent.PriceMax ?? conversationProfile.PriceMax;

                if ((parsedIntent.FilterType == PriceFilterType.Around && parsedIntent.TargetPrice.HasValue) ||
                    (conversationProfile.FilterType == PriceFilterType.Around && conversationProfile.TargetPrice.HasValue))
                {
                    var target = parsedIntent.TargetPrice ?? conversationProfile.TargetPrice ?? 0;
                    var delta = target <= 35_000_000m ? 4_000_000m : 5_000_000m;
                    toolMinPrice = Math.Max(0, target - delta);
                    toolMaxPrice = target + delta;
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

                string? advisoryContext = null;
                Dictionary<string, string> ragReasonHints = new(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var ragQuery = BuildRagAdvisoryQuery(
                        normalizedMessage,
                        parsedIntent,
                        conversationProfile,
                        rankedItems);

                    var ragResult = await _ragService.QueryAsync(ragQuery, topK: 8);

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
                await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
     conversationId,
     rankedItems);

                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    rankedItems,
                    "fresh_consultation");
                return new ToolFirstConsultationResult
                {
                    ToolName = ToolNames.GetProductsByFilters,
                    EffectivePrompt = effectivePrompt,
                    Reply = deterministicReply,
                    Products = ChatProductCardMapper.MapMany(rankedItems, 4)
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
                await SetPendingOrderLookupAsync(request.ConversationId ?? string.Empty, null, null);

                return new ChatResponse
                {
                    Success = true,
                    Reply = "Để mình kiểm tra đơn hàng cho bạn, bạn vui lòng cung cấp mã đơn hàng và số điện thoại dùng khi đặt hàng nhé.",
                    UsedTool = null
                };
            }

            if (orderId == null)
            {
                await SetPendingOrderLookupAsync(request.ConversationId ?? string.Empty, null, phone);

                return new ChatResponse
                {
                    Success = true,
                    Reply = "Mình đã nhận được số điện thoại. Bạn vui lòng cung cấp thêm mã đơn hàng để mình tra cứu chính xác nhé.",
                    UsedTool = null
                };
            }

            if (string.IsNullOrWhiteSpace(phone))
            {
                await SetPendingOrderLookupAsync(request.ConversationId ?? string.Empty, orderId.Value, null);

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
            await ClearPendingOrderLookupAsync(request.ConversationId ?? string.Empty);
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

            if (IsFollowUpPreferenceFragment(text))
                return false;

            if (Regex.IsMatch(text, @"^(case|xe|tư vấn|tu van|mình muốn|toi muon|tôi muốn|cho mình|giá|bao nhiêu|còn hàng|so sánh|tra đơn|kiểm tra đơn|đơn hàng|giờ t muốn|gio t muon|h t muốn|vậy h t muốn|vay h t muon|ý là h t muốn|y la h t muon|đổi ý|doi y)\b",
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

    
        private bool ShouldUseToolFirstConsultation(
     string message,
     ParsedIntent parsedIntent,
     CustomerPreferenceProfile? profile = null)
        {
            // If message contains price/realtime keywords, use tool
            var checkText = message?.ToLowerInvariant() ?? string.Empty;
            string[] toolKeywords = { "giá", "còn hàng", "tồn kho", "có sẵn", "bao nhiêu", "mua", "dưới", "trên", "tầm", "khoảng", "quanh", "triệu" };
            if (toolKeywords.Any(k => checkText.Contains(k)))
                return true;

            var safeMessage = message ?? string.Empty;
            var text = safeMessage.ToLowerInvariant();

            bool currentMessageLooksLikeConsultation =
   _recommendationClarificationService.IsConsultationIntent(safeMessage)
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
            sb.AppendLine("Không được tự suy ra phong cách như thanh lịch, nữ tính, thể thao nếu người dùng không nói hoặc dữ liệu xếp hạng không thể hiện rõ.");
            sb.AppendLine("Nếu người dùng chỉ đổi loại xe hoặc đổi ngân sách thì phải giữ nguyên các tín hiệu còn lại, không tự thêm sở thích mới.");
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
    HasExplicitFemaleSignal(text) ||
    HasExplicitMaleSignal(text))
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
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"\b(từ|tu)\s*\d+([.,]\d+)?\s*(đến|den)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"^\d+([.,]\d+)?\s*[-~]\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"\b(dưới|duoi|trên|tren|tối đa|toi da|không quá|khong qua|ít nhất|it nhat|trở lên|tro len)\s*\d+([.,]\d+)?\s*(triệu|triêu|trieu|tr|củ|cu|chai)\b",
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
            sb.AppendLine("Không được tự thêm phong cách hoặc đối tượng mới nếu người dùng không nêu rõ.");
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
                "giá", "còn hàng", "tồn kho", "có sẵn", "bao nhiêu", "mua",
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
        private static bool ShouldForcePriceRefinement(
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile profile)
        {
            if (parsedIntent == null || profile == null)
                return false;

            if (!profile.HasActiveRecommendationContext || profile.LastRecommendedProducts.Count == 0)
                return false;

            bool hasPriceSignal =
                parsedIntent.FilterType != PriceFilterType.None ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue;

            if (!hasPriceSignal)
                return false;

            bool hasOtherStrongSignal =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                parsedIntent.MentionedProducts.Any() ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.ForSchool ||
                parsedIntent.ForWork ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                parsedIntent.ExcludedBrands.Any() ||
                parsedIntent.ExcludedCategories.Any();

            return hasPriceSignal && !hasOtherStrongSignal;
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

            var sb = new StringBuilder();
            sb.AppendLine(intro);
            sb.AppendLine();
            sb.AppendLine(string.Join("\n", lines));

            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                sb.AppendLine();
                sb.AppendLine(suggestion);
            }

            // chỉ hỏi tiếp khi thật sự là câu mở hoặc còn nhiều hướng lọc hợp lý
            bool shouldAskFollowUp =
                selectedItems.Count >= 2 &&
                (
                    isOpenQuery ||
                    parsedIntent.WantsLargeStorage ||
                    parsedIntent.NeedsLowSeat ||
                    parsedIntent.WantsFuelSaving ||
                    parsedIntent.ForWork ||
                    parsedIntent.ForSchool ||
                    (profile?.ForWork == true) ||
                    (profile?.ForSchool == true) ||
                    (profile?.WantsLargeStorage == true) ||
                    (profile?.NeedsLowSeat == true)
                );

            if (shouldAskFollowUp && !string.IsNullOrWhiteSpace(followUp))
            {
                sb.AppendLine();
                sb.AppendLine(followUp);
            }

            return sb.ToString().Trim();
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
        private static string BuildConsultationContextPhrase(
    string text,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile = null)
        {
            bool forWork = text.Contains("đi làm") || profile?.ForWork == true || parsedIntent.ForWork;
            bool forSchool = text.Contains("đi học") || text.Contains("sinh viên") || profile?.ForSchool == true || parsedIntent.ForSchool;
            bool wantsLargeStorage = text.Contains("cốp rộng") || text.Contains("mang đồ") || profile?.WantsLargeStorage == true || parsedIntent.WantsLargeStorage;
            bool needsLowSeat = text.Contains("dễ chống chân") || text.Contains("yên thấp") || profile?.NeedsLowSeat == true || parsedIntent.NeedsLowSeat;
            bool wantsFuelSaving = text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang") || profile?.WantsFuelSaving == true || parsedIntent.WantsFuelSaving;

            if (forWork && wantsLargeStorage)
                return "cho nhu cầu đi làm và tiện mang đồ";

            if (forWork)
                return "cho nhu cầu đi làm hằng ngày";

            if (forSchool && wantsFuelSaving)
                return "cho nhu cầu đi học tiết kiệm";

            if (forSchool)
                return "cho nhu cầu đi học hằng ngày";

            if (needsLowSeat)
                return "nếu bạn ưu tiên dễ chống chân";

            if (wantsLargeStorage)
                return "nếu bạn ưu tiên cốp rộng";

            if (wantsFuelSaving)
                return "nếu bạn ưu tiên tiết kiệm xăng";

            return "trong nhóm này";
        }
        private static (bool PrefersMaleStyle, bool PrefersFemaleStyle) ResolveGenderPreference(
    string? message,
    string? target,
    CustomerPreferenceProfile? profile)
        {
            var normalizedMessage = (message ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedTarget = (target ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedProfileTarget = (profile?.Target ?? string.Empty).Trim().ToLowerInvariant();

            bool explicitMale =
                HasExplicitMaleSignal(normalizedTarget) ||
                HasExplicitMaleSignal(normalizedMessage);

            bool explicitFemale =
                HasExplicitFemaleSignal(normalizedTarget) ||
                HasExplicitFemaleSignal(normalizedMessage);

            if ((normalizedMessage.Contains("không phải nữ") || normalizedMessage.Contains("khong phai nu")) && explicitMale)
                return (true, false);

            if ((normalizedMessage.Contains("không phải nam") || normalizedMessage.Contains("khong phai nam")) && explicitFemale)
                return (false, true);

            if (explicitMale && !explicitFemale)
                return (true, false);

            if (explicitFemale && !explicitMale)
                return (false, true);

            if (HasExplicitMaleSignal(normalizedProfileTarget))
                return (true, false);

            if (HasExplicitFemaleSignal(normalizedProfileTarget))
                return (false, true);

            return (
                profile?.PrefersMaleStyle == true,
                profile?.PrefersFemaleStyle == true
            );
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

            var genderPreference = ResolveGenderPreference(text, parsedIntent.Target, profile);
            bool asksForMale = genderPreference.PrefersMaleStyle;
            bool asksForFemale = genderPreference.PrefersFemaleStyle;
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
                if (asksForMale && asksForWork)
                    signatureReason = "là mẫu ga đi phố êm và dễ dùng, nhưng thiên về phong cách mềm hơn";
                else if (asksForFemale)
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
                if (asksForMale)
                    signatureReason = "là phương án xe ga giá mềm, nhưng kiểu dáng sẽ hợp hơn nếu bạn thích hướng thanh lịch nhẹ nhàng";
                else if (asksForFemale)
                    signatureReason = Pick(
                        "hợp hơn nếu bạn thích kiểu dáng nữ tính và mềm mại",
                        "khá hợp nếu bạn ưu tiên kiểu dáng thanh lịch và mềm mại",
                        "dễ hợp gu hơn nếu bạn thích dáng xe nữ tính");
                else if (asksForWork || profile?.ForWork == true)
                    signatureReason = Pick(
                        "khá hợp đi phố hằng ngày theo hướng nhẹ nhàng và dễ điều khiển",
                        "là phương án khá ổn nếu bạn thích cảm giác lái nhẹ nhàng khi đi phố",
                        "khá hợp nếu bạn muốn một mẫu xe ga đi phố theo hướng êm và dễ điều khiển");
                else
                    signatureReason = Pick(
                        "thiên về kiểu dáng thanh lịch và cảm giác lái dễ chịu",
                        "khá hợp nếu bạn thích dáng xe mềm và đi nhẹ nhàng",
                        "là mẫu xe ga thiên về sự thanh lịch và dễ đi");
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
                if (asksForMale && asksForWork)
                    signatureReason = "đi êm và khá tiện dụng, nhưng phong cách sẽ mềm hơn nhóm nam tính";
                else if (asksForFemale)
                    signatureReason = "hợp nếu bạn thích kiểu dáng nữ tính và mềm mại hơn";
                else
                    signatureReason = "thiên về cảm giác đi êm và phong cách thanh lịch";
            }
            else if (name.Contains("Vision", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForLowSeat)
                    signatureReason = Pick(
                        "dáng xe gọn và khá dễ chống chân",
                        "khá hợp nếu bạn ưu tiên sự gọn nhẹ và dễ chống chân",
                        "dễ kiểm soát hơn nếu bạn ưu tiên cảm giác lái nhẹ nhàng");
                else if (asksForSchool)
                    signatureReason = Pick(
                        "gọn, dễ làm quen và hợp đi học hằng ngày",
                        "khá hợp cho nhu cầu đi học và đi lại hằng ngày",
                        "dễ đi, gọn và khá hợp với nhu cầu sử dụng thường ngày");
                else if (asksForWork || profile?.ForWork == true)
                    signatureReason = Pick(
                        "linh hoạt khi đi phố và khá dễ dùng hằng ngày",
                        "khá hợp để đi lại hằng ngày trong phố",
                        "cân bằng tốt giữa sự gọn nhẹ và nhu cầu đi làm hằng ngày");
                else
                    signatureReason = Pick(
                        "dễ làm quen, gọn và hợp đi lại hằng ngày",
                        "khá dễ đi và phù hợp nếu bạn thích xe gọn",
                        "là mẫu cân bằng khá tốt cho nhu cầu đi lại thường ngày");
            }
            else if (name.Contains("Freego", StringComparison.OrdinalIgnoreCase))
            {
                if (asksForLargeStorage)
                    signatureReason = Pick(
                        "có lợi thế rõ hơn nếu bạn hay mang theo đồ",
                        "khá hợp nếu bạn ưu tiên cốp rộng và tính tiện dụng",
                        "nổi bật hơn ở nhóm cần chỗ để đồ tốt");
                else if (asksForWork || profile?.ForWork == true)
                    signatureReason = Pick(
                        "thiên về hướng thực dụng và tiện đi làm hằng ngày",
                        "khá hợp nếu bạn cần một mẫu tiện dụng để đi làm",
                        "cân bằng tốt giữa tiện ích và nhu cầu đi lại hằng ngày");
                else
                    signatureReason = Pick(
                        "cân bằng khá tốt giữa tiện ích và chi phí",
                        "là phương án đáng cân nhắc nếu bạn ưu tiên sự tiện dụng",
                        "khá hợp nếu bạn muốn một mẫu xe ga thực dụng hơn");
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
                if (asksForBudgetFriendly(text))
                    signatureReason = Pick(
                        "là phương án xe ga dễ tiếp cận hơn về chi phí",
                        "khá hợp nếu bạn muốn một mẫu ga giá mềm hơn",
                        "dễ cân nhắc hơn nếu bạn muốn giữ ngân sách nhẹ hơn");
                else if (asksForWork || profile?.ForWork == true)
                    signatureReason = Pick(
                        "là lựa chọn ga giá mềm khá hợp đi lại hằng ngày",
                        "khá hợp nếu bạn muốn xe ga đi làm mà chi phí vẫn mềm",
                        "là phương án thực dụng hơn nếu bạn cần xe ga trong tầm giá dễ chịu");
                else
                    signatureReason = Pick(
                        "giá khá mềm trong nhóm xe ga cùng tầm",
                        "là mẫu ga dễ tiếp cận hơn trong nhóm đang cân nhắc",
                        "khá hợp nếu bạn muốn một lựa chọn ga nhẹ ngân sách hơn");
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
                return Pick(
                    "là phương án khá cân bằng trong nhóm đang cân nhắc",
                    "là mẫu khá đáng để cân nhắc trong nhóm này",
                    "là lựa chọn khá ổn nếu bạn muốn giữ phương án an toàn");

            var distinctReasons = reasons
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return string.Join(", ", distinctReasons.Take(2));

            static bool asksForBudgetFriendly(string inputText)
            {
                return inputText.Contains("rẻ") || inputText.Contains("giá mềm") || inputText.Contains("tiết kiệm");
            }
        }

        private async Task SetPendingOrderLookupAsync(string conversationId, int? orderId, string? phone)
        {
            var profile = await _conversationPreferenceService.GetAsync(conversationId);
            profile.HasPendingOrderLookup = true;
            profile.PendingOrderId = orderId;
            profile.PendingOrderPhone = phone;
            profile.ActiveFlow = ChatFlowType.OrderLookup;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }

        private async Task ClearPendingOrderLookupAsync(string conversationId)
        {
            var profile = await _conversationPreferenceService.GetAsync(conversationId);
            profile.HasPendingOrderLookup = false;
            profile.PendingOrderId = null;
            profile.PendingOrderPhone = null;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }
        private async Task<ChatResponse?> HandleBrandSwitchWithinCompareContextAsync(
    string conversationId,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile profile)
        {
            if (profile == null ||
                !profile.HasActiveCompareContext ||
                profile.LastComparedProducts == null ||
                profile.LastComparedProducts.Count < 2)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(parsedIntent.Brand))
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = $"Mình đang so sánh giữa **{profile.LastComparedProducts[0]}** và **{profile.LastComparedProducts[1]}**. Bạn muốn mình lọc theo hãng nào giúp bạn nhé?"
                };
            }

            var comparedProducts = new List<ProductSummaryDto>();

            var comparedNames = profile.LastComparedProducts
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Take(2)
    .ToList();

            foreach (var name in comparedNames)
            {
                var searchResult = await _toolClient.SearchProductsAsync(name, 5);

                var matched = searchResult?.Items?
                    .OrderByDescending(x => string.Equals(x.Ten, name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.SoLuong)
                    .FirstOrDefault();

                if (matched != null)
                {
                    comparedProducts.Add(matched);
                }
            }

            if (comparedProducts.Count == 0)
            {
                return null;
            }

            var filtered = comparedProducts
                .Where(x => string.Equals(x.ThuongHieu, parsedIntent.Brand, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count == 0)
            {
                return new ChatResponse
                {
                    Success = true,
                    Reply = $"Trong cặp mình đang so sánh thì hiện không có mẫu **{parsedIntent.Brand}** nào."
                };
            }

            if (filtered.Count == 1)
            {
                var item = filtered[0];

                return new ChatResponse
                {
                    Success = true,
                    Reply = $"Nếu chỉ xét theo **{parsedIntent.Brand}** trong cặp mình đang so sánh thì đó là **{item.Ten}** ({item.Gia:N0} VNĐ)."
                };
            }

            var lines = filtered
                .Select(x => $"- **{x.Ten}** ({x.Gia:N0} VNĐ)")
                .ToList();

            return new ChatResponse
            {
                Success = true,
                Reply = $"Trong cặp mình đang so sánh, các mẫu thuộc **{parsedIntent.Brand}** gồm:\n\n{string.Join("\n", lines)}"
            };
        }
        private static string BuildFollowUpQuestion(
     string text,
     ParsedIntent parsedIntent,
     CustomerPreferenceProfile? profile = null)
        {
            var genderPreference = ResolveGenderPreference(text, parsedIntent.Target, profile);
            bool asksForMale = genderPreference.PrefersMaleStyle;
            bool asksForFemale = genderPreference.PrefersFemaleStyle;
            bool asksForSchool = text.Contains("sinh viên") || text.Contains("đi học") || profile?.ForSchool == true;
            bool asksForWork = text.Contains("đi làm") || profile?.ForWork == true;
            bool asksForLowSeat = text.Contains("dễ chống chân") || text.Contains("yên thấp") || text.Contains("người thấp") || text.Contains("nhỏ con") || profile?.NeedsLowSeat == true;
            bool asksForLargeStorage = text.Contains("cốp rộng") || text.Contains("mang đồ") || profile?.WantsLargeStorage == true;
            bool asksForFuelSaving = text.Contains("tiết kiệm xăng") || text.Contains("tiet kiem xang") || profile?.WantsFuelSaving == true;
            bool asksForSporty = text.Contains("cá tính") || text.Contains("ca tinh") || text.Contains("thể thao") || text.Contains("the thao");

            bool hasBudget =
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                profile?.TargetPrice.HasValue == true ||
                profile?.PriceMin.HasValue == true ||
                profile?.PriceMax.HasValue == true ||
                LooksLikeBudgetFragment(text) ||
                text.Contains("tầm") ||
                text.Contains("khoảng") ||
                text.Contains("quanh") ||
                text.Contains("triệu") ||
                text.Contains("triêu") ||
                text.Contains("trieu");

            if (asksForSchool)
            {
                return "Bạn muốn mình lọc tiếp theo hướng tiết kiệm xăng hơn hay ưu tiên cốp rộng và tiện mang đồ hơn?";
            }

            if (asksForWork && asksForLargeStorage)
            {
                return Pick(
                    "Bạn muốn mình nghiêng tiếp về nhóm cốp rộng tiện đi làm hay nhóm gọn nhẹ, linh hoạt hơn khi đi phố?",
                    "Bạn muốn ưu tiên hẳn tính tiện dụng để mang đồ hay vẫn giữ hướng xe gọn nhẹ dễ luồn phố?",
                    "Mình có thể lọc tiếp theo hướng cốp rộng nổi bật hơn hoặc hướng xe gọn và linh hoạt hơn, bạn muốn nghiêng về bên nào?");
            }
            if (asksForWork)
            {
                return Pick(
                    "Bạn muốn mình lọc tiếp theo hướng thực dụng dễ đi hơn hay ưu tiên cốp rộng và tiện mang đồ hơn?",
                    "Bạn muốn nghiêng tiếp về nhóm đi làm gọn nhẹ dễ dùng hay nhóm tiện ích hơn để mang đồ hằng ngày?",
                    "Mình có thể lọc tiếp theo hướng dễ đi trong phố hoặc hướng tiện dụng hơn, bạn muốn ưu tiên bên nào?");
            }

            if (asksForLowSeat)
            {
                return "Bạn muốn mình nghiêng tiếp về nhóm dễ chống chân nhất hay nhóm cân bằng hơn giữa dễ đi và kiểu dáng?";
            }

            if (asksForLargeStorage)
            {
                return Pick(
                    "Bạn muốn mình ưu tiên cốp rộng rõ hơn hay vẫn giữ cân bằng giữa tiện ích và dáng xe đẹp?",
                    "Bạn muốn nghiêng hẳn về nhóm tiện dụng hơn hay vẫn giữ xe gọn đẹp dễ đi?",
                    "Mình có thể lọc tiếp theo hướng cốp rộng hơn nữa hoặc hướng cân bằng hơn giữa tiện ích và kiểu dáng, bạn muốn bên nào?");
            }


            if (asksForFuelSaving)
            {
                return "Bạn muốn mình lọc tiếp theo hướng tiết kiệm xăng hơn hay cân bằng hơn giữa tiết kiệm và tiện ích?";
            }

            if (asksForSporty)
            {
                return "Bạn muốn mình nghiêng tiếp theo hướng cá tính rõ hơn hay vẫn giữ tiêu chí dễ đi hằng ngày?";
            }

            if (asksForFemale)
            {
                return "Bạn muốn mình nghiêng tiếp về nhóm gọn nhẹ dễ đi hay nhóm mềm mại, thanh lịch hơn?";
            }

            if (asksForMale)
            {
                return Pick(
                    "Bạn muốn mình lọc tiếp theo hướng đầm chắc hơn hay ưu tiên linh hoạt để đi phố hằng ngày?",
                    "Bạn muốn mình nghiêng tiếp về nhóm trung tính thực dụng hơn hay nhóm thể thao, mạnh hơn?",
                    "Mình có thể lọc tiếp theo hướng đi phố dễ dùng hơn hoặc hướng nam tính, đầm chắc hơn, bạn muốn nghiêng về bên nào?");
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
                return $"Nếu muốn, mình có thể lọc sâu hơn riêng trong nhóm {profile.PreferredBrand} để chọn ra mẫu hợp hơn với nhu cầu hiện tại.";
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
                return Pick(
                    "Mình thấy hiện tại có 1 mẫu khá hợp với nhu cầu bạn đang hỏi:",
                    "Hiện tại mình thấy có 1 mẫu nổi bật hơn trong trường hợp này:",
                    "Nếu lọc theo tiêu chí hiện tại thì mình đang thấy 1 mẫu khá hợp:");

            if (text.Contains("sinh viên"))
                return Pick(
                    $"Với nhu cầu này, mình thấy có {count} mẫu khá dễ cân nhắc:",
                    $"Nếu ưu tiên nhóm dễ đi và hợp sinh viên thì mình thấy có {count} mẫu đáng chú ý:",
                    $"Trong nhóm phù hợp với nhu cầu này, mình chọn ra {count} mẫu để bạn tham khảo:");

            if (text.Contains("nữ"))
                return Pick(
                    $"Mình lọc ra {count} mẫu khá hợp với nhu cầu của bạn:",
                    $"Nếu đi theo hướng này thì mình thấy có {count} mẫu khá đáng cân nhắc:",
                    $"Trong nhóm đang phù hợp nhất, mình gợi ý bạn {count} mẫu sau:");

            if (LooksLikeBudgetFragment(text) || text.Contains("tầm") || text.Contains("khoảng") || text.Contains("quanh"))
                return Pick(
                    $"Trong tầm giá này, mình thấy có {count} mẫu khá đáng cân nhắc:",
                    $"Nếu bám theo ngân sách này thì mình thấy có {count} mẫu nổi bật hơn để bạn tham khảo:",
                    $"Ở mức giá này, mình đang nghiêng về {count} phương án khá hợp để bạn cân nhắc:");

            return Pick(
                $"Mình gợi ý bạn {count} mẫu để tham khảo:",
                $"Mình thấy có {count} mẫu khá đáng chú ý trong trường hợp này:",
                $"Nếu lọc theo hướng bạn đang hỏi thì đây là {count} mẫu mình thấy khá hợp:");
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
            var genderPreference = ResolveGenderPreference(text, parsedIntent.Target, profile);
            bool asksForMale = genderPreference.PrefersMaleStyle;
            bool asksForFemale = genderPreference.PrefersFemaleStyle;
            bool asksForLowSeat = text.Contains("dễ chống chân") || text.Contains("yên thấp") || text.Contains("người thấp") || text.Contains("nhỏ con");
            bool asksForSchool = text.Contains("sinh viên") || text.Contains("đi học") || profile?.ForSchool == true;
            bool asksForWork = text.Contains("đi làm") || profile?.ForWork == true;
            bool asksForSporty = text.Contains("cá tính") || text.Contains("ca tinh") || text.Contains("thể thao") || text.Contains("the thao");
            bool asksForLargeStorage = text.Contains("cốp rộng") || text.Contains("mang đồ") || profile?.WantsLargeStorage == true;
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

                return Pick(
                    $"Nếu ưu tiên dễ chống chân và dễ làm quen hơn thì mình nghiêng về **{top.Ten}**, vì mẫu này cho cảm giác kiểm soát nhẹ nhàng hơn khi đi phố.",
                    $"Nếu cần một mẫu dễ kiểm soát hơn khi đi phố thì **{top.Ten}** là phương án mình nghiêng hơn.",
                    $"Trong nhóm này, nếu ưu tiên cảm giác dễ chống chân thì **{top.Ten}** nổi bật hơn một chút.");
            }

            if (asksForLargeStorage)
            {
                var storageCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Address", StringComparison.OrdinalIgnoreCase));

                if (storageCandidate != null)
                    top = storageCandidate;

                var contextPhrase = BuildConsultationContextPhrase(text, parsedIntent, profile);

                return Pick(
                    $"Nếu ưu tiên sự tiện dụng và chỗ để đồ tốt hơn thì mình nghiêng về **{top.Ten}**.",
                    $"Nếu cần một mẫu nổi bật hơn {contextPhrase} thì **{top.Ten}** là phương án đáng cân nhắc nhất, vì lợi thế tiện dụng và chỗ để đồ đang rõ hơn nhóm còn lại.",
                    $"Nếu bạn hay mang theo đồ và muốn ưu tiên tính tiện dụng thì **{top.Ten}** là mẫu mình nghiêng hơn.");
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

                return Pick(
                    $"Nếu bạn thích dáng thanh lịch và cảm giác đi nhẹ nhàng hơn thì **{top.Ten}** là mẫu nổi bật hơn.",
                    $"Nếu ưu tiên kiểu dáng mềm mại và cảm giác lái nhẹ nhàng thì mình nghiêng về **{top.Ten}** hơn.",
                    $"Trong nhóm này, nếu đặt kiểu dáng thanh lịch lên trước thì **{top.Ten}** sáng hơn một chút.");
            }

            if (asksForSchool)
            {
                var schoolCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Sirius", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Wave", StringComparison.OrdinalIgnoreCase));

                if (schoolCandidate != null)
                    top = schoolCandidate;

                return Pick(
                    $"Nếu ưu tiên một mẫu dễ đi và hợp dùng hằng ngày hơn thì mình nghiêng về **{top.Ten}**.",
                    $"Nếu cần chốt nhanh một mẫu cân bằng cho đi học hằng ngày thì **{top.Ten}** là phương án mình nghiêng hơn.",
                    $"Trong nhóm này, nếu ưu tiên sự dễ đi và chi phí dùng hằng ngày thì **{top.Ten}** là lựa chọn dễ chốt hơn.");
            }

            if (asksForWork)
            {
                var workCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Air Blade", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Freego", StringComparison.OrdinalIgnoreCase));

                if (workCandidate != null)
                    top = workCandidate;

                var contextPhrase = BuildConsultationContextPhrase(text, parsedIntent, profile);

                return Pick(
                    $"Nếu cần chốt nhanh một mẫu cân bằng {contextPhrase} thì mình nghiêng về **{top.Ten}** hơn, vì mẫu này giữ được sự dễ đi và độ linh hoạt khá tốt trong nhóm đáng cân nhắc.",
                    $"Nếu chọn một phương án dễ đi làm hằng ngày và khá dễ dùng lâu dài thì **{top.Ten}** là mẫu mình nghiêng hơn.",
                    $"Trong nhóm này, nếu ưu tiên sự cân bằng cho đi lại hằng ngày thì **{top.Ten}** là lựa chọn sáng hơn một chút.");
            }

            if (asksForSporty)
            {
                var sportyCandidate = items.FirstOrDefault(x =>
                    (x.Ten ?? "").Contains("Air Blade", StringComparison.OrdinalIgnoreCase) ||
                    (x.Ten ?? "").Contains("Vario", StringComparison.OrdinalIgnoreCase));

                if (sportyCandidate != null)
                    top = sportyCandidate;

                return Pick(
                    $"Nếu bạn thích kiểu nổi bật và cá tính rõ hơn thì **{top.Ten}** sẽ hợp gu hơn.",
                    $"Nếu ưu tiên phong cách thể thao và cảm giác mạnh mẽ hơn thì mình nghiêng về **{top.Ten}**.",
                    $"Trong nhóm này, nếu muốn một mẫu trông cá tính rõ hơn thì **{top.Ten}** nổi bật hơn.");
            }

            if (parsedIntent.TargetPrice.HasValue)
            {
                var contextPhrase = BuildConsultationContextPhrase(text, parsedIntent, profile);

                return Pick(
                    $"Nếu cần chốt nhanh một mẫu nổi bật {contextPhrase} thì mình đang nghiêng về **{top.Ten}** hơn.",
                    $"Trong tầm này, nếu chọn một mẫu cân bằng và dễ chốt hơn thì **{top.Ten}** là phương án mình nghiêng về.",
                    $"Nếu cần một lựa chọn nổi bật hơn trong nhóm đang cân nhắc thì **{top.Ten}** là mẫu sáng hơn một chút.");
            }

            return null;
        }

    }
}
