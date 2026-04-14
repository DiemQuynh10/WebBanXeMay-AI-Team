using System;
using System.Diagnostics;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Conversation;
using Chatbot.API.Services.Interfaces;

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
        private readonly IConversationContextResolver _conversationContextResolver;
        private readonly IChatFlowRouter _chatFlowRouter;
        private readonly IFlowDecisionService _flowDecisionService;
        private readonly IProductLookupFlowService _productLookupFlowService;
        private readonly IProductSearchFlowService _productSearchFlowService;
        private readonly IRefinementService _refinementService;
        private readonly ICompareService _compareService;
        private readonly IRecommendationFlowService _recommendationFlowService;
        public ChatFlowOrchestrator(
    ILogger<ChatFlowOrchestrator> logger,
    IClarificationStateService clarificationStateService,
    IQueryNormalizationService queryNormalizationService,
    IConversationPreferenceService conversationPreferenceService,
    IPriceIntentParser priceIntentParser,
    IIntentParserService intentParserService,
    ILLMIntentUnderstandingService llmIntentUnderstandingService,
    IConversationContextResolver conversationContextResolver,
    IChatFlowRouter chatFlowRouter,
    IFlowDecisionService flowDecisionService,
    IProductLookupFlowService productLookupFlowService,
    IProductSearchFlowService productSearchFlowService,
    IRefinementService refinementService,
    ICompareService compareService,
    IRecommendationFlowService recommendationFlowService)
        {
            _logger = logger;
            _clarificationStateService = clarificationStateService;
            _queryNormalizationService = queryNormalizationService;
            _conversationPreferenceService = conversationPreferenceService;
            _priceIntentParser = priceIntentParser;
            _intentParserService = intentParserService;
            _llmIntentUnderstandingService = llmIntentUnderstandingService;
            _conversationContextResolver = conversationContextResolver;
            _chatFlowRouter = chatFlowRouter;
            _flowDecisionService = flowDecisionService;
            _productLookupFlowService = productLookupFlowService;
            _productSearchFlowService = productSearchFlowService;
            _refinementService = refinementService;
            _compareService = compareService;
            _recommendationFlowService = recommendationFlowService;
        }

        public async Task<ChatResponse> HandleAsync(ChatRequest request)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var context = await BuildContextAsync(request);

                var response = await ExecuteFlowAsync(context);

                response.ConversationId ??= context.ConversationId;
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;

                return response;
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
            var existingProfile = await _conversationPreferenceService.GetAsync(conversationId);

            var normalizationResult = _queryNormalizationService.Analyze(originalMessage);
            var normalizedMessage = normalizationResult.NormalizedText;

            var parsedIntent = await _intentParserService.ParseAsync(normalizedMessage);

            var priceIntent = _priceIntentParser.Parse(normalizedMessage);
            parsedIntent.PriceMin = priceIntent.MinPrice ?? parsedIntent.PriceMin;
            parsedIntent.PriceMax = priceIntent.MaxPrice ?? parsedIntent.PriceMax;
            parsedIntent.TargetPrice = priceIntent.TargetPrice ?? parsedIntent.TargetPrice;
            parsedIntent.FilterType = priceIntent.FilterType;
            bool isHardFilterOnlySearch =
    FlowIntentHeuristics.IsHardFilterOnlySearch(parsedIntent, normalizedMessage);
            bool shouldCallLlmIntent =
    !isHardFilterOnlySearch &&
    (
        parsedIntent.IntentType == "unknown" ||
        normalizationResult.NeedsConfirmation ||
        (
            parsedIntent.IntentType == "recommend" &&
            string.IsNullOrWhiteSpace(parsedIntent.Target) &&
            !parsedIntent.ForWork &&
            !parsedIntent.ForSchool &&
            !parsedIntent.ForCity &&
            !parsedIntent.ForTour &&
            !parsedIntent.WantsFuelSaving &&
            !parsedIntent.WantsLargeStorage &&
            !parsedIntent.WantsEasyControl &&
            !parsedIntent.NeedsLowSeat
        )
    );
            if (shouldCallLlmIntent)
            {
                var llmIntent = await _llmIntentUnderstandingService.UnderstandAsync(
                    normalizedMessage,
                    existingProfile);

                if (llmIntent != null &&
                    !string.IsNullOrWhiteSpace(llmIntent.IntentType) &&
                    llmIntent.IntentType != "unknown" &&
                    llmIntent.Confidence >= 0.85)
                {
                    parsedIntent.IntentType = llmIntent.IntentType;
                }
            }

            var contextResolution = _conversationContextResolver.Resolve(
     normalizedMessage,
     parsedIntent,
     existingProfile,
     existingProfile.ActiveFlow);

            var effectiveIntent = contextResolution.EffectiveIntent;
            var contextDecision = contextResolution.ContextDecision;

            if (LooksLikeExplicitFreshRecommendationRequest(normalizedMessage, parsedIntent))
            {
                contextDecision = RecommendationContextDecision.StartFreshRecommendation;
                effectiveIntent = SanitizeFreshRecommendationIntent(parsedIntent, effectiveIntent);

                _logger.LogInformation(
                    "Force context decision to StartFreshRecommendation. ConversationId={ConversationId}, Message={Message}, Brand={Brand}, Category={Category}, FilterType={FilterType}",
                    conversationId,
                    normalizedMessage,
                    parsedIntent.Brand,
                    parsedIntent.Category,
                    parsedIntent.FilterType);
            }
            else if (ShouldOverrideToExpandFromCurrentGoal(
                        normalizedMessage,
                        parsedIntent,
                        existingProfile,
                        contextDecision))
            {
                contextDecision = RecommendationContextDecision.ExpandFromCurrentGoal;

                if (existingProfile != null)
                {
                    effectiveIntent = MergeExpandFollowUpIntent(
                        parsedIntent,
                        effectiveIntent,
                        existingProfile);
                }

                _logger.LogInformation(
                    "Override context decision to ExpandFromCurrentGoal. ConversationId={ConversationId}, Message={Message}, Category={Category}, Brand={Brand}",
                    conversationId,
                    normalizedMessage,
                    parsedIntent.Category,
                    parsedIntent.Brand);
            }
            else if (contextDecision == RecommendationContextDecision.StartFreshRecommendation)
            {
                effectiveIntent = SanitizeFreshRecommendationIntent(parsedIntent, effectiveIntent);
            }

            var isFreshRecommendation =
    contextDecision == RecommendationContextDecision.StartFreshRecommendation;
            ApplyCurrentTurnPriceOverride(parsedIntent, effectiveIntent);
            var mergedProfile = await _conversationPreferenceService.MergeAsync(
                conversationId,
                effectiveIntent,
                isFreshRecommendation);

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

            _logger.LogInformation(
                "ConversationId={ConversationId} | ParsedIntent: IntentType={IntentType}, Brand={Brand}, Category={Category}, Target={Target}, PriceMin={PriceMin}, PriceMax={PriceMax}, TargetPrice={TargetPrice}, FilterType={FilterType} | EffectiveIntent: Brand={EffectiveBrand}, Category={EffectiveCategory}, Target={EffectiveTarget}, PriceMin={EffectivePriceMin}, PriceMax={EffectivePriceMax}, TargetPrice={EffectiveTargetPrice}, FilterType={EffectiveFilterType} | ContextDecision={ContextDecision} | FinalFlow={FinalFlow}",
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
                contextDecision,
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
            return new ChatOrchestrationContext
            {
                Request = request,
                ConversationId = conversationId,
                OriginalMessage = originalMessage,
                NormalizedMessage = normalizedMessage,
                ExistingProfile = mergedProfile,
                ParsedIntent = parsedIntent,
                EffectiveIntent = effectiveIntent,
                BaseRouting = baseRouting,
                FinalRouting = finalRouting
            };
        }
        private async Task<ChatResponse> ExecuteFlowAsync(ChatOrchestrationContext context)
        {
            var flowType = context.FinalRouting.FlowType;

            if (ShouldForceCompareFollowUp(
                context.NormalizedMessage,
                context.EffectiveIntent,
                context.ExistingProfile))
            {
                _logger.LogInformation(
                    "Force compare follow-up in orchestrator. ConversationId={ConversationId}, Message={Message}",
                    context.ConversationId,
                    context.NormalizedMessage);

                var forcedCompare = await _compareService.CompareAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);

                if (forcedCompare != null)
                    return forcedCompare;
            }

            if (string.Equals(flowType, ChatFlowType.Greeting, StringComparison.OrdinalIgnoreCase))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    Reply = "Xin chào 👋 Mình có thể hỗ trợ bạn tra cứu giá xe, kiểm tra tồn kho, tư vấn mẫu xe phù hợp hoặc tra cứu đơn hàng."
                };
            }

            if (string.Equals(flowType, ChatFlowType.OutOfScope, StringComparison.OrdinalIgnoreCase))
            {
                return new ChatResponse
                {
                    Success = true,
                    UsedAI = false,
                    Reply = "Mình hiện chỉ hỗ trợ về xe máy, sản phẩm trong hệ thống và tra cứu đơn hàng. Bạn cứ hỏi mình về mẫu xe, giá, còn hàng hay tư vấn chọn xe nhé."
                };
            }

            if (string.Equals(flowType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase))
            {
                var result = await _productLookupFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);

                if (result != null)
                    return result;
            }

            if (string.Equals(flowType, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Executing PRODUCT_SEARCH flow");
                var result = await _productSearchFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);

                if (result != null)
                    return result;
            }

            if (string.Equals(flowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase))
            {
                var result = await _refinementService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);

                if (result != null)
                    return result;
            }

            if (string.Equals(flowType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase))
            {
                var result = await _compareService.CompareAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);

                if (result != null)
                    return result;
            }

            if (string.Equals(flowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Executing RECOMMENDATION flow");
                var result = await _recommendationFlowService.HandleAsync(
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);

                if (result != null)
                    return result;
            }

            _logger.LogWarning(
    "No flow returned a result. ConversationId={ConversationId}, FlowType={FlowType}, Message={Message}",
    context.ConversationId,
    flowType,
    context.NormalizedMessage);

            return new ChatResponse
            {
                Success = true,
                UsedAI = false,
                Reply = "Mình chưa xử lý trọn vẹn câu này theo ngữ cảnh hiện tại. Bạn thử nói rõ hơn một chút như tên xe đang so sánh, hãng muốn lọc hoặc tiêu chí muốn ưu tiên nhé."
            };
        }
        private static bool ShouldForceCompareFollowUp(
    string normalizedMessage,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage) || profile == null)
                return false;

            if (!profile.HasActiveCompareContext || profile.LastComparedProducts == null || profile.LastComparedProducts.Count < 2)
                return false;

            var text = normalizedMessage.Trim().ToLowerInvariant();

            bool mentionsLessThanTwoNewProducts =
                effectiveIntent.MentionedProducts == null ||
                effectiveIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() < 2;

            bool looksLikeCompareFollowUp =
                !string.IsNullOrWhiteSpace(effectiveIntent.ComparisonFeature) ||
                text.Contains("cốp rộng") ||
                text.Contains("cop rong") ||
                text.Contains("dễ chống chân") ||
                text.Contains("de chong chan") ||
                text.Contains("tiết kiệm xăng") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("đẹp hơn") ||
                text.Contains("dep hon") ||
                text.Contains("thanh lịch hơn") ||
                text.Contains("thanh lich hon") ||
                text.Contains("êm hơn") ||
                text.Contains("em hon") ||
                text.Contains("hợp nữ") ||
                text.Contains("hop nu") ||
                text == "giá bao nhiêu" ||
                text == "gia bao nhieu" ||
                text == "bao nhiêu" ||
                text == "bao nhieu" ||
                text == "mức giá";

            return mentionsLessThanTwoNewProducts && looksLikeCompareFollowUp;
        }
        private static bool ShouldOverrideToExpandFromCurrentGoal(
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? existingProfile,
    RecommendationContextDecision currentDecision)
        {
            if (currentDecision != RecommendationContextDecision.StartFreshRecommendation)
                return false;

            if (existingProfile?.HasActiveRecommendationContext != true ||
                existingProfile.LastRecommendedProducts == null ||
                existingProfile.LastRecommendedProducts.Count == 0)
            {
                return false;
            }

            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();

            bool hasNewStructuredFilter =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.IsBrandSwitch ||
                parsedIntent.IntentType == "brand_switch";

            if (!hasNewStructuredFilter)
                return false;

            bool introducesNewGoal =
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.NeedsLowSeat;

            // Nếu user chỉ đổi hãng / loại / giá nhưng không tạo goal mới,
            // thì phải expand từ goal hiện tại chứ không reset.
            if (!introducesNewGoal)
                return true;

            bool looksLikeShortFollowUp =
                text.Contains("thì sao") ||
                text.Contains("thi sao") ||
                text.Contains("còn") ||
                text.Contains("con ") ||
                text.Contains("đổi sang") ||
                text.Contains("doi sang");

            return looksLikeShortFollowUp;
        }
        private static ParsedIntent MergeExpandFollowUpIntent(
    ParsedIntent parsedIntent,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile)
        {
            var merged = effectiveIntent.Clone();

            if (string.IsNullOrWhiteSpace(merged.Target))
                merged.Target = profile.Target;

            if (!merged.ForWork)
                merged.ForWork = profile.ForWork;

            if (!merged.ForSchool)
                merged.ForSchool = profile.ForSchool;

            if (!merged.ForCity)
                merged.ForCity = profile.ForCity;

            if (!merged.ForTour)
                merged.ForTour = profile.ForTour;

            if (!merged.PriceMin.HasValue)
                merged.PriceMin = profile.PriceMin;

            if (!merged.PriceMax.HasValue)
                merged.PriceMax = profile.PriceMax;

            if (!merged.TargetPrice.HasValue)
                merged.TargetPrice = profile.TargetPrice;

            if (merged.FilterType == PriceFilterType.None)
                merged.FilterType = profile.FilterType;

            if (!merged.WantsFuelSaving)
                merged.WantsFuelSaving = profile.WantsFuelSaving;

            if (!merged.WantsLargeStorage)
                merged.WantsLargeStorage = profile.WantsLargeStorage;

            if (!merged.WantsEasyControl)
                merged.WantsEasyControl = profile.WantsEasyControl;

            if (!merged.NeedsLowSeat)
                merged.NeedsLowSeat = profile.NeedsLowSeat;

            if (!merged.HeightCm.HasValue)
                merged.HeightCm = profile.HeightCm;

            if (!merged.PrefersMaleStyle)
                merged.PrefersMaleStyle = profile.PrefersMaleStyle;

            if (!merged.PrefersFemaleStyle)
                merged.PrefersFemaleStyle = profile.PrefersFemaleStyle;

            return merged;
        }
        private static ParsedIntent SanitizeFreshRecommendationIntent(
    ParsedIntent parsedIntent,
    ParsedIntent effectiveIntent)
        {
            var clean = effectiveIntent.Clone();

            // Chỉ giữ lại các tín hiệu user thật sự nói ở turn hiện tại
            clean.Brand = parsedIntent.Brand;
            clean.Category = parsedIntent.Category;
            clean.Target = parsedIntent.Target;

            clean.PriceMin = parsedIntent.PriceMin;
            clean.PriceMax = parsedIntent.PriceMax;
            clean.TargetPrice = parsedIntent.TargetPrice;
            clean.FilterType = parsedIntent.FilterType;

            clean.ForWork = parsedIntent.ForWork;
            clean.ForSchool = parsedIntent.ForSchool;
            clean.ForCity = parsedIntent.ForCity;
            clean.ForTour = parsedIntent.ForTour;

            clean.WantsFuelSaving = parsedIntent.WantsFuelSaving;
            clean.WantsLargeStorage = parsedIntent.WantsLargeStorage;
            clean.WantsEasyControl = parsedIntent.WantsEasyControl;
            clean.NeedsLowSeat = parsedIntent.NeedsLowSeat;

            clean.HeightCm = parsedIntent.HeightCm;

            clean.PrefersMaleStyle = parsedIntent.PrefersMaleStyle;
            clean.PrefersFemaleStyle = parsedIntent.PrefersFemaleStyle;

            clean.ExcludedBrands = new HashSet<string>(
                parsedIntent.ExcludedBrands,
                StringComparer.OrdinalIgnoreCase);

            clean.ExcludedCategories = new HashSet<string>(
                parsedIntent.ExcludedCategories,
                StringComparer.OrdinalIgnoreCase);

            clean.RequestedStyles = new HashSet<string>(
                parsedIntent.RequestedStyles,
                StringComparer.OrdinalIgnoreCase);

            clean.MentionedProducts = new List<string>(parsedIntent.MentionedProducts);
            clean.ComparisonFeature = parsedIntent.ComparisonFeature;

            return clean;
        }
        private static void ApplyCurrentTurnPriceOverride(
    ParsedIntent parsedIntent,
    ParsedIntent effectiveIntent)
        {
            if (parsedIntent == null || effectiveIntent == null)
                return;

            bool hasExplicitPrice =
                parsedIntent.FilterType != PriceFilterType.None ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue;

            if (!hasExplicitPrice)
                return;

            effectiveIntent.FilterType = parsedIntent.FilterType;

            switch (parsedIntent.FilterType)
            {
                case PriceFilterType.MaxOnly:
                    effectiveIntent.PriceMin = null;
                    effectiveIntent.PriceMax = parsedIntent.PriceMax;
                    effectiveIntent.TargetPrice = null;
                    break;

                case PriceFilterType.MinOnly:
                    effectiveIntent.PriceMin = parsedIntent.PriceMin;
                    effectiveIntent.PriceMax = null;
                    effectiveIntent.TargetPrice = null;
                    break;

                case PriceFilterType.Range:
                    effectiveIntent.PriceMin = parsedIntent.PriceMin;
                    effectiveIntent.PriceMax = parsedIntent.PriceMax;
                    effectiveIntent.TargetPrice = null;
                    break;

                case PriceFilterType.Around:
                    effectiveIntent.PriceMin = parsedIntent.PriceMin;
                    effectiveIntent.PriceMax = parsedIntent.PriceMax;
                    effectiveIntent.TargetPrice = parsedIntent.TargetPrice;
                    break;

                default:
                    // fallback an toàn
                    effectiveIntent.PriceMin = parsedIntent.PriceMin;
                    effectiveIntent.PriceMax = parsedIntent.PriceMax;
                    effectiveIntent.TargetPrice = parsedIntent.TargetPrice;
                    break;
            }

            // Chặn trường hợp min > max do merge cũ còn sót
            if (effectiveIntent.PriceMin.HasValue &&
                effectiveIntent.PriceMax.HasValue &&
                effectiveIntent.PriceMin.Value > effectiveIntent.PriceMax.Value)
            {
                if (parsedIntent.FilterType == PriceFilterType.MaxOnly)
                {
                    effectiveIntent.PriceMin = null;
                }
                else if (parsedIntent.FilterType == PriceFilterType.MinOnly)
                {
                    effectiveIntent.PriceMax = null;
                }
            }
        }
        private static bool LooksLikeExplicitFreshRecommendationRequest(
    string normalizedMessage,
    ParsedIntent parsedIntent)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            bool isShortRefineFollowUp =
    text == "xe ga thôi" ||
    text == "xe ga thoi" ||
    text == "xe số thôi" ||
    text == "xe so thoi" ||
    text == "côn tay thôi" ||
    text == "con tay thoi" ||
    text.StartsWith("bỏ ") ||
    text.StartsWith("bo ") ||
    text.StartsWith("không lấy ") ||
    text.StartsWith("khong lay ") ||
    text.StartsWith("loại ") ||
    text.StartsWith("loai ") ||
    text.StartsWith("dưới ") ||
    text.StartsWith("duoi ") ||
    text.StartsWith("trên ") ||
    text.StartsWith("tren ");

            if (isShortRefineFollowUp)
                return false;
            bool hasRecommendationVerb =
    text.Contains("tư vấn") ||
    text.Contains("tu van") ||
    text.Contains("từ vấn") ||   
    text.Contains("tuvấn") ||
    text.Contains("gợi ý") ||
    text.Contains("goi y") ||
    text.Contains("gợi") ||
    text.StartsWith("xe ") ||
    text.Contains("xe ga") ||
    text.Contains("xe số") ||
    text.Contains("xe so") ||
    text.Contains("côn tay") ||
    text.Contains("con tay");

            bool hasFreshConstraint =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.FilterType != PriceFilterType.None;
            bool hasStrongFreshPattern =
    (!string.IsNullOrWhiteSpace(parsedIntent.Brand) && !string.IsNullOrWhiteSpace(parsedIntent.Category)) ||
    (!string.IsNullOrWhiteSpace(parsedIntent.Brand) && parsedIntent.FilterType != PriceFilterType.None) ||
    (!string.IsNullOrWhiteSpace(parsedIntent.Category) && parsedIntent.FilterType != PriceFilterType.None);
            bool isClassicShortFollowUp =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.EndsWith("thì sao") ||
                text.EndsWith("thi sao") ||
                text == "xe ga thì sao" ||
                text == "xe số thì sao" ||
                text == "xe so thi sao";

            return (hasRecommendationVerb && hasFreshConstraint && !isClassicShortFollowUp)
       || hasStrongFreshPattern;
        }
    }
}