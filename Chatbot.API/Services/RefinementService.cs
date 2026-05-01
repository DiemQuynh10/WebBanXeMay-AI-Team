using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using System.Text;

namespace Chatbot.API.Services
{
    public class RefinementService : IRefinementService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly ILogger<RefinementService> _logger;
        private readonly IRecommendationLLMService _recommendationLLMService;
        private readonly IReplyStyleService _replyStyleService;
        private readonly IReplyRewriteService _replyRewriteService;

        public RefinementService(
            IWebBanXeMayToolClient toolClient,
            IProductRecommendationService productRecommendationService,
            IConversationPreferenceService conversationPreferenceService,
            IRecommendationLLMService recommendationLLMService,
            IReplyStyleService replyStyleService,
            IReplyRewriteService replyRewriteService,
            ILogger<RefinementService> logger)
        {
            _toolClient = toolClient;
            _productRecommendationService = productRecommendationService;
            _conversationPreferenceService = conversationPreferenceService;
            _recommendationLLMService = recommendationLLMService;
            _replyStyleService = replyStyleService;
            _replyRewriteService = replyRewriteService;
            _logger = logger;
        }

        private async Task<List<ProductSummaryDto>> LoadPreviousProductsAsync(HashSet<string> allowedNames)
        {
            var previousProducts = new List<ProductSummaryDto>();

            foreach (var name in allowedNames)
            {
                var result = await _toolClient.SearchProductsAsync(name, 3);

                var matched = result?.Items?
                    .OrderByDescending(x => string.Equals(x.Ten, name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => (x.Ten ?? string.Empty).Contains(name, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (matched != null)
                {
                    previousProducts.Add(matched);
                }
            }

            return previousProducts
                .Where(x => !string.IsNullOrWhiteSpace(x.Ten) && allowedNames.Contains(x.Ten))
                .GroupBy(x => x.Ten, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            _logger.LogWarning(
    "ENTER REFINEMENT => ConversationId={ConversationId}, Message={Message}",
    conversationId,
    normalizedMessage);
            if (LooksLikeLookupReferenceFollowUp(normalizedMessage) &&
    string.Equals(profile.ActiveFlow, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) &&
    !string.IsNullOrWhiteSpace(profile.LastLookupProductName))
            {
                _logger.LogInformation(
                    "Refinement bypassed because message looks like lookup reference follow-up. ConversationId={ConversationId}, LastLookupProductName={LastLookupProductName}",
                    conversationId,
                    profile.LastLookupProductName);

                return null;
            }
            if (profile.HasActiveCompareContext && profile.LastComparedProducts.Count >= 2)
            {
                return null;
            }

            var activeRecommendedProducts =
     profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0
         ? profile.CurrentRecommendedProducts
         : profile.LastRecommendedProducts != null && profile.LastRecommendedProducts.Count > 0
             ? profile.LastRecommendedProducts
             : profile.BaseRecommendedProducts;

            if (!profile.HasActiveRecommendationContext ||
                activeRecommendedProducts == null ||
                activeRecommendedProducts.Count == 0)
            {
                return null;
            }

            var allowedNames = activeRecommendedProducts
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var previousProducts = await LoadPreviousProductsAsync(allowedNames);
            if (previousProducts.Count == 0)
            {
                return null;
            }
            _logger.LogWarning(
    "REFINEMENT PREVIOUS PRODUCTS => Count={Count}, Products={Products}",
    previousProducts.Count,
    string.Join(" | ", previousProducts.Select(x => $"{x.Ten}:{x.Gia:N0}")));

            EnrichIntentFromFollowUp(normalizedMessage, intent);

            var currentTurnHasCategory = CurrentTurnHasExplicitCategory(normalizedMessage);
            var currentTurnHasBrand = CurrentTurnHasExplicitBrand(normalizedMessage);
            var currentTurnHasPrice = CurrentTurnHasExplicitPrice(normalizedMessage);

            if (currentTurnHasCategory && !currentTurnHasBrand)
            {
                intent.Brand = null;
                profile.PreferredBrand = null;
            }

            if (currentTurnHasCategory && !currentTurnHasPrice)
            {
                intent.PriceMin = null;
                intent.PriceMax = null;
                intent.TargetPrice = null;
                intent.FilterType = PriceFilterType.None;

                profile.PriceMin = null;
                profile.PriceMax = null;
                profile.TargetPrice = null;
                profile.FilterType = PriceFilterType.None;
            }

            NormalizeRefinementPriceConstraints(intent);
            var signals = AnalyzeRefinementSignals(normalizedMessage, intent, profile);
            var hasHardFilterChange = signals.HasHardFilterChange;
            var hasSoftPreferenceChange = signals.HasSoftPreferenceChange;
            _logger.LogWarning(
    "REFINEMENT SIGNALS => PreferCheaper={PreferCheaper}, PreferMoreExpensive={PreferMoreExpensive}, HasSoftPreferenceChange={HasSoftPreferenceChange}, HasHardFilterChange={HasHardFilterChange}, Brand={Brand}, Category={Category}, PriceMin={PriceMin}, PriceMax={PriceMax}, TargetPrice={TargetPrice}",
    signals.PreferCheaper,
    signals.PreferMoreExpensive,
    signals.HasSoftPreferenceChange,
    signals.HasHardFilterChange,
    intent.Brand,
    intent.Category,
    intent.PriceMin,
    intent.PriceMax,
    intent.TargetPrice);
            _logger.LogInformation(
    "Refinement entry. ConversationId={ConversationId}, Message={Message}, Brand={Brand}, Category={Category}, ExcludedProducts={ExcludedProducts}, ExcludedCategories={ExcludedCategories}, ExcludedBrands={ExcludedBrands}, PriceMin={PriceMin}, PriceMax={PriceMax}, TargetPrice={TargetPrice}, FilterType={FilterType}, HardChange={HardChange}, SoftChange={SoftChange}, PreferCheaper={PreferCheaper}, PreferDifferent={PreferDifferent}, WantsBroader={WantsBroader}",
    conversationId,
    normalizedMessage,
    intent.Brand,
    intent.Category,
    string.Join(",", intent.ExcludedProducts),
    string.Join(",", intent.ExcludedCategories),
    string.Join(",", intent.ExcludedBrands),
    intent.PriceMin,
    intent.PriceMax,
    intent.TargetPrice,
    intent.FilterType,
    hasHardFilterChange,
    hasSoftPreferenceChange,
    signals.PreferCheaper,
    signals.PreferDifferent,
    signals.WantsBroaderAlternatives);

            foreach (var p in intent.ExcludedProducts)
            {
                if (!profile.ExcludedProducts.Contains(p))
                    profile.ExcludedProducts.Add(p);
            }

            foreach (var b in intent.ExcludedBrands)
            {
                if (!profile.ExcludedBrands.Contains(b))
                    profile.ExcludedBrands.Add(b);
            }

            foreach (var c in intent.ExcludedCategories)
            {
                if (!profile.ExcludedCategories.Contains(c))
                    profile.ExcludedCategories.Add(c);
            }

            if (hasHardFilterChange)
            {
                return await HandleHardRefinementAsync(conversationId, normalizedMessage, intent, profile, previousProducts, signals);
            }

            if (hasSoftPreferenceChange)
            {
                return await HandleSoftRefinementAsync(conversationId, normalizedMessage, intent, profile, previousProducts, signals);
            }

            return await HandleLightweightRefinementAsync(conversationId, normalizedMessage, intent, profile, previousProducts, signals);
        }

        private async Task<ChatResponse> HandleHardRefinementAsync(
     string conversationId,
     string normalizedMessage,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     List<ProductSummaryDto> previousProducts,
     RefinementSignals signals)
        {
            var (minPrice, maxPrice) = BuildHardRefinementPriceWindow(intent);

            var items = await FetchHardRefinementCandidatesAsync(intent, minPrice, maxPrice);
            items = ApplyIntentFilters(items, intent);
            items = ApplyAllExclusions(items, intent, profile);
            items = ApplySemanticRefinementOrdering(items, signals, profile, intent, normalizedMessage, previousProducts);
            items = ApplyPracticalUnderboneGuard(items, intent, normalizedMessage);

            if (items.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(intent.Brand))
                {
                    var relaxedResponse = await TryBrandRelaxationAsync(
                    conversationId,
                        normalizedMessage,
                        intent,
                        profile,
                        maxPrice);

                    if (relaxedResponse != null)
                        return relaxedResponse;
                }

                return await BuildNoMatchResponseAsync(conversationId, normalizedMessage, intent, profile);
            }

            var rankedHard = _productRecommendationService.RankProducts(
                items,
                intent,
                profile,
                normalizedMessage,
                take: Math.Min(4, items.Count));

            _logger.LogInformation(
                "Hard refinement rule ranking completed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, RankedHardCount={RankedHardCount}",
                conversationId,
                items.Count,
                rankedHard.Count);

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                rankedHard,
                "refine");

            var hardDraftReply = _replyStyleService.BuildRefinementReply(
                rankedHard,
                intent,
                normalizedMessage,
                item => BuildSimpleRefineReason(item, intent, signals));

            var hardReply = await _replyRewriteService.RewriteAsync(normalizedMessage, hardDraftReply);

            _logger.LogInformation(
                "Hard refinement final result. ConversationId={ConversationId}, FinalProductCount={FinalProductCount}",
                conversationId,
                rankedHard.Count);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = hardReply,
                Products = ChatProductCardMapper.MapMany(rankedHard, 4)
            };
        }
        private static bool LooksLikeFreshTargetRecommendation(string message, ParsedIntent intent)
        {
            var text = NormalizeText(message);

            bool hasExplicitCategoryInCurrentMessage =
    text.Contains("xe ga") ||
    text.Contains("xe so") ||
    text.Contains("xe số") ||
    text.Contains("tay ga") ||
    text.Contains("con tay") ||
    text.Contains("côn tay");

            if (hasExplicitCategoryInCurrentMessage)
                return false;

            bool hasTargetSignal =
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.PrefersFemaleStyle ||
                intent.PrefersMaleStyle ||
                text.Contains("cho nu") ||
                text.Contains("cho nữ") ||
                text.Contains("xe nu") ||
                text.Contains("xe nữ") ||
                text.Contains("hop nu") ||
                text.Contains("hợp nữ") ||
                text.Contains("nu tinh") ||
                text.Contains("nữ tính") ||
                text.Contains("cho nam") ||
                text.Contains("xe nam") ||
                text.Contains("hop nam") ||
                text.Contains("hợp nam") ||
                text.Contains("nam tinh");

            if (!hasTargetSignal)
                return false;

            bool looksLikeFreshAsk =
                text.Contains("tu van") ||
                text.Contains("tư vấn") ||
                text.Contains("goi y") ||
                text.Contains("gợi ý") ||
                text.Contains("xe cho") ||
                text.Contains("hop") ||
                text.Contains("hợp");

            return looksLikeFreshAsk;
        }
        private static bool LooksLikeLookupReferenceFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = NormalizeText(message);

            return text.Contains("mau do") ||
                   text.Contains("con do") ||
                   text.Contains("xe do") ||
                   text.Contains("mau kia") ||
                   text.Contains("con kia") ||
                   text.Contains("xe kia") ||
                   text.Contains("dau tien") ||
                   text.Contains("thu 2") ||
                   text.Contains("thu hai");
        }
        private async Task<ChatResponse> HandleSoftRefinementAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    List<ProductSummaryDto> previousProducts,
    RefinementSignals signals)
        {
            var currentProducts = previousProducts.ToList();
            var currentBand = BuildCurrentResultPriceBand(currentProducts);

            var contextFilteredCurrent = ApplySoftContextFilters(
                currentProducts.ToList(),
                intent,
                profile);

            if (contextFilteredCurrent.Count == 0)
            {
                bool hasExclusion =
                    intent.ExcludedBrands.Any() ||
                    intent.ExcludedProducts.Any() ||
                    intent.ExcludedCategories.Any() ||
                    profile.ExcludedBrands.Any() ||
                    profile.ExcludedProducts.Any() ||
                    profile.ExcludedCategories.Any();

                if (hasExclusion)
                {
                    contextFilteredCurrent = new List<ProductSummaryDto>();
                }
                else
                {
                    contextFilteredCurrent = currentProducts.ToList();
                }
            }
            var candidateProducts = contextFilteredCurrent;

            var isRelativePriceRefinement = IsRelativePriceRefinement(signals);
            var relativePriceAnchor = ResolveRelativePriceAnchor(intent, profile, currentBand);
            var shouldRefetchBroader =
     signals.WantsBroaderAlternatives ||
     signals.PreferDifferent ||
     string.Equals(intent.ComparisonFeature, "alternative", StringComparison.OrdinalIgnoreCase) ||
     isRelativePriceRefinement ||
     ShouldRefetchBroaderForSoftRefine(candidateProducts, intent);

            if (shouldRefetchBroader)
            {
                if (isRelativePriceRefinement)
                {
                    var relativeCandidates = await FetchRelativePriceCandidatesAsync(
                        intent,
                        profile,
                        currentBand,
                        signals);
                    _logger.LogWarning(
    "RELATIVE FETCH => Count={Count}, Products={Products}",
    relativeCandidates.Count,
    string.Join(" | ", relativeCandidates.Select(x => $"{x.Ten}:{x.Gia:N0}")));
                    var filteredRelative = ApplySoftContextFilters(relativeCandidates, intent, profile);

                    if (filteredRelative.Count > 0)
                    {
                        candidateProducts = filteredRelative;
                    }
                    else
                    {
                        candidateProducts = relativeCandidates;
                    }

                    _logger.LogInformation(
     "Relative price refinement fetch executed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, PreferCheaper={PreferCheaper}, PreferMoreExpensive={PreferMoreExpensive}, Anchor={Anchor}, CurrentMin={CurrentMin}, CurrentMax={CurrentMax}",
     conversationId,
     candidateProducts.Count,
     signals.PreferCheaper,
     signals.PreferMoreExpensive,
     relativePriceAnchor,
     currentBand.MinPrice,
     currentBand.MaxPrice);
                }
                else
                {
                    var (minPrice, maxPrice, brand, category) = BuildSoftRefinementBaseFilters(intent, profile);

                    var broaderCandidates = await FetchBroaderSoftCandidatesAsync(
    intent,
    profile,
    minPrice,
    maxPrice,
    brand,
    category);
                    if (broaderCandidates.Count > 0)
                    {
                        candidateProducts = ApplySoftContextFilters(broaderCandidates, intent, profile);

                        if (candidateProducts.Count == 0)
                        {
                            candidateProducts = broaderCandidates;
                        }
                    }
                    if (signals.PreferDifferent ||
     string.Equals(intent.ComparisonFeature, "alternative", StringComparison.OrdinalIgnoreCase))
                    {
                        var previousNames = profile.CurrentRecommendedProducts != null && profile.CurrentRecommendedProducts.Count > 0
                            ? profile.CurrentRecommendedProducts
                            : profile.LastRecommendedProducts ?? new List<string>();

                        var antiRepeat = candidateProducts
                            .Where(p => !previousNames.Contains(p.Ten, StringComparer.OrdinalIgnoreCase))
                            .ToList();

                        if (antiRepeat.Count > 0)
                        {
                            candidateProducts = antiRepeat;
                        }
                        else
                        {
                            var topName = previousNames.FirstOrDefault();

                            var softAntiRepeat = candidateProducts
                                .Where(p => !string.Equals(p.Ten, topName, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (softAntiRepeat.Count > 0)
                            {
                                candidateProducts = softAntiRepeat;
                            }
                        }
                    }

                    _logger.LogInformation(
                        "Soft refinement broader refetch executed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, Brand={Brand}, Category={Category}, MinPrice={MinPrice}, MaxPrice={MaxPrice}",
                        conversationId,
                        candidateProducts.Count,
                        brand,
                        category,
                        minPrice,
                        maxPrice);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Soft refinement using current set first. ConversationId={ConversationId}, CandidateCount={CandidateCount}, PreferCheaper={PreferCheaper}, PreferMoreExpensive={PreferMoreExpensive}",
                    conversationId,
                    candidateProducts.Count,
                    signals.PreferCheaper,
                    signals.PreferMoreExpensive);
            }


            if (!isRelativePriceRefinement)
            {
                candidateProducts = ApplySoftPriceGuard(candidateProducts, previousProducts, profile);
            }

            if (isRelativePriceRefinement)
            {
                candidateProducts = ApplyRelativePriceRefinement(
    candidateProducts,
    relativePriceAnchor,
    currentBand,
    signals);

                candidateProducts = ApplyFemaleFriendlyRelativeGuard(
                    candidateProducts,
                    profile,
                    normalizedMessage,
                    previousProducts,
                    signals);

                _logger.LogWarning(
                    "RELATIVE FILTER RESULT => Anchor={Anchor}, Count={Count}, Products={Products}",
                    relativePriceAnchor,
                    candidateProducts.Count,
                    string.Join(" | ", candidateProducts.Select(x => $"{x.Ten}:{x.Gia:N0}")));
                if (candidateProducts.Count == 0)
                {
                    return await BuildRelativePriceNoMatchResponseAsync(
    conversationId,
    normalizedMessage,
    intent,
    profile,
    relativePriceAnchor,
    currentBand,
    signals);
                }

                var rankedRelative = candidateProducts
                    .Take(Math.Min(4, candidateProducts.Count))
                    .ToList();

                await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                    conversationId,
                    rankedRelative,
                    "refine");

                var draftReply = _replyStyleService.BuildRefinementReply(
                    rankedRelative,
                    intent,
                    normalizedMessage,
                    item => BuildSimpleRefineReason(item, intent, signals));

                var reply = await _replyRewriteService.RewriteAsync(normalizedMessage, draftReply);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = reply,
                    Products = ChatProductCardMapper.MapMany(rankedRelative, 4)
                };
            }

            candidateProducts = ApplySemanticRefinementOrdering(
    candidateProducts,
    signals,
    profile,
    intent,
    normalizedMessage,
    previousProducts);

            candidateProducts = ApplyPracticalUnderboneGuard(
                candidateProducts,
                intent,
                normalizedMessage);


            var isDecideBest =
     string.Equals(intent.ComparisonFeature, "decide_best", StringComparison.OrdinalIgnoreCase);

            var rankedSoftByRule = _productRecommendationService.RankProducts(
                candidateProducts,
                intent,
                profile,
                normalizedMessage,
                take: isDecideBest
                    ? Math.Min(2, candidateProducts.Count)
                    : Math.Min(5, candidateProducts.Count));

            if (rankedSoftByRule == null || rankedSoftByRule.Count == 0)
            {
                return await BuildNoMatchResponseAsync(conversationId, normalizedMessage, intent, profile);
            }

            _logger.LogInformation(
                "Soft refinement rule ranking completed. ConversationId={ConversationId}, CandidateCount={CandidateCount}, RankedByRuleCount={RankedByRuleCount}",
                conversationId,
                candidateProducts.Count,
                rankedSoftByRule.Count);

            var rankedSoft = await TryLlmSoftRerankAsync(
                conversationId,
                normalizedMessage,
                intent,
                profile,
                rankedSoftByRule);

            if (rankedSoft == null || rankedSoft.Count == 0)
            {
                return await BuildNoMatchResponseAsync(conversationId, normalizedMessage, intent, profile);
            }

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                rankedSoft,
                "refine");

            var softDraftReply = _replyStyleService.BuildRefinementReply(
                rankedSoft,
                intent,
                normalizedMessage,
                item => BuildSimpleRefineReason(item, intent, signals));

            var softReply = await _replyRewriteService.RewriteAsync(normalizedMessage, softDraftReply);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = softReply,
                Products = ChatProductCardMapper.MapMany(rankedSoft, 4)
            };
        }

        private async Task<ChatResponse> HandleLightweightRefinementAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            List<ProductSummaryDto> previousProducts,
            RefinementSignals signals)
        {
            var filteredList = ApplyAllExclusions(previousProducts, intent, profile);
            _logger.LogWarning(
    "EXCLUDE DEBUG => IntentExcludedProducts={IntentExcludedProducts}, ProfileExcludedProducts={ProfileExcludedProducts}, Previous={Previous}, After={After}",
    string.Join(",", intent.ExcludedProducts),
    string.Join(",", profile.ExcludedProducts),
    string.Join(" | ", previousProducts.Select(x => x.Ten)),
    string.Join(" | ", filteredList.Select(x => x.Ten)));
            if (filteredList.Count == 0)
            {
                return await BuildNoMatchResponseAsync(conversationId, normalizedMessage, intent, profile);
            }

            filteredList = ProductPriceFilterHelper.ApplyStrictPriceFilter(filteredList, intent);
            if (filteredList.Count == 0)
            {
                var draftNoMatch = _replyStyleService.BuildRefinementNoMatchReply(intent);
                var noMatchReply = await _replyRewriteService.RewriteAsync(normalizedMessage, draftNoMatch);

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = noMatchReply
                };
            }

            var currentBand = BuildCurrentResultPriceBand(previousProducts);
            var relativePriceAnchor = ResolveRelativePriceAnchor(intent, profile, currentBand);
            bool isRelativePriceRefinement = IsRelativePriceRefinement(signals);

            List<ProductSummaryDto> ranked;

            if (isRelativePriceRefinement)
            {
                filteredList = ApplyRelativePriceRefinement(
     filteredList,
     relativePriceAnchor,
     currentBand,
     signals);

                if (filteredList.Count == 0)
                {
                    return await BuildRelativePriceNoMatchResponseAsync(
    conversationId,
    normalizedMessage,
    intent,
    profile,
    relativePriceAnchor,
    currentBand,
    signals);
                }

                ranked = filteredList
                    .Take(Math.Min(3, filteredList.Count))
                    .ToList();
            }
            else
            {
                filteredList = ApplySemanticRefinementOrdering(
                    filteredList,
                    signals,
                    profile,
                    intent,
                    normalizedMessage,
                    previousProducts);

                if (signals.PreferDifferent)
                {
                    ranked = filteredList.Take(Math.Min(3, filteredList.Count)).ToList();
                }
                else
                {
                    ranked = _productRecommendationService.RankProducts(
                        filteredList,
                        intent,
                        profile,
                        normalizedMessage,
                        take: Math.Min(3, filteredList.Count));
                }
            }

            if (ranked == null || ranked.Count == 0)
            {
                return await BuildNoMatchResponseAsync(conversationId, normalizedMessage, intent, profile);
            }

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                "refine");

            var draftReply = _replyStyleService.BuildRefinementReply(
                ranked,
                intent,
                normalizedMessage,
                item => BuildSimpleRefineReason(item, intent, signals));

            var reply = await _replyRewriteService.RewriteAsync(normalizedMessage, draftReply);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(ranked, 4)
            };
        }

        private async Task<ChatResponse> BuildNoMatchResponseAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var draftNoMatch = BuildSmartRefinementNoMatchReply(intent, profile);
            var noMatchReply = await _replyRewriteService.RewriteAsync(normalizedMessage, draftNoMatch);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = noMatchReply
            };
        }

        private static void EnrichIntentFromFollowUp(string normalizedMessage, ParsedIntent intent)
        {
            if (intent == null) return;

            EnrichSoftPreferenceIntent(normalizedMessage, intent);

            var text = NormalizeText(normalizedMessage);

            if (LooksLikeCheaperRequest(text))
            {
                if (intent.FilterType == PriceFilterType.None && !intent.PriceMax.HasValue && intent.TargetPrice.HasValue)
                {
                    intent.FilterType = PriceFilterType.MaxOnly;
                    intent.PriceMax = intent.TargetPrice.Value;
                }

                intent.ComparisonFeature ??= "price";
            }
            if (LooksLikeMoreExpensiveRequest(text))
            {
                intent.ComparisonFeature ??= "price";
            }

            if (LooksLikeDifferentAlternativeRequest(text))
            {
                intent.ComparisonFeature ??= "alternative";
            }

            if (LooksLikeWorkSuitabilityRequest(text))
            {
                intent.ForWork = true;
                intent.ComparisonFeature ??= "work_fit";
            }

            if (LooksLikeSchoolSuitabilityRequest(text))
            {
                intent.ForSchool = true;
                intent.ComparisonFeature ??= "school_fit";
            }
        }

        private static List<ProductSummaryDto> ApplyIntentFilters(List<ProductSummaryDto> items, ParsedIntent intent)
        {
            items ??= new List<ProductSummaryDto>();
            bool isRelativePriceRequest =
    string.Equals(intent.ComparisonFeature, "price", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                items = items
                    .Where(x => IsSameCategory(x.Loai, intent.Category))
                    .ToList();
            }

            if (intent.ExcludedCategories.Any())
            {
                items = items
                    .Where(x => !intent.ExcludedCategories.Any(ex => IsSameCategory(x.Loai, ex)))
                    .ToList();
            }

            if (intent.ExcludedBrands.Any())
            {
                items = items
                    .Where(x => !intent.ExcludedBrands.Any(ex =>
                        string.Equals(x.ThuongHieu, ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            if (intent.ExcludedProducts.Any())
            {
                items = items
                    .Where(x => !intent.ExcludedProducts.Any(ex =>
                        string.Equals(x.Ten, ex, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            return items;
        }

        private static List<ProductSummaryDto> ApplySemanticRefinementOrdering(
            List<ProductSummaryDto> items,
            RefinementSignals signals,
            CustomerPreferenceProfile profile,
            ParsedIntent intent,
            string normalizedMessage,
            List<ProductSummaryDto> previousProducts)
        {
            items ??= new List<ProductSummaryDto>();
            if (items.Count == 0) return items;
            if (intent.WantsLargeStorage)
            {
                items = items
                    .OrderByDescending(x => LooksLikeLargeStorageCandidate(x))
                    .ThenByDescending(x => x.SoLuong)
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.WantsFuelSaving)
            {
                items = items
                    .OrderByDescending(x => LooksLikeFuelSavingCandidate(x))
                    .ThenByDescending(x => x.SoLuong)
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            if (intent.NeedsLowSeat || intent.WantsEasyControl)
            {
                items = items
                    .OrderByDescending(x => LooksLikeLowSeatCandidate(x))
                    .ThenByDescending(x => x.SoLuong)
                    .ThenBy(x => x.Gia)
                    .ToList();
            }
            var currentTopNames = (profile.CurrentRecommendedProducts ?? profile.LastRecommendedProducts ?? new List<string>())
     .Where(x => !string.IsNullOrWhiteSpace(x))
     .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IEnumerable<ProductSummaryDto> ordered = items;

            if (signals.PreferDifferent)
            {
                ordered = ordered
                    .OrderBy(x => currentTopNames.Contains(x.Ten ?? string.Empty) ? 1 : 0)
                    .ThenByDescending(x => x.SoLuong)
                    .ThenBy(x => x.Gia);

                var reordered = ordered.ToList();
                if (reordered.All(x => currentTopNames.Contains(x.Ten ?? string.Empty)))
                {
                    reordered = previousProducts
                        .Where(x => !currentTopNames.Contains(x.Ten ?? string.Empty))
                        .ToList();
                }

                items = reordered;
            }

            if (signals.PreferMoreStylish)
            {
                items = items
                    .OrderByDescending(x => GetVisualStyleScore(x))
                    .ThenByDescending(x => x.Gia)
                    .ToList();
            }
            if (signals.PreferCheaper || signals.PreferMoreExpensive)
            {
                return items;
            }
            if (signals.HasSoftPreferenceChange)
            {
                items = items
                    .OrderByDescending(x => GetSoftPreferenceMatchScore(x, intent))
                    .ThenByDescending(x => x.SoLuong)
                    .ThenBy(x => x.Gia)
                    .ToList();
            }

            return items;
        }

        private static void EnrichSoftPreferenceIntent(string normalizedMessage, ParsedIntent intent)
        {
            var text = NormalizeText(normalizedMessage);

            if (LooksLikeLargeStorageRequest(text))
            {
                intent.WantsLargeStorage = true;
                intent.ComparisonFeature ??= "storage";
            }

            if (LooksLikeLowSeatRequest(text))
            {
                intent.NeedsLowSeat = true;
                intent.WantsEasyControl = true;
                intent.ComparisonFeature ??= "low_seat";
            }

            if (LooksLikeFuelSavingRequest(text))
            {
                intent.WantsFuelSaving = true;
                intent.ComparisonFeature ??= "fuel_saving";
            }

            if (LooksLikeWorkSuitabilityRequest(text))
            {
                intent.ForWork = true;
                intent.ComparisonFeature ??= "work_fit";
            }

            if (LooksLikeSchoolSuitabilityRequest(text))
            {
                intent.ForSchool = true;
                intent.ComparisonFeature ??= "school_fit";
            }
        }

        private static int GetSoftPreferenceMatchScore(ProductSummaryDto item, ParsedIntent intent)
        {
            if (item == null) return 0;

            int score = 0;
            var name = item.Ten ?? string.Empty;

            if (intent.WantsLargeStorage)
            {
                if (name.Contains("Freego", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Address", StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            if (intent.NeedsLowSeat || intent.WantsEasyControl)
            {
                if (name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Zip", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Latte", StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            if (intent.WantsFuelSaving)
            {
                if (name.Contains("Wave", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Future", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Sirius", StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                string.Equals(item.ThuongHieu, intent.Brand, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }

            if (!string.IsNullOrWhiteSpace(intent.Category) &&
                IsSameCategory(item.Loai, intent.Category))
            {
                score += 1;
            }

            return score;
        }

        private static int GetVisualStyleScore(ProductSummaryDto item)
        {
            var name = item?.Ten ?? string.Empty;
            return
                name.Contains("Latte", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Grande", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Vision", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Lead", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        }

        private static bool ShouldRefetchBroaderForSoftRefine(List<ProductSummaryDto> currentProducts, ParsedIntent intent)
        {
            if (currentProducts == null || currentProducts.Count == 0)
                return true;

            if (currentProducts.Count <= 1)
                return true;

            var matchCount = currentProducts.Count(x => GetSoftPreferenceMatchScore(x, intent) > 0);

            if (matchCount >= 2)
                return false;

            if (currentProducts.Count <= 2 && matchCount >= 1)
                return false;

            if ((intent.WantsLargeStorage || intent.WantsFuelSaving || intent.NeedsLowSeat || intent.WantsEasyControl)
                && currentProducts.Count >= 3
                && matchCount >= 1)
            {
                return false;
            }

            return true;
        }

        private static bool IsSameCategory(string? actualCategory, string excludedCategory)
        {
            var actual = NormalizeCategory(actualCategory);
            var excluded = NormalizeCategory(excludedCategory);
            return string.Equals(actual, excluded, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCategory(string? category)
        {
            var text = NormalizeText(category);

            if (text.Contains("ga"))
                return "xe ga";

            if (text.Contains("số") || text.Contains("so"))
                return "xe số";

            if (text.Contains("côn") || text.Contains("con"))
                return "côn tay";

            return text;
        }

        private static List<ProductSummaryDto> ApplyLlmRerank(
            IReadOnlyList<ProductSummaryDto> rankedByRule,
            LLMRecommendationResult? llmResult,
            int take)
        {
            if (rankedByRule == null || rankedByRule.Count == 0)
                return new List<ProductSummaryDto>();

            if (llmResult?.Recommendations == null || llmResult.Recommendations.Count == 0)
                return rankedByRule.Take(take).ToList();

            var byId = rankedByRule.ToDictionary(x => x.Id, x => x);

            var orderedLlmRecs = llmResult.Recommendations
                .Where(x => x != null)
                .GroupBy(x => x.ProductId)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ProductId)
                .ToList();

            var selected = new List<ProductSummaryDto>();

            foreach (var rec in orderedLlmRecs)
            {
                if (byId.TryGetValue(rec.ProductId, out var product))
                {
                    selected.Add(product);
                }
            }

            if (selected.Count > 0)
            {
                return selected.Take(take).ToList();
            }

            return rankedByRule.Take(take).ToList();
        }

        private static string BuildSimpleRefineReason(
      ProductSummaryDto item,
     ParsedIntent intent,
     RefinementSignals signals)
        {
            var name = item?.Ten ?? string.Empty;
            var category = NormalizeText(intent.Category);

            bool hasPriceIntent =
                intent.PriceMax.HasValue ||
                intent.PriceMin.HasValue ||
                intent.TargetPrice.HasValue;

            if (signals.PreferCheaper)
                return PickReasonByName(name,
                    "giá mềm hơn nhóm bạn vừa xem, dễ cân nhắc hơn nếu muốn tiết kiệm chi phí",
                    "hợp hơn nếu bạn muốn giảm ngân sách mà vẫn có lựa chọn ổn");

            if (signals.PreferMoreExpensive)
                return PickReasonByName(name,
                    "nhỉnh hơn một chút nhưng hợp nếu bạn muốn nâng lên lựa chọn cao hơn",
                    "đáng cân nhắc nếu bạn muốn chọn mẫu cao hơn nhóm vừa xem");

            if (signals.PreferDifferent || signals.WantsBroaderAlternatives)
                return PickReasonByName(name,
                    "là phương án khác khá đáng tham khảo trong nhóm này",
                    "giúp bạn có thêm lựa chọn ngoài các mẫu vừa xem");

            if (signals.WantsLargeStorage)
                return PickReasonByName(name,
                    "hợp hơn nếu bạn cần cốp rộng và tiện mang đồ",
                    "phù hợp nếu bạn hay mang theo nhiều vật dụng hằng ngày");

            if (signals.NeedsLowSeat)
                return PickReasonByName(name,
                    "dễ làm quen hơn, phù hợp nếu bạn ưu tiên xe dễ điều khiển",
                    "hợp với nhu cầu cần xe nhẹ, dễ kiểm soát khi đi phố");

            if (signals.WantsFuelSaving)
                return PickReasonByName(name,
                    "tiết kiệm xăng, phù hợp đi lại thường xuyên",
                    "chi phí vận hành thấp, hợp dùng hằng ngày");

            if (signals.ForWork)
                return PickReasonByName(name,
                    "khá hợp để đi làm hằng ngày, dễ dùng và thực dụng",
                    "đi làm ổn định, chi phí sử dụng hợp lý");

            if (signals.ForSchool)
                return PickReasonByName(name,
                    "khá hợp để đi học hằng ngày, chi phí sử dụng dễ chịu",
                    "nhẹ nhàng, dễ đi và phù hợp với nhu cầu đi học");

            // Ưu tiên category trước price để tránh hỏi "xe số bền" nhưng lại trả reason ngân sách
            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                if (category.Contains("so"))
                {
                    if (item.Gia > 50000000)
                    {
                        return "thiết kế hoài cổ, phù hợp nếu bạn thích phong cách đặc biệt";
                    }

                    return PickReasonByName(name,
                        "dễ đi và khá bền cho nhu cầu hằng ngày",
                        "chi phí thấp, phù hợp dùng lâu dài",
                        "thực dụng, dễ bảo dưỡng",
                        "hợp để đi làm hoặc đi học hằng ngày");
                }

                if (category.Contains("ga"))
                    return PickReasonByName(name,
                        "tiện đi phố, dễ sử dụng và hợp nhu cầu di chuyển hằng ngày",
                        "thoải mái khi đi trong đô thị, không cần thao tác quá nhiều",
                        "gọn gàng, dễ dùng và phù hợp đi lại thường xuyên");

                if (category.Contains("con"))
                    return PickReasonByName(name,
                        "cảm giác lái chủ động, hợp kiểu thể thao",
                        "phù hợp nếu bạn thích xe mạnh và cá tính",
                        "đáng cân nhắc nếu bạn thích phong cách thể thao hơn");

                return "khá hợp với kiểu xe bạn đang ưu tiên";
            }

            if (!string.IsNullOrWhiteSpace(intent.Brand))
                return PickReasonByName(name,
                    $"đáng cân nhắc nếu bạn đang ưu tiên hãng {intent.Brand}",
                    $"là một lựa chọn khá ổn trong nhóm xe của {intent.Brand}");

            if (hasPriceIntent)
                return PickReasonByName(name,
                    "nằm khá sát mức giá bạn vừa đưa ra",
                    "giá gần với ngân sách nên khá dễ cân nhắc",
                    "phù hợp nếu bạn muốn bám sát mức tiền đang dự tính");

            return PickReasonByName(name,
                "là lựa chọn khá ổn nếu bạn muốn tham khảo thêm trong nhóm này",
                "đáng cân nhắc thêm nếu bạn muốn có thêm phương án so sánh",
                "phù hợp khá ổn với nhu cầu hiện tại");
        }

        private static string PickReasonByName(string? productName, params string[] reasons)
        {
            if (reasons == null || reasons.Length == 0)
                return "là lựa chọn khá ổn nếu bạn muốn tham khảo thêm";

            var name = productName ?? string.Empty;
            var index = Math.Abs(name.GetHashCode()) % reasons.Length;

            return reasons[index];
        }
        private static RefinementSignals AnalyzeRefinementSignals(
            string message,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var text = NormalizeText(message);

            bool mentionsBrandInCurrentTurn =
                !string.IsNullOrWhiteSpace(intent.Brand) &&
                text.Contains(NormalizeText(intent.Brand));

            bool mentionsCategoryInCurrentTurn =
                !string.IsNullOrWhiteSpace(intent.Category) &&
                text.Contains(NormalizeText(intent.Category));

            bool hasExplicitBrandOrCategoryThisTurn =
     mentionsBrandInCurrentTurn ||
     mentionsCategoryInCurrentTurn ||
     intent.ExcludedCategories.Any() ||
     intent.ExcludedBrands.Any() ||
     intent.ExcludedProducts.Any();

            bool hasExplicitPricePhrase = HasAnyPricePhrase(text);
            bool hasPriceIntent =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                intent.FilterType != PriceFilterType.None;

            bool wantsLargeStorage = intent.WantsLargeStorage || LooksLikeLargeStorageRequest(text);
            bool needsLowSeat = intent.NeedsLowSeat || intent.WantsEasyControl || LooksLikeLowSeatRequest(text);
            bool wantsFuelSaving = intent.WantsFuelSaving || LooksLikeFuelSavingRequest(text);
            bool forWork = intent.ForWork || LooksLikeWorkSuitabilityRequest(text);
            bool forSchool = intent.ForSchool || LooksLikeSchoolSuitabilityRequest(text);
            bool preferCheaper = LooksLikeCheaperRequest(text);
            bool preferMoreExpensive = LooksLikeMoreExpensiveRequest(text);
            bool preferDifferent =
     LooksLikeDifferentAlternativeRequest(text) ||
     string.Equals(intent.ComparisonFeature, "alternative", StringComparison.OrdinalIgnoreCase);

            bool wantsBroader =
                LooksLikeBroaderAlternativeRequest(text) ||
                string.Equals(intent.ComparisonFeature, "alternative", StringComparison.OrdinalIgnoreCase);

            bool wantsDecideBest =
                string.Equals(intent.ComparisonFeature, "decide_best", StringComparison.OrdinalIgnoreCase);

            bool preferMoreStylish = LooksLikePrettierRequest(text);

            bool hasSoftPreferenceChange =
                wantsLargeStorage ||
                needsLowSeat ||
                wantsFuelSaving ||
                forWork ||
                forSchool ||
                preferMoreStylish ||
                preferCheaper ||
                preferMoreExpensive ||
                preferDifferent ||
                wantsBroader ||
                wantsDecideBest;
            bool isSoftOnlyPhrase = hasSoftPreferenceChange &&
                !hasExplicitBrandOrCategoryThisTurn &&
                !hasExplicitPricePhrase;

            bool hasHardFilterChange =
                hasExplicitBrandOrCategoryThisTurn ||
                (hasPriceIntent && hasExplicitPricePhrase);

            if (isSoftOnlyPhrase)
            {
                hasHardFilterChange = false;
            }

            return new RefinementSignals
            {
                HasHardFilterChange = hasHardFilterChange,
                HasSoftPreferenceChange = hasSoftPreferenceChange,
                PreferCheaper = preferCheaper,
                PreferMoreExpensive = preferMoreExpensive,
                PreferDifferent = preferDifferent,
                PreferMoreStylish = preferMoreStylish,
                WantsBroaderAlternatives = wantsBroader,
                WantsLargeStorage = wantsLargeStorage,
                WantsFuelSaving = wantsFuelSaving,
                NeedsLowSeat = needsLowSeat,
                ForWork = forWork,
                ForSchool = forSchool
            };
        }

        private string BuildSmartRefinementNoMatchReply(ParsedIntent intent, CustomerPreferenceProfile profile)
        {
            var categoryText = !string.IsNullOrWhiteSpace(intent.Category) ? intent.Category : "xe hiện tại";
            var brandText = !string.IsNullOrWhiteSpace(intent.Brand) ? intent.Brand : "hãng hiện tại";
            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                !string.IsNullOrWhiteSpace(intent.Category) &&
                (intent.PriceMax.HasValue || intent.PriceMin.HasValue || intent.TargetPrice.HasValue))
            {
                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return $"Hiện tại {intent.Brand} gần như không có mẫu {intent.Category} trong tầm dưới {intent.PriceMax.Value:N0} VNĐ. Bạn có thể tăng ngân sách hoặc đổi sang hãng khác để mình lọc tiếp.";
                }

                if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
                {
                    return $"Hiện tại {intent.Brand} gần như không có mẫu {intent.Category} trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
                }

                return $"Hiện tại {intent.Brand} gần như không có mẫu {intent.Category} thật sự phù hợp với mức giá bạn đang nhắm tới. Bạn có thể tăng ngân sách hoặc đổi sang hãng khác để mình lọc tiếp.";
            }

            if (!string.IsNullOrWhiteSpace(intent.Category) &&
                (intent.PriceMax.HasValue || intent.PriceMin.HasValue || intent.TargetPrice.HasValue))
            {
                if (intent.PriceMax.HasValue && !intent.PriceMin.HasValue)
                {
                    return $"Hiện tại nhóm {categoryText} trong tầm dưới {intent.PriceMax.Value:N0} VNĐ khá ít lựa chọn với tiêu chí này. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
                }
            }

            if (intent.PriceMin.HasValue && intent.PriceMax.HasValue)
            {
                return $"Hiện tại nhóm {intent.Category} trong khoảng {intent.PriceMin.Value:N0} - {intent.PriceMax.Value:N0} VNĐ khá ít lựa chọn với tiêu chí này. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
            }

            if (intent.TargetPrice.HasValue)
            {
                return $"Hiện tại nhóm {intent.Category} quanh mức {intent.TargetPrice.Value:N0} VNĐ khá ít lựa chọn với tiêu chí này. Bạn có thể nới nhẹ ngân sách hoặc đổi hãng để mình lọc tiếp.";
            }

            if (intent.PriceMin.HasValue && !intent.PriceMax.HasValue)
            {
                return $"Hiện tại nhóm {intent.Category} từ {intent.PriceMin.Value:N0} VNĐ trở lên vẫn chưa có nhiều lựa chọn thật sự phù hợp với tiêu chí này. Bạn có thể đổi hãng hoặc điều chỉnh thêm điều kiện để mình lọc tiếp.";
            }

            return _replyStyleService.BuildRefinementNoMatchReply(intent);
        }

        private static bool HasAnyPricePhrase(string text)
        {
            return text.Contains("dưới ") ||
                   text.Contains("duoi ") ||
                   text.Contains("trên ") ||
                   text.Contains("tren ") ||
                   text.Contains("từ ") ||
                   text.Contains("tu ") ||
                   text.Contains("đến ") ||
                   text.Contains("den ") ||
                   text.Contains("khoảng ") ||
                   text.Contains("khoang ") ||
                   text.Contains("tầm ") ||
                   text.Contains("tam ") ||
                   text.Contains("quanh ") ||
                   text.Contains("triệu") ||
                   text.Contains("trieu") ||
                   text.Contains("tr");
        }

        private static bool LooksLikeLargeStorageRequest(string text) =>
            text.Contains("cốp rộng") ||
            text.Contains("cop rong") ||
            text.Contains("đựng đồ") ||
            text.Contains("dung do") ||
            text.Contains("nhiều đồ") ||
            text.Contains("nhieu do");

        private static bool LooksLikeLowSeatRequest(string text) =>
            text.Contains("dễ chống chân") ||
            text.Contains("de chong chan") ||
            text.Contains("yên thấp") ||
            text.Contains("yen thap") ||
            text.Contains("thấp người") ||
            text.Contains("thap nguoi") ||
            text.Contains("dễ đi") ||
            text.Contains("de di");

        private static bool LooksLikeFuelSavingRequest(string text) =>
            text.Contains("tiết kiệm xăng") ||
            text.Contains("tiet kiem xang") ||
            text.Contains("ít hao xăng") ||
            text.Contains("it hao xang") ||
            text.Contains("đỡ hao xăng") ||
            text.Contains("do hao xang");

        private static bool LooksLikeWorkSuitabilityRequest(string text) =>
            text.Contains("đi làm") ||
            text.Contains("di lam") ||
            text.Contains("đi làm hằng ngày") ||
            text.Contains("di lam hang ngay") ||
            text.Contains("đi phố") ||
            text.Contains("di pho");

        private static bool LooksLikeSchoolSuitabilityRequest(string text) =>
            text.Contains("đi học") ||
            text.Contains("di hoc") ||
            text.Contains("sinh viên") ||
            text.Contains("sinh vien") ||
            text.Contains("học sinh") ||
            text.Contains("hoc sinh");

        private static bool LooksLikeCheaperRequest(string text) =>
    text.Contains("re hon") ||
    text.Contains("mau nao re hon") ||
    text.Contains("xe nao re hon") ||
    text.Contains("co mau nao re hon") ||
    text.Contains("mem hon") ||
    text.Contains("gia thap hon") ||
    text.Contains("thap hon") ||
    text.Contains("it tien hon") ||
    text.Contains("tiet kiem hon") ||
    text.Contains("xuong gia") ||
    text.Contains("xuong tien");

        private static bool LooksLikeDifferentAlternativeRequest(string text) =>
            text.Contains("loại khác") ||
            text.Contains("loai khac") ||
            text.Contains("xe khác") ||
            text.Contains("xe khac") ||
            text.Contains("mẫu khác") ||
            text.Contains("mau khac") ||
            text.Contains("con khác") ||
            text.Contains("khac di");

        private static bool LooksLikePrettierRequest(string text) =>
            text.Contains("đẹp hơn") ||
            text.Contains("dep hon") ||
            text.Contains("thời trang hơn") ||
            text.Contains("thoi trang hon") ||
            text.Contains("nữ tính hơn") ||
            text.Contains("nu tinh hon");

        private static bool LooksLikeBroaderAlternativeRequest(string text)
        {
            if (text.Contains("mau khac") ||
                text.Contains("xe khac") ||
                text.Contains("khac di") ||
                text.Contains("loai khac") ||
                text.Contains("con khac"))
            {
                return true;
            }

            return false;
        }
        private static bool CurrentTurnHasExplicitCategory(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("xe so") ||
                   text.Contains("xe ga") ||
                   text.Contains("tay ga") ||
                   text.Contains("con tay");
        }

        private static bool CurrentTurnHasExplicitBrand(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("honda") ||
                   text.Contains("yamaha") ||
                   text.Contains("suzuki") ||
                   text.Contains("sym") ||
                   text.Contains("piaggio");
        }
        private static bool CurrentTurnHasExplicitPrice(string message)
        {
            var text = NormalizeText(message);

            return text.Contains("trieu") ||
                   text.Contains("vnd") ||
                   text.Contains("vnđ") ||
                   text.Contains("duoi") ||
                   text.Contains("tren") ||
                   text.Contains("tam") ||
                   text.Contains("khoang") ||
                   text.Contains("tu ") ||
                   text.Contains("den ") ||
                   text.Any(char.IsDigit);
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
        private static (decimal? MinPrice, decimal? MaxPrice) BuildHardRefinementPriceWindow(ParsedIntent intent)
        {
            decimal? minPrice = intent.PriceMin;
            decimal? maxPrice = intent.PriceMax;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var delta = ProductPriceFilterHelper.GetAroundDelta(intent.TargetPrice.Value);
                minPrice = intent.TargetPrice.Value - delta;
                maxPrice = intent.TargetPrice.Value + delta;
            }

            return (minPrice, maxPrice);
        }
        private static List<ProductSummaryDto> OrderBySlightlyCheaperPreference(
     List<ProductSummaryDto> items,
     CustomerPreferenceProfile profile,
     List<ProductSummaryDto> previousProducts)
        {
            decimal? anchor = profile.TargetPrice ?? profile.PriceMax;

            if (!anchor.HasValue && previousProducts.Count > 0)
                anchor = previousProducts.Average(x => x.Gia);

            if (!anchor.HasValue)
                return items.OrderBy(x => x.Gia).ToList();

            return items
                .OrderBy(x => x.Gia > anchor.Value ? 1 : 0)
                .ThenBy(x => Math.Abs(anchor.Value - x.Gia))
                .ToList();
        }
        private async Task<List<ProductSummaryDto>> FetchHardRefinementCandidatesAsync(
    ParsedIntent intent,
    decimal? minPrice,
    decimal? maxPrice)
        {
            var toolResult = await _toolClient.GetProductsByFiltersAsync(
                brand: intent.Brand,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: intent.Category,
                take: 30);

            return toolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();
        }
        private static bool IsBrandSwitchOnly(ParsedIntent intent)
        {
            return !string.IsNullOrWhiteSpace(intent.Brand) &&
                   string.IsNullOrWhiteSpace(intent.Category) &&
                   !intent.ExcludedBrands.Any() &&
                   !intent.ExcludedCategories.Any() &&
                   !intent.PriceMin.HasValue &&
                   !intent.TargetPrice.HasValue;
        }
        private async Task<ChatResponse?> TryBrandRelaxationAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    decimal? maxPrice)
        {
            decimal? relaxedMin = null;
            decimal? relaxedMax = maxPrice.HasValue ? maxPrice.Value + 8_000_000m : null;

            var relaxedToolResult = await _toolClient.GetProductsByFiltersAsync(
                brand: intent.Brand,
                minPrice: relaxedMin,
                maxPrice: relaxedMax,
                category: intent.Category,
                take: 20);

            var relaxedItems = relaxedToolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            relaxedItems = ApplyIntentFilters(relaxedItems, intent);
            relaxedItems = ApplyAllExclusions(relaxedItems, intent, profile);

            if (relaxedItems.Count == 0)
                return null;

            var rankedRelaxed = _productRecommendationService.RankProducts(
                relaxedItems,
                intent,
                profile,
                normalizedMessage,
                take: Math.Min(3, relaxedItems.Count));

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                rankedRelaxed,
                "refine");

            var brandDraftReply = _replyStyleService.BuildRefinementBrandRelaxedReply(rankedRelaxed, intent);
            var brandReply = await _replyRewriteService.RewriteAsync(normalizedMessage, brandDraftReply);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = brandReply,
                Products = ChatProductCardMapper.MapMany(rankedRelaxed, 4)
            };
        }
        private static (decimal? MinPrice, decimal? MaxPrice, string? Brand, string? Category) BuildSoftRefinementBaseFilters(
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
            decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;

            if (!intent.TargetPrice.HasValue && profile.TargetPrice.HasValue)
                intent.TargetPrice = profile.TargetPrice;

            if (intent.FilterType == PriceFilterType.None && profile.FilterType != PriceFilterType.None)
                intent.FilterType = profile.FilterType;

            var brand = !string.IsNullOrWhiteSpace(intent.Brand) ? intent.Brand : profile.PreferredBrand;
            var category = !string.IsNullOrWhiteSpace(intent.Category) ? intent.Category : profile.PreferredCategory;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var delta = ProductPriceFilterHelper.GetAroundDelta(intent.TargetPrice.Value);
                minPrice = intent.TargetPrice.Value - delta;
                maxPrice = intent.TargetPrice.Value + delta;
            }
            else if (profile.FilterType == PriceFilterType.Around &&
                     profile.TargetPrice.HasValue &&
                     !intent.PriceMin.HasValue &&
                     !intent.PriceMax.HasValue &&
                     !intent.TargetPrice.HasValue)
            {
                var delta = ProductPriceFilterHelper.GetAroundDelta(profile.TargetPrice.Value);
                minPrice = profile.TargetPrice.Value - delta;
                maxPrice = profile.TargetPrice.Value + delta;
            }

            return (minPrice, maxPrice, brand, category);
        }
        private async Task<List<ProductSummaryDto>> FetchBroaderSoftCandidatesAsync(
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     decimal? minPrice,
     decimal? maxPrice,
     string? brand,
     string? category)
        {
            var toolResult = await _toolClient.GetProductsByFiltersAsync(
                brand: brand,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: category,
                take: 30);

            var broaderCandidates = toolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();

            broaderCandidates = ApplyIntentFilters(broaderCandidates, intent);
            broaderCandidates = ApplyAllExclusions(broaderCandidates, intent, profile);

            return broaderCandidates;
        }
        private static List<ProductSummaryDto> ApplySoftPriceGuard(
    List<ProductSummaryDto> candidateProducts,
    List<ProductSummaryDto> previousProducts,
    CustomerPreferenceProfile profile)
        {
            if (!profile.PriceMin.HasValue && !profile.PriceMax.HasValue)
                return candidateProducts;

            var guardMin = profile.PriceMin.HasValue ? profile.PriceMin.Value - 5_000_000m : decimal.MinValue;
            var guardMax = profile.PriceMax.HasValue ? profile.PriceMax.Value + 8_000_000m : decimal.MaxValue;

            candidateProducts = candidateProducts
                .Where(x => x.Gia >= guardMin && x.Gia <= guardMax)
                .ToList();

            if (candidateProducts.Count == 0)
                return previousProducts.ToList();

            return candidateProducts;
        }
        private async Task<List<ProductSummaryDto>> TryLlmSoftRerankAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    List<ProductSummaryDto> rankedSoftByRule)
        {
            if (rankedSoftByRule.Count < 3)
            {
                _logger.LogInformation(
                    "Skip LLM rerank in soft refinement because only {RuleCount} rule-ranked candidates remain. ConversationId={ConversationId}",
                    rankedSoftByRule.Count,
                    conversationId);

                return rankedSoftByRule;
            }

            try
            {
                var llmResult = await _recommendationLLMService.RerankAsync(
                    normalizedMessage,
                    intent,
                    profile,
                    rankedSoftByRule);

                var llmRanked = ApplyLlmRerank(
                    rankedSoftByRule,
                    llmResult,
                    Math.Min(4, rankedSoftByRule.Count));

                if (llmRanked.Count > 0)
                {
                    _logger.LogInformation(
                        "Soft refinement LLM rerank applied. ConversationId={ConversationId}, RuleCount={RuleCount}, LlmSelectedCount={LlmSelectedCount}",
                        conversationId,
                        rankedSoftByRule.Count,
                        llmRanked.Count);

                    return llmRanked;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM rerank failed in RefinementService. Fallback to rule ranking.");
            }

            return rankedSoftByRule;
        }
        private static (decimal? PreferredMin, decimal? PreferredMax) BuildSlightlyCheaperWindow(
     CustomerPreferenceProfile profile,
     List<ProductSummaryDto> currentContextProducts,
     List<ProductSummaryDto> previousProducts)
        {
            decimal? anchor = null;

            if (currentContextProducts != null && currentContextProducts.Count > 0)
            {
                anchor = currentContextProducts.Min(x => x.Gia);
            }

            if (!anchor.HasValue && profile.TargetPrice.HasValue)
                anchor = profile.TargetPrice.Value;

            if (!anchor.HasValue && profile.PriceMax.HasValue)
                anchor = profile.PriceMax.Value;

            if (!anchor.HasValue && previousProducts.Count > 0)
            {
                var ordered = previousProducts.OrderBy(x => x.Gia).ToList();
                anchor = ordered[ordered.Count / 2].Gia;
            }

            if (!anchor.HasValue)
                return (null, null);

            var preferredMax = anchor.Value - 1_000_000m;
            var preferredMin = Math.Max(0, anchor.Value - 8_000_000m);

            return (preferredMin, preferredMax);
        }
        private static List<ProductSummaryDto> ApplySoftContextFilters(
    List<ProductSummaryDto> items,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            items ??= new List<ProductSummaryDto>();

            bool isRelativePriceRequest =
                string.Equals(intent.ComparisonFeature, "price", StringComparison.OrdinalIgnoreCase);

            bool currentTurnHasCategory = !string.IsNullOrWhiteSpace(intent.Category);
            bool currentTurnHasBrand = !string.IsNullOrWhiteSpace(intent.Brand);

            var effectiveBrand = currentTurnHasBrand
                ? intent.Brand
                : isRelativePriceRequest || currentTurnHasCategory
                    ? null
                    : profile.PreferredBrand;

            var effectiveCategory = !string.IsNullOrWhiteSpace(intent.Category)
                ? intent.Category
                : profile.PreferredCategory;

            if (!string.IsNullOrWhiteSpace(effectiveBrand))
            {
                items = items
                    .Where(x => string.Equals(x.ThuongHieu, effectiveBrand, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(effectiveCategory))
            {
                items = items
                    .Where(x => IsSameCategory(x.Loai, effectiveCategory))
                    .ToList();
            }

            items = ApplyAllExclusions(items, intent, profile);
            return items;
        }
        private static bool LooksLikeMoreExpensiveRequest(string text) =>
    text.Contains("đắt hơn") ||
    text.Contains("dat hon") ||
    text.Contains("cao hơn") ||
    text.Contains("cao hon") ||
    text.Contains("xịn hơn") ||
    text.Contains("xin hon") ||
    text.Contains("nhỉnh hơn") ||
    text.Contains("nhinh hon");
        private static CurrentResultPriceBand BuildCurrentResultPriceBand(
    List<ProductSummaryDto> currentProducts)
        {
            if (currentProducts == null || currentProducts.Count == 0)
            {
                return new CurrentResultPriceBand();
            }

            var ordered = currentProducts
                .Where(x => x != null)
                .OrderBy(x => x.Gia)
                .ToList();

            if (ordered.Count == 0)
            {
                return new CurrentResultPriceBand();
            }

            return new CurrentResultPriceBand
            {
                MinPrice = ordered.First().Gia,
                MaxPrice = ordered.Last().Gia,
                MedianPrice = ordered[ordered.Count / 2].Gia
            };
        }
        private static decimal? ResolveRelativePriceAnchor(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    CurrentResultPriceBand currentBand)
        {
            if (signalsLikeAround(intent) && intent.TargetPrice.HasValue)
                return intent.TargetPrice.Value;

            if (intent.PriceMax.HasValue)
                return intent.PriceMax.Value;

            if (intent.TargetPrice.HasValue)
                return intent.TargetPrice.Value;

            if (profile.FilterType == PriceFilterType.Around && profile.TargetPrice.HasValue)
                return profile.TargetPrice.Value;

            if (profile.PriceMax.HasValue)
                return profile.PriceMax.Value;

            if (profile.TargetPrice.HasValue)
                return profile.TargetPrice.Value;

            if (currentBand.MedianPrice.HasValue)
                return currentBand.MedianPrice.Value;

            return currentBand.MinPrice;
        }

        private static bool signalsLikeAround(ParsedIntent intent)
        {
            return intent.FilterType == PriceFilterType.Around;
        }
        private static bool IsRelativePriceRefinement(RefinementSignals signals)
        {
            return signals.PreferCheaper || signals.PreferMoreExpensive;
        }
        private async Task<List<ProductSummaryDto>> FetchRelativePriceCandidatesAsync(
       ParsedIntent intent,
       CustomerPreferenceProfile profile,
       CurrentResultPriceBand currentBand,
       RefinementSignals signals)
        {
            string? brand = !string.IsNullOrWhiteSpace(intent.Brand)
                ? intent.Brand
                : profile.PreferredBrand;

            string? category = !string.IsNullOrWhiteSpace(intent.Category)
                ? intent.Category
                : profile.PreferredCategory;

            decimal? minPrice = null;
            decimal? maxPrice = null;

            var anchor = ResolveRelativePriceAnchor(intent, profile, currentBand);

            if (signals.PreferCheaper)
            {
                maxPrice = anchor.HasValue
                    ? anchor.Value - 1
                    : profile.PriceMax;

                minPrice = null;

                // Nếu đang có brand thì thử giữ brand trước.
                // Nếu không có kết quả, bên dưới sẽ tự nới brand.
            }
            else if (signals.PreferMoreExpensive)
            {
                minPrice = anchor.HasValue
                    ? anchor.Value + 1
                    : profile.PriceMin;
            }

            async Task<List<ProductSummaryDto>> FetchAsync(string? fetchBrand, string? fetchCategory)
            {
                var result = await _toolClient.GetProductsByFiltersAsync(
                    brand: fetchBrand,
                    minPrice: minPrice,
                    maxPrice: maxPrice,
                    category: fetchCategory,
                    take: 30);

                return result?.Items?
                    .Where(x => x != null)
                    .ToList() ?? new List<ProductSummaryDto>();
            }

            // Lần 1: giữ brand + category nếu có
            var items = await FetchAsync(brand, category);
            items = ApplyIntentFilters(items, intent);
            items = ApplyAllExclusions(items, intent, profile);

            if (items.Count > 0)
                return items;

            if (signals.PreferCheaper && !string.IsNullOrWhiteSpace(brand))
            {
                var relaxedBrandItems = await FetchAsync(null, category);

                var relaxedIntent = intent.Clone();
                relaxedIntent.Brand = null;

                relaxedBrandItems = ApplyIntentFilters(relaxedBrandItems, relaxedIntent);
                relaxedBrandItems = ApplyAllExclusions(relaxedBrandItems, relaxedIntent, profile);

                if (relaxedBrandItems.Count > 0)
                    return relaxedBrandItems;
            }
            if (signals.PreferCheaper && !string.IsNullOrWhiteSpace(category))
            {
                var relaxedCategoryItems = await FetchAsync(null, null);

                var relaxedIntent = intent.Clone();
                relaxedIntent.Brand = null;
                relaxedIntent.Category = null;

                relaxedCategoryItems = ApplyIntentFilters(relaxedCategoryItems, relaxedIntent);
                relaxedCategoryItems = ApplyAllExclusions(relaxedCategoryItems, relaxedIntent, profile);

                if (relaxedCategoryItems.Count > 0)
                    return relaxedCategoryItems;
            }

            return items;
        }
        private static List<ProductSummaryDto> ApplyRelativePriceRefinement(
     List<ProductSummaryDto> candidateProducts,
     decimal? anchor,
     CurrentResultPriceBand currentBand,
     RefinementSignals signals)
        {
            if (candidateProducts == null || candidateProducts.Count == 0)
                return new List<ProductSummaryDto>();

            var effectiveAnchor = anchor
                ?? currentBand.MedianPrice
                ?? currentBand.MinPrice
                ?? currentBand.MaxPrice;

            if (!effectiveAnchor.HasValue)
                return candidateProducts;

            if (signals.PreferCheaper)
            {
                return candidateProducts
                    .Where(x => x.Gia < effectiveAnchor.Value)
                    .OrderByDescending(x => x.Gia)
                    .ToList();
            }

            if (signals.PreferMoreExpensive)
            {
                return candidateProducts
                    .Where(x => x.Gia > effectiveAnchor.Value)
                    .OrderBy(x => x.Gia)
                    .ToList();
            }

            return candidateProducts;
        }

        private async Task<ChatResponse> BuildRelativePriceNoMatchResponseAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    decimal? anchor,
    CurrentResultPriceBand currentBand,
    RefinementSignals signals)
        {
            string draftReply;

            if (signals.PreferCheaper)
            {
                draftReply = anchor.HasValue
                    ? $"Trong nhóm bạn đang cân nhắc, hiện chưa có mẫu nào rẻ hơn mức {anchor.Value:N0} VNĐ. Nếu muốn, mình có thể mở rộng sang hãng khác hoặc nới điều kiện để tìm thêm."
                    : "Hiện mình chưa tìm được mẫu nào rẻ hơn trong nhóm bạn đang cân nhắc. Nếu muốn, mình có thể mở rộng sang hãng khác hoặc nới điều kiện để tìm thêm.";
            }
            else if (signals.PreferMoreExpensive)
            {
                draftReply = anchor.HasValue
                    ? $"Trong nhóm bạn đang cân nhắc, hiện chưa có mẫu nào cao hơn mức {anchor.Value:N0} VNĐ. Nếu muốn, mình có thể mở rộng lên nhóm xe cao hơn hoặc đổi sang hãng khác."
                    : "Hiện mình chưa tìm được mẫu nào đắt hơn trong nhóm bạn đang cân nhắc. Nếu muốn, mình có thể mở rộng lên nhóm xe cao hơn hoặc đổi sang hãng khác.";
            }
            else
            {
                draftReply = BuildSmartRefinementNoMatchReply(intent, profile);
            }

            var reply = await _replyRewriteService.RewriteAsync(normalizedMessage, draftReply);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                Reply = reply
            };
        }
        private static void NormalizeRefinementPriceConstraints(ParsedIntent intent)
        {
            if (intent == null)
                return;

            if (intent.FilterType == PriceFilterType.MaxOnly && intent.PriceMax.HasValue)
            {
                intent.PriceMin = null;
                intent.TargetPrice = null;
                return;
            }

            if (intent.FilterType == PriceFilterType.MinOnly && intent.PriceMin.HasValue)
            {
                intent.PriceMax = null;
                intent.TargetPrice = null;
                return;
            }

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                intent.PriceMin = null;
                intent.PriceMax = null;
                return;
            }

            if (intent.FilterType == PriceFilterType.Range &&
                intent.PriceMin.HasValue &&
                intent.PriceMax.HasValue)
            {
                intent.TargetPrice = null;
            }
        }
        private static bool LooksLikeLargeStorageCandidate(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("latte") || name.Contains("lead") || name.Contains("freego") || name.Contains("air blade");
        }

        private static bool LooksLikeFuelSavingCandidate(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") || name.Contains("wave") || name.Contains("future") || name.Contains("sirius");
        }

        private static bool LooksLikeLowSeatCandidate(ProductSummaryDto product)
        {
            var name = (product.Ten ?? string.Empty).ToLowerInvariant();
            return name.Contains("vision") || name.Contains("janus") || name.Contains("zip") || name.Contains("latte");
        }
        private static List<ProductSummaryDto> ApplyPracticalUnderboneGuard(
    List<ProductSummaryDto> items,
    ParsedIntent intent,
    string normalizedMessage)
        {
            if (items == null || items.Count == 0)
                return new List<ProductSummaryDto>();

            var text = NormalizeText(normalizedMessage);
            var category = NormalizeCategory(intent.Category);

            bool wantsDurableUnderbone =
    category == "xe số" &&
    (
        text.Contains("ben") ||
        text.Contains("b?n") ||
        text.Contains("thuc dung") ||
        text.Contains("de bao duong") ||
        text.Contains("de nuoi") ||
        text.Contains("tiet kiem") ||
        text.Contains("hang ngay") ||
        text.Contains("di lam")
    );

            if (!wantsDurableUnderbone)
                return items;

            var practical = items
                .Where(x =>
                {
                    var name = NormalizeText(x.Ten);

                    if (x.Gia > 50_000_000)
                        return false;

                    return !name.Contains("cub") &&
        !name.Contains("super cub") &&
        !name.Contains("125") &&
        !name.Contains("gd110") &&
        !name.Contains("axelo") &&
        !name.Contains("winner") &&
        !name.Contains("exciter") &&
        !name.Contains("raider");
                })
                .ToList();

            return practical.Count > 0 ? practical : new List<ProductSummaryDto>();

        }
        private static List<ProductSummaryDto> ApplyFemaleFriendlyRelativeGuard(
    List<ProductSummaryDto> items,
    CustomerPreferenceProfile profile,
    string normalizedMessage,
    List<ProductSummaryDto> previousProducts,
    RefinementSignals signals)
        {
            if (items == null || items.Count == 0)
                return new List<ProductSummaryDto>();

            if (!signals.PreferCheaper)
                return items;

            if (!LooksLikeFemaleContext(profile, normalizedMessage, previousProducts))
                return items;

            var filtered = items
                .Where(IsFemaleFriendlyCandidate)
                .ToList();

            return filtered;
        }

        private static bool LooksLikeFemaleContext(
            CustomerPreferenceProfile profile,
            string normalizedMessage,
            List<ProductSummaryDto> previousProducts)
        {
            var text = NormalizeText(normalizedMessage);

            if (profile.PrefersFemaleStyle)
                return true;

            if (!string.IsNullOrWhiteSpace(profile.Target) &&
                NormalizeText(profile.Target).Contains("nu"))
                return true;

            if (ContainsAny(text, "cho nu", "xe nu", "hop nu", "nu tinh", "ban nu", "con gai"))
                return true;

            return previousProducts != null &&
                   previousProducts.Any(x =>
                       ContainsAny(x.Ten,
                           "Vision",
                           "Attila",
                           "Shark",
                           "Latte",
                           "Grande",
                           "Janus",
                           "Lead",
                           "Zip"));
        }

        private static bool IsFemaleFriendlyCandidate(ProductSummaryDto product)
        {
            if (product == null)
                return false;

            var name = NormalizeText(product.Ten);
            var category = NormalizeCategory(product.Loai);

            if (ContainsAny(name,
                "winner",
                "exciter",
                "raider",
                "sonic",
                "husky",
                "cbr",
                "rebel",
                "axelo",
                "gd110",
                "galaxy",
                "star sr"))
            {
                return false;
            }

            if (category == "côn tay")
                return false;

            if (category == "xe ga")
                return true;

            if (ContainsAny(name,
                "vision",
                "janus",
                "latte",
                "grande",
                "lead",
                "zip",
                "attila",
                "shark",
                "elite",
                "address",
                "impulse",
                "angela",
                "passing"))
            {
                return true;
            }

            return false;
        }
        private static bool ContainsAny(string? source, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(source) || keywords == null || keywords.Length == 0)
                return false;

            var text = NormalizeText(source);

            foreach (var keyword in keywords)
            {
                if (text.Contains(NormalizeText(keyword)))
                    return true;
            }

            return false;
        }
        private static List<ProductSummaryDto> ApplyAllExclusions(
    IEnumerable<ProductSummaryDto> source,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            var items = source?.Where(x => x != null).ToList() ?? new List<ProductSummaryDto>();

            var excludedBrands = new HashSet<string>(intent.ExcludedBrands, StringComparer.OrdinalIgnoreCase);
            excludedBrands.UnionWith(profile.ExcludedBrands);

            var excludedCategories = new HashSet<string>(intent.ExcludedCategories, StringComparer.OrdinalIgnoreCase);
            excludedCategories.UnionWith(profile.ExcludedCategories);

            var excludedProducts = new HashSet<string>(intent.ExcludedProducts, StringComparer.OrdinalIgnoreCase);
            excludedProducts.UnionWith(profile.ExcludedProducts);

            return items.Where(x =>
                !excludedBrands.Contains(x.ThuongHieu ?? string.Empty) &&
                !excludedCategories.Any(ex => IsSameCategory(x.Loai, ex)) &&
                !excludedProducts.Contains(x.Ten ?? string.Empty))
                .ToList();
        }
        private sealed class RefinementSignals
        {
            public bool HasHardFilterChange { get; set; }
            public bool HasSoftPreferenceChange { get; set; }
            public bool PreferCheaper { get; set; }
            public bool PreferDifferent { get; set; }
            public bool PreferMoreStylish { get; set; }
            public bool WantsBroaderAlternatives { get; set; }
            public bool WantsLargeStorage { get; set; }
            public bool WantsFuelSaving { get; set; }
            public bool NeedsLowSeat { get; set; }
            public bool ForWork { get; set; }
            public bool ForSchool { get; set; }
            public bool PreferMoreExpensive { get; set; }
        }
        private sealed class CurrentResultPriceBand
        {
            public decimal? MinPrice { get; init; }
            public decimal? MaxPrice { get; init; }
            public decimal? MedianPrice { get; init; }
        }
    }
}
