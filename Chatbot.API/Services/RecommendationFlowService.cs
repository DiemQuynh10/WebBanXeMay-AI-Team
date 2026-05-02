using System.Text;
using Chatbot.API.Configurations;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Recommendation;
using Chatbot.API.Models.Responses;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;
using Chatbot.API.Tools;
using Microsoft.Extensions.Options;
using static Chatbot.API.Models.Intent.ParsedIntent;

namespace Chatbot.API.Services
{
    public class RecommendationFlowService : IRecommendationFlowService
    {
        private readonly IWebBanXeMayToolClient _toolClient;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IProductRecommendationService _productRecommendationService;
        private readonly IRecommendationClarificationService _recommendationClarificationService;
        private readonly ILogger<RecommendationFlowService> _logger;
        private readonly IRecommendationLLMService _recommendationLLMService;
        private readonly IReplyStyleService _replyStyleService;
        private readonly IReplyRewriteService _replyRewriteService;
        private readonly ReplyRewriteOptions _replyRewriteOptions;
        private readonly IRecommendationScoringService _recommendationScoringService;
        public RecommendationFlowService(
            IWebBanXeMayToolClient toolClient,
            IConversationPreferenceService conversationPreferenceService,
            IProductRecommendationService productRecommendationService,
            IRecommendationClarificationService recommendationClarificationService,
            IRecommendationLLMService recommendationLLMService,
            IReplyStyleService replyStyleService,
            IReplyRewriteService replyRewriteService,
            IRecommendationScoringService recommendationScoringService,
            IOptions<ReplyRewriteOptions> replyRewriteOptions,
            ILogger<RecommendationFlowService> logger)
        {
            _toolClient = toolClient;
            _conversationPreferenceService = conversationPreferenceService;
            _productRecommendationService = productRecommendationService;
            _recommendationClarificationService = recommendationClarificationService;
            _recommendationLLMService = recommendationLLMService;
            _replyStyleService = replyStyleService;
            _replyRewriteService = replyRewriteService;
            _recommendationScoringService = recommendationScoringService;
            _replyRewriteOptions = replyRewriteOptions.Value;
            _logger = logger;
        }

        public async Task<ChatResponse?> HandleAsync(
            string conversationId,
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var effectiveIntent = BuildEffectiveIntent(intent, profile, normalizedMessage);
            if (string.Equals(effectiveIntent.FollowUpType, "pick_best", StringComparison.OrdinalIgnoreCase))
            {
                var picked = await PickBestFromComparedProductsAsync(
                    conversationId,
                    normalizedMessage,
                    effectiveIntent,
                    profile);

                if (picked != null)
                    return picked;
            }
            if (string.Equals(effectiveIntent.FollowUpType, "restart_recommendation", StringComparison.OrdinalIgnoreCase))
            {
                effectiveIntent.IntentType = "recommend";
                effectiveIntent.IsFollowUp = true;
                effectiveIntent.IsOpenRecommendation = false;

                effectiveIntent.MentionedProducts.Clear();

            }
            bool hasEnoughSignals =
                _recommendationClarificationService.HasEnoughSignalsForDirectRecommendation(
                    normalizedMessage,
                    effectiveIntent,
                    profile);
            var requestedBrand = FirstNonEmpty(effectiveIntent.Brand, profile.PreferredBrand);

            if (!string.IsNullOrWhiteSpace(intent.Brand))
            {
                effectiveIntent.ExcludedBrands.RemoveWhere(x =>
                    string.Equals(x, intent.Brand, StringComparison.OrdinalIgnoreCase));

                profile.ExcludedBrands.RemoveWhere(x =>
     string.Equals(x, intent.Brand, StringComparison.OrdinalIgnoreCase));

                requestedBrand = intent.Brand;
                effectiveIntent.Brand = intent.Brand;
            }

            var effectiveCategory = FirstNonEmpty(effectiveIntent.Category, profile.PreferredCategory);
            var (minPrice, maxPrice) = ResolveRecommendationPriceRange(effectiveIntent, profile);

            bool shouldClarify = ShouldAskClarification(
                normalizedMessage,
                effectiveIntent,
                profile,
                requestedBrand,
                effectiveCategory,
                minPrice,
                maxPrice,
                hasEnoughSignals);

            if (shouldClarify)
            {
                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    Reply = _recommendationClarificationService.BuildClarificationQuestion(
                        normalizedMessage,
                        effectiveIntent,
                        profile)
                };
            }

            var candidateBuckets = await CollectCandidatesAsync(
    conversationId,
    effectiveIntent,
    profile,
    requestedBrand,
    effectiveCategory,
    minPrice,
    maxPrice);

            var items = MergeCandidateBuckets(candidateBuckets);

            if (items.Count == 0 && HasExclusionIntent(effectiveIntent))
            {
                items = await LoadBroadCandidatesForExclusionAsync(
                    conversationId,
                    effectiveIntent,
                    profile,
                    effectiveCategory,
                    minPrice,
                    maxPrice);
            }

            if (items.Count == 0)
            {

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = _replyStyleService.BuildRecommendationNoMatchReply(
                        effectiveIntent,
                        profile,
                        effectiveCategory)
                };
            }
            items = ApplyPreferenceAwareFiltering(
                items,
                effectiveIntent,
                profile,
                requestedBrand,
                effectiveCategory,
                conversationId,
                normalizedMessage);
            if (items.Count == 0 && HasExclusionIntent(effectiveIntent))
            {
                items = await LoadBroadCandidatesForExclusionAsync(
                    conversationId,
                    effectiveIntent,
                    profile,
                    effectiveCategory,
                    minPrice,
                    maxPrice);
            }

            var scored = _recommendationScoringService.ScoreProducts(
    items,
    effectiveIntent,
    profile,
    normalizedMessage,
    take: 6);

            var rankedByRule = scored
                .Select(x => x.Product)
                .ToList();

            _logger.LogInformation(
                "Recommendation rule ranking completed. ConversationId={ConversationId}, RankedByRuleCount={RankedByRuleCount}",
                conversationId,
                rankedByRule.Count);
            var llmReasonMap = new Dictionary<int, string>();
            await TryApplyLlmReasonsOnlyAsync(
     conversationId,
     normalizedMessage,
     effectiveIntent,
     profile,
     rankedByRule,
     llmReasonMap);
            var ranked = EnforceFinalRecommendationGuards(
                rankedByRule,
                effectiveIntent,
                profile,
                effectiveCategory,
                normalizedMessage);
            if (ranked == null || ranked.Count == 0)
            {
                ranked = ApplyConversationAwareReorder(
                    rankedByRule,
                    effectiveIntent,
                    profile,
                    normalizedMessage);
            }
            ranked = EnsureDiversity(ranked, requestedBrand);

            ranked = EnforceFinalRecommendationGuards(
    ranked,
    effectiveIntent,
    profile,
    effectiveCategory,
    normalizedMessage);
            if (ranked.Count < 3)
            {
                var backup = ApplyExclusions(rankedByRule, effectiveIntent, profile);

                backup = EnforceFinalRecommendationGuards(
    backup,
    effectiveIntent,
    profile,
    effectiveCategory,
    normalizedMessage);

                var strictBackup = ProductPriceFilterHelper.ApplyStrictPriceFilter(backup, effectiveIntent);
                if (strictBackup.Count > 0)
                {
                    backup = strictBackup;
                }

                foreach (var item in backup)
                {
                    if (ranked.Count >= 3)
                        break;

                    if (!ranked.Any(x => x.Id == item.Id))
                        ranked.Add(item);
                }
            }
            ranked = FinalizeRecommendationRanking(
     ranked,
     rankedByRule,
     effectiveIntent,
     intent,
     profile,
     effectiveCategory,
     normalizedMessage);

            if (ranked.Count == 0)
            {
                bool isChangeProduct =
                    effectiveIntent.Action == ConversationAction.ChangeProduct ||
                    string.Equals(effectiveIntent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase) ||
                    effectiveIntent.ExcludePreviousProducts;

                return new ChatResponse
                {
                    Success = true,
                    ConversationId = conversationId,
                    UsedAI = false,
                    UsedTool = ToolNames.GetProductsByFilters,
                    Reply = isChangeProduct
                       ? "Mình đã giữ các tiêu chí cũ và thử tìm mẫu khác, nhưng hiện chưa có mẫu nào khớp hoàn toàn. Bạn có thể nới nhẹ ngân sách, đổi hãng hoặc bỏ bớt một tiêu chí để mình lọc tiếp nhé."
                        : _replyStyleService.BuildRecommendationNoMatchReply(
                            effectiveIntent,
                            profile,
                            effectiveCategory)
                };
            }

            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(conversationId, ranked);
            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                ranked,
                BuildRecommendationReasonTag(effectiveIntent));

            var breakdownMap = scored.ToDictionary(x => x.Product.Id, x => x.Breakdown);

            var anchor = ranked.FirstOrDefault();
            var recommendationBuckets = BucketRecommendations(ranked);
            var bucketNarratives = BuildBucketNarratives(recommendationBuckets);
            bool isFreshCategoryNeed =
    normalizedMessage.Contains("tư vấn", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("tu van", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("gợi ý", StringComparison.OrdinalIgnoreCase) ||
    normalizedMessage.Contains("goi y", StringComparison.OrdinalIgnoreCase);

            if (isFreshCategoryNeed &&
                !string.IsNullOrWhiteSpace(effectiveIntent.Category))
            {
                profile.LastAnswerMode = "fresh_consultation_category";
            }
            var draftReply = _replyStyleService.BuildClusteredRecommendationReply(
    ranked,
    effectiveIntent,
    profile,
    anchor,
    bucketNarratives,
    normalizedMessage,
    item =>
    {
        if (llmReasonMap.TryGetValue(item.Id, out var llmReason) &&
    !string.IsNullOrWhiteSpace(llmReason) &&
    IsSafeLlmReasonForProduct(item, llmReason, ranked))
        {
            return new List<string> { llmReason.Trim().TrimEnd('.') };
        }

        return new List<string>
{
    BuildNaturalRecommendationReason(item, effectiveIntent, profile, normalizedMessage)
};
    });

            var reply = await RewriteIfEnabledAsync(normalizedMessage, draftReply);

            _logger.LogInformation(
                "Recommendation final result. ConversationId={ConversationId}, FinalProductCount={FinalProductCount}, Brand={Brand}, Category={Category}, MinPrice={MinPrice}, MaxPrice={MaxPrice}",
                conversationId,
                ranked.Count,
                requestedBrand,
                effectiveCategory,
                minPrice,
                maxPrice);

            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ToolNames.GetProductsByFilters,
                Reply = reply,
                Products = ChatProductCardMapper.MapMany(ranked, 3)
            };
        }

        private static ParsedIntent BuildEffectiveIntent(
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string normalizedMessage)
        {
            var effective = intent.Clone();
            bool shouldIgnoreOldProfile = ShouldIgnoreOldProfileForFreshPriceAsk(
    normalizedMessage,
    effective);
            bool shouldIgnoreOldCategory = ShouldIgnoreOldCategoryForFreshTargetAsk(
     normalizedMessage,
     effective);

            var text = NormalizeText(normalizedMessage);
            ApplyInlineExcludedBrandGuard(text, effective);
            bool currentTurnHasCategory = ContainsAny(text,
                "xe so", "xe số",
                "xe ga",
                "tay ga",
                "con tay", "côn tay");

            bool currentTurnHasBrand = ContainsAny(text,
                "honda", "yamaha", "suzuki", "sym", "piaggio");

            effective.Brand =
     effective.HasFreshConsultationSignal ||
     shouldIgnoreOldProfile ||
     (currentTurnHasCategory && !currentTurnHasBrand)
         ? effective.Brand
         : FirstNonEmpty(effective.Brand, profile.PreferredBrand);

            effective.Category =
     effective.HasFreshConsultationSignal ||
     shouldIgnoreOldProfile ||
     shouldIgnoreOldCategory
         ? effective.Category
         : FirstNonEmpty(effective.Category, profile.PreferredCategory);

            effective.Target = shouldIgnoreOldProfile
                ? effective.Target
                : FirstNonEmpty(effective.Target, profile.Target);

            if (!effective.PriceMin.HasValue)
                effective.PriceMin = profile.PriceMin;

            if (!effective.PriceMax.HasValue)
                effective.PriceMax = profile.PriceMax;

            if (!effective.TargetPrice.HasValue)
                effective.TargetPrice = profile.TargetPrice;

            if (effective.FilterType == PriceFilterType.None && profile.FilterType != PriceFilterType.None)
                effective.FilterType = profile.FilterType;

            if (!shouldIgnoreOldProfile)
            {
                effective.ForWork |= profile.ForWork;
                effective.ForSchool |= profile.ForSchool;
                effective.ForCity |= profile.ForCity;
                effective.ForTour |= profile.ForTour;
                effective.WantsFuelSaving |= profile.WantsFuelSaving;
                effective.WantsLargeStorage |= profile.WantsLargeStorage;
                effective.WantsEasyControl |= profile.WantsEasyControl;
                effective.NeedsLowSeat |= profile.NeedsLowSeat;
                effective.PrefersMaleStyle |= profile.PrefersMaleStyle;
                effective.PrefersFemaleStyle |= profile.PrefersFemaleStyle;
            }

            if (!effective.HeightCm.HasValue)
                effective.HeightCm = profile.HeightCm;

            if (effective.RequestedStyles.Count == 0 && profile.RequestedStyles.Count > 0)
            {
                effective.RequestedStyles = new HashSet<string>(
                    profile.RequestedStyles.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (ShouldCarryExcludedBrands(normalizedMessage, effective, profile))
            {
                effective.ExcludedBrands = new HashSet<string>(
                    profile.ExcludedBrands.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (ShouldCarryExcludedCategories(normalizedMessage, effective, profile))
            {
                effective.ExcludedCategories = new HashSet<string>(
                    profile.ExcludedCategories.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }

            if (!effective.HasFreshConsultationSignal &&
                effective.ExcludedProducts.Count == 0 &&
                profile.ExcludedProducts.Count > 0)
            {
                effective.ExcludedProducts = new HashSet<string>(
                    profile.ExcludedProducts.Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
            }
            if (!string.IsNullOrWhiteSpace(effective.Target))
            {
                var target = effective.Target.Trim().ToLowerInvariant();

                if (target.Contains("nữ") || target.Contains("nu"))
                {
                    effective.PrefersFemaleStyle = true;
                }

                if (target.Contains("nam"))
                {
                    effective.PrefersMaleStyle = true;
                }
            }
            if (LooksLikeCheaperFollowUp(normalizedMessage) &&
    HasFemaleFriendlyPreviousRecommendation(profile))
            {
                effective.PrefersFemaleStyle = true;
                effective.PrefersMaleStyle = false;

                if (string.IsNullOrWhiteSpace(effective.Target))
                    effective.Target = "nữ";
            }
            bool isChangeProduct =
    intent.Action == ConversationAction.ChangeProduct ||
    string.Equals(intent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase) ||
    intent.ExcludePreviousProducts;

            if (isChangeProduct)
            {
                effective.IntentType = "refine";
                effective.IsFollowUp = true;
                effective.FollowUpType = "change_product";
                effective.Action = ConversationAction.ChangeProduct;
                effective.KeepConstraints = true;
                effective.ExcludePreviousProducts = true;

                effective.ExcludedProducts ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var productName in profile.LastRecommendedProducts ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(productName))
                        effective.ExcludedProducts.Add(productName.Trim());
                }

                foreach (var productName in profile.CurrentRecommendedProducts ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(productName))
                        effective.ExcludedProducts.Add(productName.Trim());
                }
            }

            if (intent.IsFollowUp &&
     string.Equals(intent.FollowUpType, "other", StringComparison.OrdinalIgnoreCase))
            {
                effective.ExcludedProducts ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                effective.ExcludedBrands ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var productName in profile.LastRecommendedProducts ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        effective.ExcludedProducts.Add(productName.Trim());

                        var brand = ExtractBrandFromProductName(productName);
                        if (!string.IsNullOrWhiteSpace(brand))
                            effective.ExcludedBrands.Add(brand);
                    }
                }
            }

            if (intent.IsFollowUp &&
     string.Equals(intent.FollowUpType, "other_brand", StringComparison.OrdinalIgnoreCase))
            {
                effective.Brand = null;

                effective.ExcludedBrands ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var productName in profile.LastRecommendedProducts ?? new List<string>())
                {
                    var brand = ExtractBrandFromProductName(productName);
                    if (!string.IsNullOrWhiteSpace(brand))
                        effective.ExcludedBrands.Add(brand);
                }
            }
            return effective;
        }

        private bool ShouldAskClarification(
     string normalizedMessage,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string? requestedBrand,
     string? effectiveCategory,
     decimal? minPrice,
     decimal? maxPrice,
     bool hasEnoughSignals)
        {
            if (hasEnoughSignals)
                return false;

            bool hasAnyConcreteAnchor =
                !string.IsNullOrWhiteSpace(requestedBrand) ||
                !string.IsNullOrWhiteSpace(effectiveCategory) ||
                minPrice.HasValue ||
                maxPrice.HasValue ||
                intent.TargetPrice.HasValue ||
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.ForWork ||
                intent.ForSchool ||
                intent.ForCity ||
                intent.ForTour ||
                intent.WantsFuelSaving ||
                intent.WantsLargeStorage ||
                intent.WantsEasyControl ||
                intent.NeedsLowSeat ||
                intent.HeightCm.HasValue ||
                intent.RequestedStyles.Count > 0 ||
                intent.MentionedProducts.Count > 0 ||
                intent.PrefersFemaleStyle ||
                intent.PrefersMaleStyle;
            var text = NormalizeText(normalizedMessage);
            bool hasExclusionIntent = HasExclusionIntent(intent);

            bool looksLikeFreshRecommendationWithExclusion =
                hasExclusionIntent &&
                ContainsAny(text,
                    "tu van",
                    "tư vấn",
                    "goi y",
                    "gợi ý",
                    "tu van lai",
                    "tư vấn lại",
                    "chon xe",
                    "chọn xe");

            if (hasExclusionIntent &&
                (profile.HasActiveRecommendationContext || looksLikeFreshRecommendationWithExclusion))
            {
                return false;
            }
            bool isTargetFollowUp =
                ContainsAny(text,
                    "cho nu",
                    "cho nữ",
                    "xe nu",
                    "xe nữ",
                    "hop nu",
                    "hợp nữ",
                    "nu tinh",
                    "nữ tính",
                    "cho nam",
                    "xe nam",
                    "hop nam",
                    "hợp nam");

            if (isTargetFollowUp && profile.HasActiveRecommendationContext)
            {
                return false; 
            }
            return !hasAnyConcreteAnchor;
        }
        private async Task<List<List<ProductSummaryDto>>> CollectCandidatesAsync(
            string conversationId,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string? requestedBrand,
            string? effectiveCategory,
            decimal? minPrice,
            decimal? maxPrice)
        {
            var buckets = new List<List<ProductSummaryDto>>();

            var strict = await GetCandidatesAsync(
                requestedBrand,
                effectiveCategory,
                minPrice,
                maxPrice,
                take: 30);

            LogCandidateBucket(conversationId, "strict", requestedBrand, effectiveCategory, minPrice, maxPrice, strict.Count);
            buckets.Add(strict);

            bool isChangeProduct =
    intent.Action == ConversationAction.ChangeProduct ||
    string.Equals(intent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase) ||
    intent.ExcludePreviousProducts;

            if (isChangeProduct)
            {
                var (relaxedMinPrice, relaxedMaxPrice) = RelaxPriceRangeForChangeProduct(minPrice, maxPrice);

                bool priceWasRelaxed =
                    relaxedMinPrice != minPrice ||
                    relaxedMaxPrice != maxPrice;

                if (priceWasRelaxed)
                {
                    var relaxedByPrice = await GetCandidatesAsync(
                        requestedBrand,
                        effectiveCategory,
                        relaxedMinPrice,
                        relaxedMaxPrice,
                        take: 30);

                    LogCandidateBucket(
                        conversationId,
                        "change_product_relaxed_price",
                        requestedBrand,
                        effectiveCategory,
                        relaxedMinPrice,
                        relaxedMaxPrice,
                        relaxedByPrice.Count);

                    if (relaxedByPrice.Count > 0)
                        buckets.Add(relaxedByPrice);
                }
            }
            if (strict.Count == 0 && !string.IsNullOrWhiteSpace(effectiveCategory))
            {
                var noCategory = await GetCandidatesAsync(
                    requestedBrand,
                    null,
                    minPrice,
                    maxPrice,
                    take: 30);

                LogCandidateBucket(conversationId, "without_category", requestedBrand, null, minPrice, maxPrice, noCategory.Count);
                buckets.Add(noCategory);
            }

            if (strict.Count == 0)
            {
                var relaxed = await TryGetBrandRelaxedCandidatesAsync(intent, profile, effectiveCategory);
                LogCandidateBucket(conversationId, "relaxed", requestedBrand, effectiveCategory, minPrice, maxPrice, relaxed.Count);
                if (relaxed.Count > 0)
                    buckets.Add(relaxed);
            }

            if (ShouldTryCategoryFallback(intent, profile, effectiveCategory))
            {
                var categoryFallback = await GetCandidatesAsync(
                    null,
                    effectiveCategory,
                    minPrice,
                    maxPrice,
                    take: 30);

                LogCandidateBucket(conversationId, "category_fallback", null, effectiveCategory, minPrice, maxPrice, categoryFallback.Count);
                if (categoryFallback.Count > 0)
                    buckets.Add(categoryFallback);
            }

            return buckets;
        }

        private void LogCandidateBucket(
            string conversationId,
            string step,
            string? brand,
            string? category,
            decimal? minPrice,
            decimal? maxPrice,
            int count)
        {
            _logger.LogInformation(
                "Recommendation candidate bucket. ConversationId={ConversationId}, Step={Step}, Brand={Brand}, Category={Category}, MinPrice={MinPrice}, MaxPrice={MaxPrice}, CandidateCount={CandidateCount}",
                conversationId,
                step,
                brand,
                category,
                minPrice,
                maxPrice,
                count);
        }

        private static bool ShouldTryCategoryFallback(
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string? effectiveCategory)
        {
            if (string.IsNullOrWhiteSpace(effectiveCategory))
                return false;

            bool explicitBrand = !string.IsNullOrWhiteSpace(intent.Brand);
            bool hasCategoryIntent =
                !string.IsNullOrWhiteSpace(intent.Category) ||
                !string.IsNullOrWhiteSpace(profile.PreferredCategory);

            return !explicitBrand && hasCategoryIntent;
        }

        private static List<ProductSummaryDto> MergeCandidateBuckets(List<List<ProductSummaryDto>> buckets)
        {
            var byId = new Dictionary<int, ProductSummaryDto>();

            foreach (var bucket in buckets)
            {
                foreach (var item in bucket.Where(x => x != null))
                {
                    if (!byId.ContainsKey(item.Id))
                        byId[item.Id] = item;
                }
            }

            return byId.Values.ToList();
        }

        private List<ProductSummaryDto> ApplyPreferenceAwareFiltering(
            List<ProductSummaryDto> items,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string? requestedBrand,
            string? effectiveCategory,
            string conversationId,
            string normalizedMessage)
        {
            if (items.Count == 0)
                return items;
            var strictPriceFiltered = ProductPriceFilterHelper.ApplyStrictPriceFilter(items, intent);

            if (strictPriceFiltered.Count > 0)
            {
                items = strictPriceFiltered;
            }
            else if (intent.Action == ConversationAction.ChangeProduct ||
                     string.Equals(intent.FollowUpType, "change_product", StringComparison.OrdinalIgnoreCase) ||
                     intent.ExcludePreviousProducts)
            {
                _logger.LogInformation(
                    "Skip strict price empty result for ChangeProduct. Keep relaxed candidates. ConversationId={ConversationId}",
                    conversationId);
            }
            else
            {
                _logger.LogInformation(
                    "Strict price filter produced no items. Keep relaxed candidates for ranking. ConversationId={ConversationId}, RequestedBrand={Brand}, RequestedCategory={Category}",
                    conversationId,
                    requestedBrand,
                    effectiveCategory);
            }
            items = ApplyExclusions(items, intent, profile);

            items = ApplyCategoryGuard(
                items,
                intent,
                profile,
                effectiveCategory,
                normalizedMessage,
                conversationId);

            if (intent.MentionedProducts.Count > 0 && items.Count >= 4)
            {
                var mentionedSet = intent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeToken)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var nonMentioned = items
                    .Where(x => !mentionedSet.Contains(NormalizeToken(x.Ten)))
                    .ToList();

                if (nonMentioned.Count >= 2)
                    items = nonMentioned;
            }

            return items;
        }


        private List<ProductSummaryDto> ApplyCategoryGuard(
     List<ProductSummaryDto> items,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string? effectiveCategory,
     string normalizedMessage,
     string conversationId)
        {
            if (items == null || items.Count == 0)
                return items ?? new List<ProductSummaryDto>();

            var desiredCategory = NormalizeCategory(effectiveCategory);

            if (!string.IsNullOrWhiteSpace(desiredCategory))
            {
                var sameCategory = items
                    .Where(x => string.Equals(
                        NormalizeCategory(x.Loai),
                        desiredCategory,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (sameCategory.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(intent.Category))
                    {
                        _logger.LogInformation(
                            "Explicit category requested but no same-category candidates remain. ConversationId={ConversationId}, Category={Category}",
                            conversationId,
                            desiredCategory);

                        return new List<ProductSummaryDto>();
                    }

                    return items;
                }

                if (desiredCategory == "xe so")
                {
                    var practicalUnderbones = sameCategory
                        .Where(x =>
                        {
                            var name = NormalizeText(x.Ten);
                            if (x.Gia > 50_000_000)
                                return false;

                            return !ContainsAny(name,
                                "cub",
                                "super cub",
                                "125",
                                "gd110",
                                "axelo",
                                "winner",
                                "exciter",
                                "raider",
                                "sonic",
                                "husky",
                                "cbr",
                                "rebel");
                        })
                        .ToList();

                    if (practicalUnderbones.Count > 0)
                    {
                        _logger.LogInformation(
                            "Practical underbone guard removed sport/manual candidates. ConversationId={ConversationId}, Before={Before}, After={After}",
                            conversationId,
                            sameCategory.Count,
                            practicalUnderbones.Count);

                        return practicalUnderbones;
                    }
                }

                _logger.LogInformation(
                    "Category guard kept same-category candidates. ConversationId={ConversationId}, Category={Category}, Before={Before}, After={After}",
                    conversationId,
                    desiredCategory,
                    items.Count,
                    sameCategory.Count);

                return sameCategory;
            }

            if (ShouldSuppressSportManualForOpenAsk(intent, profile, normalizedMessage))
            {
                var mainstream = items
                    .Where(x => !LooksLikeSportManualCandidate(x))
                    .ToList();

                if (mainstream.Count > 0)
                {
                    _logger.LogInformation(
                        "Open recommendation guard removed sport/manual candidates. ConversationId={ConversationId}, Before={Before}, After={After}",
                        conversationId,
                        items.Count,
                        mainstream.Count);

                    return mainstream;
                }
            }

            return items;
        }
        private static bool ShouldSuppressSportManualForOpenAsk(
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage)
        {
            var desiredCategory = NormalizeCategory(FirstNonEmpty(intent.Category, profile.PreferredCategory));
            if (!string.IsNullOrWhiteSpace(desiredCategory))
                return false;

            var text = NormalizeText(normalizedMessage);

            bool explicitlySporty =
                intent.PrefersMaleStyle ||
                profile.PrefersMaleStyle ||
                ContainsAny(text, "con tay", "côn tay", "the thao", "thể thao", "toc do", "tốc độ", "manh", "mạnh", "ca tinh", "cá tính", "winner", "exciter", "raider", "sonic");

            return !explicitlySporty;
        }

        private static bool LooksLikeSportManualCandidate(ProductSummaryDto product)
        {
            var category = NormalizeCategory(product.Loai);
            var name = product.Ten ?? string.Empty;

            return category == "con tay" ||
       ContainsAny(name,
           "Winner",
           "Exciter",
           "Raider",
           "Sonic",
           "Husky",
           "CBR",
           "Rebel",
           "GD110",
           "Axelo");
        }

        private static List<ProductSummaryDto> ApplyExclusions(
     List<ProductSummaryDto> items,
     ParsedIntent intent,
     CustomerPreferenceProfile profile)
        {
            var excludedBrands = new HashSet<string>(intent.ExcludedBrands, StringComparer.OrdinalIgnoreCase);
            excludedBrands.UnionWith(profile.ExcludedBrands);

            var excludedCategories = new HashSet<string>(intent.ExcludedCategories, StringComparer.OrdinalIgnoreCase);
            excludedCategories.UnionWith(profile.ExcludedCategories);

            var excludedProducts = new HashSet<string>(intent.ExcludedProducts, StringComparer.OrdinalIgnoreCase);
            excludedProducts.UnionWith(profile.ExcludedProducts);

            if (excludedBrands.Count == 0 &&
                excludedCategories.Count == 0 &&
                excludedProducts.Count == 0)
            {
                return items;
            }

            var filtered = items.Where(item =>
                !excludedBrands.Contains(item.ThuongHieu ?? string.Empty) &&
                !excludedCategories.Any(ex => string.Equals(
                    NormalizeCategory(item.Loai),
                    NormalizeCategory(ex),
                    StringComparison.OrdinalIgnoreCase)) &&
                !excludedProducts.Contains(item.Ten ?? string.Empty))
                .ToList();

            return filtered;
        }
        private static List<ProductSummaryDto> ApplyConversationAwareReorder(
    IReadOnlyList<ProductSummaryDto> products,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string normalizedMessage)
        {
            if (products == null || products.Count == 0)
                return new List<ProductSummaryDto>();

            bool wantsCheaper = LooksLikeCheaperFollowUp(normalizedMessage);
            bool explicitCategoryChange = !string.IsNullOrWhiteSpace(intent.Category);
            bool explicitBrandChange = !string.IsNullOrWhiteSpace(intent.Brand);

            var ranked = products
                .Select(p => new
                {
                    Product = p,
                    Score = ComputeConversationAwareScore(
                        p,
                        intent,
                        profile,
                        wantsCheaper,
                        explicitCategoryChange,
                        explicitBrandChange)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Product.Gia)
                .Select(x => x.Product)
                .ToList();

            return ranked;
        }

        private static int ComputeConversationAwareScore(
            ProductSummaryDto product,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            bool wantsCheaper,
            bool explicitCategoryChange,
            bool explicitBrandChange)
        {
            int score = 0;

            var name = product.Ten ?? string.Empty;
            var targetPrice =
    intent.TargetPrice ??
    profile.TargetPrice;

            if (targetPrice.HasValue)
            {
                var diff = Math.Abs(product.Gia - targetPrice.Value);

                if (diff <= 1_000_000m)
                    score += 40;
                else if (diff <= 3_000_000m)
                    score += 26;
                else if (diff <= 5_000_000m)
                    score += 12;
                else if (diff >= 10_000_000m)
                    score -= 30;
            }
            var category = NormalizeCategory(product.Loai);
            var brand = product.ThuongHieu ?? string.Empty;
            bool prefersFemale =
    intent.PrefersFemaleStyle ||
    profile.PrefersFemaleStyle ||
    (!string.IsNullOrWhiteSpace(intent.Target) && intent.Target.Contains("nữ", StringComparison.OrdinalIgnoreCase)) ||
    (!string.IsNullOrWhiteSpace(profile.Target) && profile.Target.Contains("nữ", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(intent.Brand) &&
                string.Equals(brand, intent.Brand, StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }

            if (!string.IsNullOrWhiteSpace(profile.PreferredBrand) &&
     string.Equals(brand, profile.PreferredBrand, StringComparison.OrdinalIgnoreCase) &&
     !intent.ExcludedBrands.Contains(profile.PreferredBrand))
            {
                score += 10;
            }

            if (!explicitCategoryChange &&
                !string.IsNullOrWhiteSpace(profile.PreferredCategory) &&
                string.Equals(category, NormalizeCategory(profile.PreferredCategory), StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }


            if (intent.ForWork || profile.ForWork)
            {
                score += GetWorkUsageBoost(name, category);
            }

            if (intent.WantsFuelSaving || profile.WantsFuelSaving)
            {
                score += GetFuelSavingBoost(name, category);
            }

            if (intent.WantsLargeStorage || profile.WantsLargeStorage)
            {
                score += GetStorageBoost(name, category);
            }

            if (intent.NeedsLowSeat || profile.NeedsLowSeat || intent.WantsEasyControl || profile.WantsEasyControl)
            {
                score += GetLowSeatBoost(name, category);
            }

            if (wantsCheaper)
            {
                var referencePrice =
                    intent.TargetPrice ??
                    profile.TargetPrice ??
                    intent.PriceMax ??
                    profile.PriceMax;

                if (referencePrice.HasValue)
                {
                    if (product.Gia < referencePrice.Value)
                        score += 24;

                    // "rẻ hơn chút" thì không nên tụt quá sâu xuống nhóm quá thấp
                    if (product.Gia < referencePrice.Value * 0.60m)
                        score -= 35;

                    if (product.Gia >= referencePrice.Value - 4_000_000m)
                        score += 12;
                }
                if (ContainsAny(name, "50"))
                    score -= 25;
            }

            if (!explicitBrandChange &&
     !string.IsNullOrWhiteSpace(profile.PreferredBrand) &&
     string.Equals(brand, profile.PreferredBrand, StringComparison.OrdinalIgnoreCase) &&
     !intent.ExcludedBrands.Contains(profile.PreferredBrand))
            {
                score += 5;
            }
            bool hasPriceAnchor =
    intent.TargetPrice.HasValue ||
    intent.PriceMin.HasValue ||
    intent.PriceMax.HasValue ||
    profile.TargetPrice.HasValue ||
    profile.PriceMin.HasValue ||
    profile.PriceMax.HasValue;

            if (!hasPriceAnchor)
            {
                // Khi user chưa nói ngân sách, đừng ưu tiên xe quá cao giá
                if (product.Gia >= 45_000_000m)
                    score -= 32;
                else if (product.Gia >= 40_000_000m)
                    score -= 18;

                // Ưu tiên nhẹ nhóm phổ thông dễ chốt hơn
                if (product.Gia <= 35_000_000m)
                    score += 10;
            }

            if ((intent.ForWork || profile.ForWork) && !hasPriceAnchor)
            {
                // Với case đi làm nhưng chưa có ngân sách, tránh đẩy các mẫu hơi niche
                if (ContainsAny(name, "Burgman", "PCX", "SH"))
                    score -= 26;

                // Ưu tiên nhóm phổ thông, thực dụng hơn
                if (ContainsAny(name, "Vision", "Future", "Freego", "Air Blade", "Wave"))
                    score += 14;
            }
            if (prefersFemale)
            {
                if (string.Equals(category, "xe ga", StringComparison.OrdinalIgnoreCase))
                    score += 24;

                if (ContainsAny(name, "Vision", "Latte", "Grande", "Janus", "Zip", "Lead"))
                    score += 36;

                if (ContainsAny(name, "Air Blade"))
                    score += 12;

                if (ContainsAny(name, "Future", "Wave") &&
                    !ContainsAny(NormalizeText(intent.Category ?? profile.PreferredCategory ?? ""), "xe so"))
                    score -= 24;

                if (ContainsAny(name, "Husky", "Winner", "Exciter", "Raider", "Sonic"))
                    score -= 40;

                if (ContainsAny(name, "PCX", "SH", "CBR", "Rebel"))
                    score -= 24;
            }
            return score;
        }

        private static int GetWorkUsageBoost(string productName, string? normalizedCategory)
        {
            if (ContainsAny(productName, "Future", "Air Blade", "Vision", "Freego", "Lead"))
                return 36;

            if (ContainsAny(productName, "Wave", "Sirius"))
                return 20;

            if (string.Equals(normalizedCategory, "xe ga", StringComparison.OrdinalIgnoreCase))
                return 14;

            if (string.Equals(normalizedCategory, "xe so", StringComparison.OrdinalIgnoreCase))
                return 12;

            return 0;
        }

        private static int GetFuelSavingBoost(string productName, string? normalizedCategory)
        {
            if (ContainsAny(productName, "Vision", "Wave", "Future", "Sirius"))
                return 28;

            if (string.Equals(normalizedCategory, "xe so", StringComparison.OrdinalIgnoreCase))
                return 16;

            return 0;
        }

        private static int GetStorageBoost(string productName, string? normalizedCategory)
        {
            if (ContainsAny(productName, "Lead", "Freego", "Air Blade", "Latte"))
                return 28;

            if (string.Equals(normalizedCategory, "xe ga", StringComparison.OrdinalIgnoreCase))
                return 12;

            return 0;
        }

        private static int GetLowSeatBoost(string productName, string? normalizedCategory)
        {
            if (ContainsAny(productName, "Vision", "Janus", "Latte", "Zip"))
                return 26;

            if (string.Equals(normalizedCategory, "xe ga", StringComparison.OrdinalIgnoreCase))
                return 10;

            return 0;
        }

        private static bool LooksLikeCheaperFollowUp(string message)
        {
            var text = NormalizeText(message);
            return text.Contains("re hon") ||
                   text.Contains("re hon chut") ||
                   text.Contains("xuong") ||
                   text.Contains("mem hon") ||
                   text.Contains("thap hon");
        }
        private static bool LooksLikeUsageOnlyRequest(ParsedIntent intent, CustomerPreferenceProfile profile)
        {
            bool hasUsage =
                intent.ForWork || intent.ForSchool || intent.ForCity || intent.ForTour ||
                profile.ForWork || profile.ForSchool || profile.ForCity || profile.ForTour;

            bool hasPriceAnchor =
                intent.TargetPrice.HasValue || intent.PriceMin.HasValue || intent.PriceMax.HasValue ||
                profile.TargetPrice.HasValue || profile.PriceMin.HasValue || profile.PriceMax.HasValue;

            return hasUsage && !hasPriceAnchor;
        }

        private static string NormalizeCategory(string? category)
        {
            var text = NormalizeText(category);

            if (text.Contains("ga"))
                return "xe ga";

            if (text.Contains("so"))
                return "xe so";

            if (text.Contains("con"))
                return "con tay";

            return text;
        }

        private static bool ContainsAny(string? text, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
                return false;

            return keywords.Any(k =>
                !string.IsNullOrWhiteSpace(k) &&
                text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
        private static bool ShouldCarryExcludedBrands(
    string normalizedMessage,
    ParsedIntent effective,
    CustomerPreferenceProfile profile)
        {
            if (effective == null || profile == null)
                return false;

            if (effective.ExcludedBrands.Count > 0)
                return false;

            if (profile.ExcludedBrands == null || profile.ExcludedBrands.Count == 0)
                return false;

            var text = NormalizeText(normalizedMessage);

            bool userExplicitlyPickedBrand =
                !string.IsNullOrWhiteSpace(effective.Brand) ||
                ContainsAny(text, "honda", "yamaha", "suzuki", "sym", "piaggio");

            if (userExplicitlyPickedBrand)
                return false;

            return true;
        }

        private static bool ShouldCarryExcludedCategories(
            string normalizedMessage,
            ParsedIntent effective,
            CustomerPreferenceProfile profile)
        {
            if (effective == null || profile == null)
                return false;

            if (effective.ExcludedCategories.Count > 0)
                return false;

            if (profile.ExcludedCategories == null || profile.ExcludedCategories.Count == 0)
                return false;

            var text = NormalizeText(normalizedMessage);

            bool userExplicitlyPickedCategory =
                !string.IsNullOrWhiteSpace(effective.Category) ||
                ContainsAny(text, "xe ga", "xe so", "xe số", "tay ga", "con tay", "côn tay");

            if (userExplicitlyPickedCategory)
                return false;

            return true;
        }
        private static bool ShouldIgnoreOldCategoryForFreshTargetAsk(
    string normalizedMessage,
    ParsedIntent intent)
        {
            var text = NormalizeText(normalizedMessage);

            bool hasExplicitCategoryInCurrentMessage =
    ContainsAny(text,
        "xe ga",
        "xe so",
        "xe số",
        "tay ga",
        "con tay",
        "côn tay");

            if (hasExplicitCategoryInCurrentMessage)
                return false;

            bool hasTargetSignal =
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.PrefersFemaleStyle ||
                intent.PrefersMaleStyle ||
                ContainsAny(text,
                    "cho nu",
                    "cho nữ",
                    "xe nu",
                    "xe nữ",
                    "hop nu",
                    "hợp nữ",
                    "nu tinh",
                    "nữ tính",
                    "cho nam",
                    "xe nam",
                    "hop nam",
                    "hợp nam",
                    "nam tinh");

            if (!hasTargetSignal)
                return false;

            bool looksLikeFreshAsk =
                ContainsAny(text,
                    "tu van",
                    "tư vấn",
                    "goi y",
                    "gợi ý",
                    "xe cho",
                    "mau cho",
                    "mẫu cho",
                    "hop",
                    "hợp");

            return looksLikeFreshAsk;
        }
        private static bool IsSafeLlmReasonForProduct(
    ProductSummaryDto currentProduct,
    string reason,
    IReadOnlyList<ProductSummaryDto> currentProducts)
        {
            if (currentProduct == null || string.IsNullOrWhiteSpace(reason))
                return false;

            var normalizedReason = NormalizeText(reason);
            var currentName = NormalizeText(currentProduct.Ten);

            foreach (var product in currentProducts)
            {
                if (product == null || product.Id == currentProduct.Id)
                    continue;

                var otherName = NormalizeText(product.Ten);
                if (string.IsNullOrWhiteSpace(otherName))
                    continue;

                if (normalizedReason.Contains(otherName))
                    return false;

                var otherCoreName = RemoveBrandPrefix(otherName);
                if (!string.IsNullOrWhiteSpace(otherCoreName) &&
                    otherCoreName.Length >= 4 &&
                    normalizedReason.Contains(otherCoreName))
                {
                    return false;
                }
            }

            return true;
        }

        private static string RemoveBrandPrefix(string productName)
        {
            var text = NormalizeText(productName);

            foreach (var brand in new[] { "honda", "yamaha", "suzuki", "piaggio", "sym" })
            {
                if (text.StartsWith(brand + " "))
                    return text.Substring(brand.Length).Trim();
            }

            return text;
        }
        private static string BuildNaturalRecommendationReason(
    ProductSummaryDto item,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string normalizedMessage)
        {
            var name = item?.Ten ?? string.Empty;
            var category = NormalizeCategory(item?.Loai);
            var text = NormalizeText(normalizedMessage);

            bool wantsDurable =
                ContainsAny(text, "ben", "bền", "thuc dung", "thực dụng") ||
                string.Equals(category, "xe so", StringComparison.OrdinalIgnoreCase);

            bool wantsFemale =
                intent.PrefersFemaleStyle ||
                profile.PrefersFemaleStyle ||
                ContainsAny(text, "nu", "nữ", "cho nu", "cho nữ", "nu tinh", "nữ tính");

            bool wantsMale =
                intent.PrefersMaleStyle ||
                profile.PrefersMaleStyle ||
                ContainsAny(text, "nam", "cho nam", "nam tinh", "nam tính");

            bool hasPrice =
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue ||
                intent.TargetPrice.HasValue ||
                profile.PriceMin.HasValue ||
                profile.PriceMax.HasValue ||
                profile.TargetPrice.HasValue;

            if (wantsFemale)
            {
                return PickReasonByName(name,
                    "dáng gọn, dễ đi và khá hợp với nhu cầu nữ",
                    "thiết kế nhẹ nhàng, dễ sử dụng khi đi phố",
                    "phù hợp nếu bạn ưu tiên xe dễ điều khiển và tiện đi hằng ngày");
            }

            if (wantsMale)
            {
                return PickReasonByName(name,
                    "kiểu dáng chắc chắn, hợp nhu cầu đi lại hằng ngày",
                    "phù hợp nếu bạn thích mẫu xe thực dụng và nam tính hơn",
                    "đáng cân nhắc nếu bạn muốn xe khỏe, dễ dùng");
            }

            if (string.Equals(category, "xe so", StringComparison.OrdinalIgnoreCase) || wantsDurable)
            {
                return PickReasonByName(name,
                    "dễ đi và khá bền cho nhu cầu hằng ngày",
                    "chi phí thấp, phù hợp dùng lâu dài",
                    "thực dụng, dễ bảo dưỡng và hợp đi lại thường xuyên");
            }

            if (string.Equals(category, "xe ga", StringComparison.OrdinalIgnoreCase))
            {
                return PickReasonByName(name,
                    "tiện đi phố, dễ sử dụng và hợp nhu cầu di chuyển hằng ngày",
                    "gọn gàng, dễ dùng và phù hợp đi lại thường xuyên",
                    "thoải mái khi đi trong đô thị, không cần thao tác quá nhiều");
            }

            if (string.Equals(category, "con tay", StringComparison.OrdinalIgnoreCase))
            {
                return PickReasonByName(name,
                    "cảm giác lái chủ động, hợp kiểu thể thao",
                    "phù hợp nếu bạn thích xe mạnh và cá tính",
                    "đáng cân nhắc nếu bạn thích phong cách thể thao hơn");
            }

            if (hasPrice)
            {
                return PickReasonByName(name,
                    "nằm khá sát mức giá bạn vừa đưa ra",
                    "giá gần với ngân sách nên khá dễ cân nhắc",
                    "phù hợp nếu bạn muốn bám sát mức tiền đang dự tính");
            }

            return PickReasonByName(name,
    "dễ dùng và hợp với nhu cầu đi lại hằng ngày",
    "là lựa chọn khá ổn nếu bạn muốn xe bền và tiết kiệm",
    "phù hợp nếu bạn cần một mẫu xe thực dụng, dễ sử dụng");
        }

        private static string PickReasonByName(string? productName, params string[] reasons)
        {
            if (reasons == null || reasons.Length == 0)
                return "là lựa chọn khá ổn nếu bạn muốn tham khảo thêm";

            var name = productName ?? string.Empty;
            var index = Math.Abs(name.GetHashCode()) % reasons.Length;

            return reasons[index];
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
        private async Task TryApplyLlmReasonsOnlyAsync(
     string conversationId,
     string normalizedMessage,
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     IReadOnlyList<ProductSummaryDto> rankedByRule,
     Dictionary<int, string> llmReasonMap)
        {
            if (rankedByRule == null || rankedByRule.Count == 0)
                return;

            if (rankedByRule.Count == 1)
                return;

            try
            {
                var llmResult = await _recommendationLLMService.RerankAsync(
                    normalizedMessage,
                    intent,
                    profile,
                    rankedByRule);

                if (llmResult?.Recommendations != null)
                {
                    var validIds = rankedByRule.Select(x => x.Id).ToHashSet();

                    foreach (var item in llmResult.Recommendations)
                    {
                        if (item == null)
                            continue;

                        if (!validIds.Contains(item.ProductId))
                            continue;

                        if (!string.IsNullOrWhiteSpace(item.Reason))
                            llmReasonMap[item.ProductId] = item.Reason.Trim();
                    }
                }

                _logger.LogInformation(
                    "LLM reasons applied without changing rule ranking. ConversationId={ConversationId}, ReasonCount={ReasonCount}",
                    conversationId,
                    llmReasonMap.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM reason generation failed. Keep rule reasons.");
            }
        }

        private async Task<string> RewriteIfEnabledAsync(string normalizedMessage, string draftReply)
        {
            if (!_replyRewriteOptions.EnableRecommendationRewrite)
                return draftReply;

            return await _replyRewriteService.RewriteAsync(normalizedMessage, draftReply);
        }

        private static List<ProductSummaryDto> EnsureDiversity(
            IReadOnlyList<ProductSummaryDto> products,
            string? requestedBrand)
        {
            if (products == null || products.Count == 0)
                return new List<ProductSummaryDto>();

            if (!string.IsNullOrWhiteSpace(requestedBrand))
                return products.ToList();

            var result = new List<ProductSummaryDto>();
            var seenBrand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in products)
            {
                var brand = item.ThuongHieu ?? string.Empty;
                if (seenBrand.Add(brand))
                    result.Add(item);
            }

            foreach (var item in products)
            {
                if (result.Count >= 4)
                    break;

                if (!result.Any(x => x.Id == item.Id))
                    result.Add(item);
            }

            return result;
        }

        private static string BuildRecommendationReasonTag(ParsedIntent intent)
        {
            if (!string.IsNullOrWhiteSpace(intent.Brand))
                return "fresh_consultation_brand";

            if (!string.IsNullOrWhiteSpace(intent.Category))
                return "fresh_consultation_category";

            if (intent.TargetPrice.HasValue || intent.PriceMin.HasValue || intent.PriceMax.HasValue)
                return "fresh_consultation_price";

            if (intent.ForWork || intent.ForSchool || intent.ForCity || intent.ForTour)
                return "fresh_consultation_usage";

            return "fresh_consultation";
        }

        private static string NormalizeToken(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        private static (decimal? minPrice, decimal? maxPrice) ResolveRecommendationPriceRange(
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            decimal? minPrice = intent.PriceMin ?? profile.PriceMin;
            decimal? maxPrice = intent.PriceMax ?? profile.PriceMax;

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var delta = ProductPriceFilterHelper.GetAroundDelta(target);

                minPrice = Math.Max(0, target - delta);
                maxPrice = target + delta;
            }

            return (minPrice, maxPrice);
        }
        private static (decimal? minPrice, decimal? maxPrice) RelaxPriceRangeForChangeProduct(
     decimal? minPrice,
     decimal? maxPrice)
        {
            if (!minPrice.HasValue && !maxPrice.HasValue)
                return (minPrice, maxPrice);

            decimal? relaxedMin = minPrice;
            decimal? relaxedMax = maxPrice;

            if (relaxedMin.HasValue)
                relaxedMin = Math.Max(0, relaxedMin.Value - 2_000_000m);

            if (relaxedMax.HasValue)
                relaxedMax = relaxedMax.Value + 2_000_000m;

            return (relaxedMin, relaxedMax);
        }
        private async Task<List<ProductSummaryDto>> GetCandidatesAsync(
            string? requestedBrand,
            string? effectiveCategory,
            decimal? minPrice,
            decimal? maxPrice,
            int take)
        {
            var toolResult = await _toolClient.GetProductsByFiltersAsync(
                brand: requestedBrand,
                minPrice: minPrice,
                maxPrice: maxPrice,
                category: effectiveCategory,
                take: take);

            return toolResult?.Items?
                .Where(x => x != null)
                .ToList() ?? new List<ProductSummaryDto>();
        }

        private async Task<List<ProductSummaryDto>> TryGetBrandRelaxedCandidatesAsync(
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string? effectiveCategory)
        {
            var requestedBrand = intent.Brand ?? profile.PreferredBrand;
            bool userExplicitBrand = !string.IsNullOrWhiteSpace(intent.Brand);
            var (minPrice, maxPrice) = ResolveRecommendationPriceRange(intent, profile);

            if (intent.FilterType == PriceFilterType.Around && intent.TargetPrice.HasValue)
            {
                var target = intent.TargetPrice.Value;
                var relaxedDelta = ProductPriceFilterHelper.GetAroundDelta(target) + 2_000_000m;
                minPrice = Math.Max(0, target - relaxedDelta);
                maxPrice = target + relaxedDelta;
            }
            else
            {
                if (minPrice.HasValue)
                    minPrice = Math.Max(0, minPrice.Value - 2_000_000m);

                if (maxPrice.HasValue)
                    maxPrice = maxPrice.Value + 2_000_000m;
            }

            var items = await GetCandidatesAsync(
                requestedBrand,
                null,
                minPrice,
                maxPrice,
                take: 30);

            if (items.Count > 0)
                return items;

            if (!userExplicitBrand)
            {
                items = await GetCandidatesAsync(
                    null,
                    effectiveCategory,
                    minPrice,
                    maxPrice,
                    take: 30);

                if (items.Count > 0)
                    return items;
            }

            if (userExplicitBrand)
                return new List<ProductSummaryDto>();

            return await GetCandidatesAsync(
                null,
                null,
                minPrice,
                maxPrice,
                take: 30);
        }

        private static List<ProductSummaryDto> ApplyLlmRerank(
     IReadOnlyList<ProductSummaryDto> rankedByRule,
     LLMRecommendationResult? llmResult)
        {
            if (rankedByRule == null || rankedByRule.Count == 0)
                return new List<ProductSummaryDto>();

            if (llmResult?.Recommendations == null || llmResult.Recommendations.Count == 0)
                return rankedByRule.Take(3).ToList();

            var byId = rankedByRule.ToDictionary(x => x.Id, x => x);

            var selected = llmResult.Recommendations
                .Where(x => x != null)
                .GroupBy(x => x.ProductId)
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .Where(x => byId.ContainsKey(x.ProductId))
                .Select(x => byId[x.ProductId])
                .ToList();

            if (selected.Count > 0)
            {
                foreach (var item in rankedByRule)
                {
                    if (selected.Count >= 3)
                        break;

                    if (!selected.Any(x => x.Id == item.Id))
                        selected.Add(item);
                }

                return selected.Take(3).ToList();
            }

            return rankedByRule.Take(3).ToList();
        }
        private static void ApplyInlineExcludedBrandGuard(string text, ParsedIntent intent)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var brands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["honda"] = "Honda",
                ["yamaha"] = "Yamaha",
                ["suzuki"] = "Suzuki",
                ["sym"] = "SYM",
                ["piaggio"] = "Piaggio"
            };

            foreach (var pair in brands)
            {
                var normalizedBrand = pair.Key;
                var displayBrand = pair.Value;

                bool hasNegativeBrand =
                    ContainsAny(text,
                        $"khong {normalizedBrand}",
                        $"ko {normalizedBrand}",
                        $"k {normalizedBrand}",
                        $"khong thich {normalizedBrand}",
                        $"khong muon {normalizedBrand}",
                        $"khong lay {normalizedBrand}",
                        $"khong chon {normalizedBrand}",
                        $"khong phai {normalizedBrand}",
                        $"tru {normalizedBrand}",
                        $"ngoai tru {normalizedBrand}",
                        $"mien khong {normalizedBrand}",
                        $"mien khong la {normalizedBrand}");

                if (!hasNegativeBrand)
                    continue;

                intent.ExcludedBrands.Add(displayBrand);

                if (string.Equals(intent.Brand, displayBrand, StringComparison.OrdinalIgnoreCase))
                    intent.Brand = null;
            }
        }
        private static string? ExtractBrandFromProductName(string? productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return null;

            var text = NormalizeText(productName);

            if (text.StartsWith("honda "))
                return "Honda";

            if (text.StartsWith("yamaha "))
                return "Yamaha";

            if (text.StartsWith("suzuki "))
                return "Suzuki";

            if (text.StartsWith("sym "))
                return "SYM";

            if (text.StartsWith("piaggio "))
                return "Piaggio";

            return null;
        }
        private static RecommendationBuckets BucketRecommendations(IReadOnlyList<ProductSummaryDto> products)
        {
            var buckets = new RecommendationBuckets();

            if (products == null || products.Count == 0)
                return buckets;

            foreach (var product in products)
            {
                if (product == null)
                    continue;

                var name = product.Ten ?? string.Empty;
                var category = NormalizeCategory(product.Loai);

                if (category == "xe ga" &&
                    ContainsAny(name, "Air Blade", "Vision", "Lead", "Latte", "Grande", "Freego", "Address", "Burgman"))
                {
                    buckets.ScooterPractical.Add(product);
                }
                else if (ContainsAny(name, "Future", "Wave", "Sirius", "Galaxy", "Angela", "Elegant"))
                {
                    buckets.DurablePractical.Add(product);
                }
                else if (ContainsAny(name, "Winner", "Exciter", "Raider", "Sonic", "Husky"))
                {
                    buckets.Sporty.Add(product);
                }
                else
                {
                    buckets.Others.Add(product);
                }
            }

            return buckets;
        }
        private static ProductSummaryDto? PickAnchorProduct(
    IReadOnlyList<ProductSummaryDto> products,
    ParsedIntent intent,
    CustomerPreferenceProfile profile)
        {
            if (products == null || products.Count == 0)
                return null;

            bool prefersFemale =
                intent.PrefersFemaleStyle ||
                profile.PrefersFemaleStyle ||
                (!string.IsNullOrWhiteSpace(intent.Target) && intent.Target.Contains("nữ", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(profile.Target) && profile.Target.Contains("nữ", StringComparison.OrdinalIgnoreCase));

            if (prefersFemale)
            {
                var femaleAnchor = products.FirstOrDefault(x =>
                    ContainsAny(x.Ten, "Vision", "Latte", "Grande", "Zip", "Janus", "Lead"));

                if (femaleAnchor != null)
                    return femaleAnchor;
            }

            bool prefersMale =
                intent.PrefersMaleStyle ||
                profile.PrefersMaleStyle ||
                (!string.IsNullOrWhiteSpace(intent.Target) && intent.Target.Contains("nam", StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(profile.Target) && profile.Target.Contains("nam", StringComparison.OrdinalIgnoreCase));

            if (prefersMale)
            {
                var maleAnchor = products.FirstOrDefault(x =>
                    ContainsAny(x.Ten, "Air Blade", "Winner", "Future", "PCX", "Husky"));

                if (maleAnchor != null)
                    return maleAnchor;
            }

            return products.FirstOrDefault();
        }
        private static int GetScooterNarrativePriority(ProductSummaryDto product)
        {
            var name = product.Ten ?? string.Empty;

            if (ContainsAny(name, "Latte", "Grande", "Vision", "Lead", "Janus"))
                return 3;

            if (ContainsAny(name, "Freego", "Address"))
                return 2;

            if (ContainsAny(name, "Air Blade", "Burgman"))
                return 1;

            return 0;
        }
        private static List<string> BuildBucketNarratives(RecommendationBuckets buckets)
        {
            var lines = new List<string>();

            if (buckets.ScooterPractical.Count >= 2)
            {
                var names = string.Join(", ",
    buckets.ScooterPractical
        .OrderByDescending(GetScooterNarrativePriority)
        .Take(2)
        .Select(x => x.Ten ?? string.Empty));
                lines.Add($"Nếu bạn thích xe ga tiện đi phố và dễ dùng hằng ngày thì có thể xem: {names}.");
            }

            if (buckets.DurablePractical.Count >= 2)
            {
                var names = string.Join(", ", buckets.DurablePractical.Take(2).Select(x => x.Ten ?? string.Empty));
                lines.Add($"Nếu bạn ưu tiên hướng bền, dễ nuôi và thực dụng thì có thể xem: {names}.");
            }
            var modernScooters = buckets.ScooterPractical
    .Where(LooksLikeModernScooter)
    .Take(2)
    .ToList();

            if (modernScooters.Count >= 1)
            {
                var names = string.Join(", ", modernScooters.Select(x => x.Ten ?? string.Empty));
                lines.Add($"Nếu bạn thích xe ga đầm, hiện đại và đi phố thoải mái thì có thể xem: {names}.");
            }

            if (buckets.Sporty.Count >= 2)
            {
                var names = string.Join(", ", buckets.Sporty.Take(2).Select(x => x.Ten ?? string.Empty));
                lines.Add($"Nếu bạn thích kiểu dáng thể thao hoặc cá tính hơn thì có thể xem: {names}.");
            }

            return lines;
        }
        private static List<ProductSummaryDto> FinalizeRecommendationRanking(
    IReadOnlyList<ProductSummaryDto> ranked,
    IReadOnlyList<ProductSummaryDto> rankedByRule,
    ParsedIntent effectiveIntent,
    ParsedIntent originalIntent,
    CustomerPreferenceProfile profile,
    string? effectiveCategory,
    string normalizedMessage)
        {
            var result = ranked?.ToList() ?? new List<ProductSummaryDto>();

            result = EnforceFinalRecommendationGuards(
                result,
                effectiveIntent,
                profile,
                effectiveCategory,
                normalizedMessage);

            result = RemoveRepeatedProductsForOtherFollowUp(
                result,
                originalIntent,
                profile,
                normalizedMessage);

            if (result.Count == 0)
            {
                result = ApplyExclusions(
                    rankedByRule?.ToList() ?? new List<ProductSummaryDto>(),
                    effectiveIntent,
                    profile);

                result = EnforceFinalRecommendationGuards(
                    result,
                    effectiveIntent,
                    profile,
                    effectiveCategory,
                    normalizedMessage);

                result = RemoveRepeatedProductsForOtherFollowUp(
                    result,
                    originalIntent,
                    profile,
                    normalizedMessage);
            }

            result = EnforceFinalRecommendationGuards(
                result,
                effectiveIntent,
                profile,
                effectiveCategory,
                normalizedMessage);

            if (string.Equals(effectiveIntent.FollowUpType, "pick_best", StringComparison.OrdinalIgnoreCase))
            {
                return result.Take(1).ToList();
            }
            return result.Take(3).ToList();
        }
        private static List<ProductSummaryDto> EnforceFinalRecommendationGuards(
    IReadOnlyList<ProductSummaryDto> products,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string? effectiveCategory,
    string normalizedMessage)
        {
            if (products == null || products.Count == 0)
                return new List<ProductSummaryDto>();

            var desiredCategory = NormalizeCategory(effectiveCategory ?? intent.Category);
            var result = products.ToList();

            if (!string.IsNullOrWhiteSpace(desiredCategory))
            {
                var sameCategory = result
                    .Where(x => string.Equals(
                        NormalizeCategory(x.Loai),
                        desiredCategory,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (sameCategory.Count > 0)
                    result = sameCategory;
            }

            if (desiredCategory == "xe so")
            {
                var practical = result
                    .Where(x =>
                    {
                        var name = NormalizeText(x.Ten);

                        if (x.Gia > 50_000_000)
                            return false;

                        return !ContainsAny(name,
                            "cub",
                            "super cub",
                            "125",
                            "gd110",
                            "axelo",
                            "winner",
                            "exciter",
                            "raider",
                            "sonic",
                            "husky",
                            "cbr",
                            "rebel");
                    })
                    .ToList();

                if (practical.Count > 0)
                    result = practical;
            }

            if (ShouldApplyFemaleFriendlyGuard(intent, profile, normalizedMessage))
            {
                var femaleFriendly = result
                    .Where(IsFemaleFriendlyCandidate)
                    .ToList();

                result = femaleFriendly;
            }

            return result;
        }
        private static bool ShouldApplyFemaleFriendlyGuard(
     ParsedIntent intent,
     CustomerPreferenceProfile profile,
     string normalizedMessage)
        {
            var text = NormalizeText(normalizedMessage);

            bool wantsFemale =
                intent.PrefersFemaleStyle ||
                profile.PrefersFemaleStyle ||
                (!string.IsNullOrWhiteSpace(intent.Target) &&
                 NormalizeText(intent.Target).Contains("nu")) ||
                (!string.IsNullOrWhiteSpace(profile.Target) &&
                 NormalizeText(profile.Target).Contains("nu")) ||
                ContainsAny(text,
                    "cho nu",
                    "xe nu",
                    "hop nu",
                    "nu tinh",
                    "ban nu",
                    "con gai");

            bool isCheaperFollowUp =
                ContainsAny(text,
                    "re hon",
                    "mem hon",
                    "thap hon",
                    "it tien hon",
                    "xuong");

            bool previousListLooksFemale =
                isCheaperFollowUp &&
                HasFemaleFriendlyPreviousRecommendation(profile);

            bool explicitlySporty =
                intent.PrefersMaleStyle ||
                profile.PrefersMaleStyle ||
                ContainsAny(text,
                    "con tay",
                    "the thao",
                    "ca tinh",
                    "manh",
                    "toc do",
                    "winner",
                    "exciter",
                    "raider",
                    "sonic",
                    "axelo",
                    "gd110");

            return (wantsFemale || previousListLooksFemale) && !explicitlySporty;
        }
        private static bool HasFemaleFriendlyPreviousRecommendation(CustomerPreferenceProfile profile)
        {
            var previousNames = new List<string>();

            if (profile.LastRecommendedProducts != null)
                previousNames.AddRange(profile.LastRecommendedProducts);

            if (profile.CurrentRecommendedProducts != null)
                previousNames.AddRange(profile.CurrentRecommendedProducts);

            if (previousNames.Count == 0)
                return false;

            return previousNames.Any(name =>
                ContainsAny(name,
                    "vision",
                    "attila",
                    "shark",
                    "latte",
                    "grande",
                    "janus",
                    "lead",
                    "zip",
                    "elite",
                    "address",
                    "impulse"));
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

            if (category == "con tay")
                return false;

            // Ưu tiên mạnh xe ga
            if (category == "xe ga")
                return true;

            // Chỉ cho phép MỘT SỐ xe số thực sự phù hợp nữ
            if (category == "xe so")
            {
                if (ContainsAny(name,
     "wave",
     "sirius"))
                {
                    return true;
                }

                return false;
            }
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
                "impulse"))
            {
                return true;
            }

            return false;
        }
        private static bool ShouldIgnoreOldProfileForFreshPriceAsk(
    string normalizedMessage,
    ParsedIntent intent)
        {
            var text = NormalizeText(normalizedMessage);

            bool looksFreshAdvice =
                ContainsAny(text,
                    "tu van",
                    "tư vấn",
                    "goi y",
                    "gợi ý",
                    "nen mua",
                    "nên mua");

            bool hasPrice =
                intent.TargetPrice.HasValue ||
                intent.PriceMin.HasValue ||
                intent.PriceMax.HasValue;

            bool hasExplicitCategory =
                !string.IsNullOrWhiteSpace(intent.Category) ||
                ContainsAny(text, "xe ga", "xe so", "xe số", "con tay", "côn tay");

            bool hasExplicitTarget =
                !string.IsNullOrWhiteSpace(intent.Target) ||
                intent.PrefersFemaleStyle ||
                intent.PrefersMaleStyle ||
                ContainsAny(text, "cho nu", "cho nữ", "cho nam", "xe nu", "xe nữ", "xe nam");

            bool hasExplicitBrand = !string.IsNullOrWhiteSpace(intent.Brand);

            return looksFreshAdvice &&
                   hasPrice &&
                   !hasExplicitCategory &&
                   !hasExplicitTarget &&
                   !hasExplicitBrand;
        }
        private static bool LooksLikeModernScooter(ProductSummaryDto product)
        {
            var name = product.Ten ?? string.Empty;
            return ContainsAny(name, "Air Blade", "Burgman", "Address", "NVX", "PCX");
        }
        private static bool LooksLikeOtherProductRequest(string normalizedMessage, ParsedIntent intent)
        {
            var text = NormalizeText(normalizedMessage);

            if (intent.IsFollowUp &&
                string.Equals(intent.FollowUpType, "other", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsAny(text,
                "xe khac",
                "mau khac",
                "con khac",
                "khac di",
                "doi xe khac",
                "chon xe khac",
                "goi y xe khac",
                "tu van xe khac");
        }
        private static List<ProductSummaryDto> RemoveRepeatedProductsForOtherFollowUp(
       IReadOnlyList<ProductSummaryDto> products,
       ParsedIntent intent,
       CustomerPreferenceProfile profile,
       string normalizedMessage)
        {
            if (products == null || products.Count == 0)
                return new List<ProductSummaryDto>();

            if (!LooksLikeOtherProductRequest(normalizedMessage, intent))
                return products.ToList();

            var oldNames = new HashSet<string>(
                profile.LastRecommendedProducts ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (oldNames.Count == 0)
                return products.ToList();

            return products
                .Where(x => !oldNames.Contains(x.Ten ?? string.Empty))
                .ToList();
        }
        private async Task<ChatResponse?> PickBestFromComparedProductsAsync(
    string conversationId,
    string normalizedMessage,
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile)
        {
            var comparedNames = profile.LastComparedProducts?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (comparedNames.Count == 0)
                return null;

            var candidates = new List<ProductSummaryDto>();

            foreach (var name in comparedNames)
            {
                var result = await _toolClient.SearchProductsAsync(name, 3);
                var product = result?.Items?
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Ten))
                    .OrderByDescending(x => string.Equals(x.Ten, name, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.SoLuong)
                    .FirstOrDefault();

                if (product != null && !candidates.Any(x => x.Id == product.Id))
                    candidates.Add(product);
            }

            if (candidates.Count == 0)
                return null;

            var feature = effectiveIntent.ComparisonFeature;
            if (string.IsNullOrWhiteSpace(feature))
                feature = profile.LastComparisonFeature;

            var ranked = candidates
                .Select(p => new
                {
                    Product = p,
                    Score = ScorePickBestCandidate(p, feature, effectiveIntent, profile, normalizedMessage)
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Product.Gia)
                .Select(x => x.Product)
                .ToList();

            var best = ranked.First();

            await _conversationPreferenceService.SetBaseRecommendedProductsAsync(
                conversationId,
                new List<ProductSummaryDto> { best });

            await _conversationPreferenceService.UpdateCurrentRecommendedProductsAsync(
                conversationId,
                new List<ProductSummaryDto> { best },
                "pick_best_from_compare");
            var reason = BuildReasonByFeature(best, feature);
            return new ChatResponse
            {
                Success = true,
                ConversationId = conversationId,
                UsedAI = false,
                UsedTool = ToolNames.GetProductsByFilters,
                Reply =
    $"Nếu chốt 1 xe trong nhóm vừa so sánh theo tiêu chí này thì mình nghiêng về **{best.Ten}**.\n\n" +
    $"- **{best.Ten}** ({best.Gia:N0} VNĐ): {reason}",
                Products = new List<ChatProductCard>
        {
            ChatProductCardMapper.Map(best)
        }
            };
        }

        private static int ScorePickBestCandidate(
            ProductSummaryDto product,
            string? feature,
            ParsedIntent intent,
            CustomerPreferenceProfile profile,
            string normalizedMessage)
        {
            var score = 0;
            var name = product.Ten ?? string.Empty;
            var text = NormalizeText(normalizedMessage);

            if (string.Equals(feature, "power", StringComparison.OrdinalIgnoreCase) ||
                ContainsAny(text, "boc", "may khoe", "manh", "khoe"))
            {
                if (ContainsAny(name, "Air Blade", "PCX", "SH", "Winner", "Exciter", "Raider"))
                    score += 50;

                if (ContainsAny(name, "Vision", "Latte", "Freego", "Zip", "Janus"))
                    score -= 10;
            }

            if (string.Equals(feature, "fuel_saving", StringComparison.OrdinalIgnoreCase) ||
                intent.WantsFuelSaving || profile.WantsFuelSaving)
            {
                if (ContainsAny(name, "Vision", "Future", "Wave", "Sirius", "Janus"))
                    score += 45;

                if (ContainsAny(name, "SH", "PCX"))
                    score -= 10;
            }

            if (string.Equals(feature, "storage", StringComparison.OrdinalIgnoreCase) ||
                intent.WantsLargeStorage || profile.WantsLargeStorage)
            {
                if (ContainsAny(name, "Lead", "Freego", "Air Blade", "Latte"))
                    score += 40;
            }

            if (string.Equals(feature, "female_fit", StringComparison.OrdinalIgnoreCase) ||
                intent.PrefersFemaleStyle || profile.PrefersFemaleStyle)
            {
                if (ContainsAny(name, "Vision", "Latte", "Grande", "Janus", "Zip"))
                    score += 40;

                if (ContainsAny(name, "SH", "PCX", "Winner", "Exciter", "Raider"))
                    score -= 20;
            }

            if (intent.ForWork || profile.ForWork)
            {
                if (ContainsAny(name, "Vision", "Air Blade", "Future", "Freego", "Lead"))
                    score += 20;
            }

            if (product.Gia <= 35_000_000m)
                score += 5;

            return score;
        }
        private static bool HasExclusionIntent(ParsedIntent intent)
        {
            return intent.ExcludedBrands.Any() ||
                   intent.ExcludedProducts.Any() ||
                   intent.ExcludedCategories.Any();
        }
        private async Task<List<ProductSummaryDto>> LoadBroadCandidatesForExclusionAsync(
    string conversationId,
    ParsedIntent intent,
    CustomerPreferenceProfile profile,
    string? effectiveCategory,
    decimal? minPrice,
    decimal? maxPrice)
        {
            var products = await GetCandidatesAsync(
                requestedBrand: null,
                effectiveCategory: effectiveCategory,
                minPrice: minPrice,
                maxPrice: maxPrice,
                take: 200);

            products = ApplyExclusions(products, intent, profile);

            _logger.LogInformation(
                "Loaded broad candidates for exclusion. ConversationId={ConversationId}, Count={Count}",
                conversationId,
                products.Count);

            return products;
        }
        private static string BuildReasonByFeature(ProductSummaryDto product, string? feature)
        {
            var name = product.Ten ?? "";

            if (feature == "power")
                return "máy khỏe hơn, bốc hơn và cảm giác xe đầm hơn khi chạy.";

            if (feature == "fuel_saving")
                return "tiết kiệm xăng hơn, phù hợp đi lâu dài.";

            if (feature == "storage")
                return "cốp rộng hơn, tiện mang đồ hằng ngày.";

            if (feature == "female_fit")
                return "dáng xe gọn, dễ điều khiển và hợp nữ hơn.";

            return "phù hợp nhất trong nhóm theo tiêu chí bạn đang xét.";
        }
        private sealed class RecommendationBuckets
        {
            public List<ProductSummaryDto> ScooterPractical { get; set; } = new();
            public List<ProductSummaryDto> DurablePractical { get; set; } = new();
            public List<ProductSummaryDto> Sporty { get; set; } = new();
            public List<ProductSummaryDto> Others { get; set; } = new();
        }
    }
}
