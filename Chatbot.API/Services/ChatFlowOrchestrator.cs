/*
Refactor ChatFlowOrchestrator để chuyển từ rule-based sang AI-first semantic pipeline.

Hiện tại hệ thống:
- Parse intent bằng rule-based trước
- Chỉ gọi LLM khi confidence thấp

Yêu cầu sửa:
1. LUÔN gọi SemanticParserService trước để hiểu ngữ nghĩa user input
2. KHÔNG dùng keyword matching làm nguồn quyết định chính
3. SemanticParser phải trả về JSON:
{
  intent: string,
  price_level: string,
  installment: string,
  interest: string,
  preference: string,
  normalized_meaning: string
}

4. Sau khi có semantic result:
   - Map sang internal intent
   - Dùng FlowDecisionService để route
   - Giữ nguyên các flow: lookup, search, recommend, compare, refine

5. Rule-based chỉ dùng fallback nếu LLM fail (không phải ngược lại)

6. Bỏ cơ chế confidence threshold hiện tại cho rule-based intent

7. Code phải async/await, clean architecture, dễ mở rộng

Mục tiêu:
User → Semantic Understanding → Intent → Flow → Response
*/
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        private readonly ISemanticParserService _semanticParserService;
        private readonly IQueryNormalizationService _queryNormalizationService;
        private readonly IConversationPreferenceService _conversationPreferenceService;
        private readonly IIntentParserService _intentParserService;
        private readonly IConversationContextResolver _conversationContextResolver;
        private readonly IChatFlowRouter _chatFlowRouter;
        private readonly IFlowDecisionService _flowDecisionService;
        private readonly IProductLookupFlowService _productLookupFlowService;
        private readonly IProductSearchFlowService _productSearchFlowService;
        private readonly IRefinementService _refinementService;
        private readonly ICompareService _compareService;
        private readonly IRecommendationFlowService _recommendationFlowService;
        private readonly IServiceInfoFlowService _serviceInfoFlowService;
        private readonly IOpenAIService _openAIService;
        private readonly IRagService _ragService;
        public ChatFlowOrchestrator(
    ILogger<ChatFlowOrchestrator> logger,
    IClarificationStateService clarificationStateService,
    ISemanticParserService semanticParserService,
    IQueryNormalizationService queryNormalizationService,
    IConversationPreferenceService conversationPreferenceService,
    IIntentParserService intentParserService,
    IConversationContextResolver conversationContextResolver,
    IChatFlowRouter chatFlowRouter,
    IFlowDecisionService flowDecisionService,
    IProductLookupFlowService productLookupFlowService,
    IProductSearchFlowService productSearchFlowService,
    IRefinementService refinementService,
    ICompareService compareService,
    IRecommendationFlowService recommendationFlowService,
    IServiceInfoFlowService serviceInfoFlowService,
    IOpenAIService openAIService,
    IRagService ragService)
        {
            _logger = logger;
            _clarificationStateService = clarificationStateService;
            _semanticParserService = semanticParserService;
            _queryNormalizationService = queryNormalizationService;
            _conversationPreferenceService = conversationPreferenceService;
            _intentParserService = intentParserService;
            _conversationContextResolver = conversationContextResolver;
            _chatFlowRouter = chatFlowRouter;
            _flowDecisionService = flowDecisionService;
            _productLookupFlowService = productLookupFlowService;
            _productSearchFlowService = productSearchFlowService;
            _refinementService = refinementService;
            _compareService = compareService;
            _recommendationFlowService = recommendationFlowService;
            _serviceInfoFlowService = serviceInfoFlowService;
            _openAIService = openAIService;
            _ragService = ragService;
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
            var semanticResult = await _semanticParserService.ParseAsync(originalMessage, existingProfile);
            semanticResult = MergeSemanticWithSessionContext(semanticResult, existingProfile);
            ApplyExplicitNegations(normalizedMessage, semanticResult);
            ApplyPreviousProductReferenceContext(normalizedMessage, semanticResult, existingProfile);
            var semanticMeaning = BuildSemanticMessage(normalizedMessage, semanticResult);
            var semanticQuery = BuildSemanticQuery(normalizedMessage, semanticResult, existingProfile);

            var parsedIntent = !semanticResult.IsFallback
                ? BuildIntentFromSemantic(semanticResult, semanticMeaning)
                : await _intentParserService.ParseAsync(normalizedMessage);

            parsedIntent.RawMessage = originalMessage;
            ApplyExplicitNegations(normalizedMessage, parsedIntent);
            ApplyPreviousProductReferenceContext(normalizedMessage, parsedIntent, existingProfile);

            if (semanticResult.IsFallback && existingProfile.LastSemanticResult != null)
            {
                ApplyStoredIntentContext(parsedIntent, existingProfile.LastSemanticResult);
                ApplyExplicitNegations(normalizedMessage, parsedIntent);
                ApplyPreviousProductReferenceContext(normalizedMessage, parsedIntent, existingProfile);
            }

            bool isServiceOrPolicyIntent = IsServiceOrPolicyIntent(parsedIntent.IntentType);

            _logger.LogInformation(
                "Semantic parsed. ConversationId={ConversationId}, SemanticIntent={SemanticIntent}, SemanticFlow={SemanticFlow}, PriceLevel={PriceLevel}, Installment={Installment}, Interest={Interest}, IsFallback={IsFallback}, Meaning={Meaning}",
                conversationId,
                semanticResult.Intent,
                semanticResult.FlowType,
                semanticResult.PriceLevel,
                semanticResult.Installment,
                semanticResult.Interest,
                semanticResult.IsFallback,
                semanticResult.NormalizedMeaning);

            ContextResolutionResult contextResolution;
            ParsedIntent effectiveIntent;
            CustomerPreferenceProfile mergedProfile;
            FlowRoutingResult baseRouting;
            FlowRoutingResult finalRouting;

            if (isServiceOrPolicyIntent)
            {
                effectiveIntent = parsedIntent.Clone();
                contextResolution = new ContextResolutionResult
                {
                    EffectiveIntent = effectiveIntent,
                    ContextDecision = RecommendationContextDecision.None,
                    ShouldResetContext = false,
                    ShouldPreserveBudgetOnlyContext = false
                };

                mergedProfile = existingProfile;
                baseRouting = DeterministicRoute(
                    GetServiceFlowType(parsedIntent.IntentType),
                    "Service/policy intent detected");
                baseRouting.ShouldUseRag = true;
                finalRouting = baseRouting;

                _logger.LogInformation(
                    "Service/policy intent keeps product reference context. ConversationId={ConversationId}, IntentType={IntentType}",
                    conversationId,
                    parsedIntent.IntentType);
            }
            else
            {
                contextResolution = _conversationContextResolver.Resolve(
                    normalizedMessage,
                    parsedIntent,
                    existingProfile,
                    existingProfile.ActiveFlow);

                effectiveIntent = contextResolution.EffectiveIntent;
                var shouldPreserveContextForBudgetOnly = contextResolution.ShouldPreserveBudgetOnlyContext;

                bool sameIntentFamilyAsSession = IsSameIntentFamily(
                    semanticResult.Intent,
                    existingProfile.LastSemanticIntent ?? existingProfile.LastIntentType);

                if (contextResolution.ShouldResetContext && sameIntentFamilyAsSession)
                {
                    contextResolution.ShouldResetContext = false;
                    _logger.LogInformation(
                        "Skip context reset because semantic intent is unchanged. ConversationId={ConversationId}, SemanticIntent={SemanticIntent}, PreviousIntent={PreviousIntent}",
                        conversationId,
                        semanticResult.Intent,
                        existingProfile.LastSemanticIntent ?? existingProfile.LastIntentType);
                }

                if (contextResolution.ShouldResetContext)
                {
                    await _conversationPreferenceService.ResetForFreshConsultationAsync(conversationId);
                    existingProfile = await _conversationPreferenceService.GetAsync(conversationId);

                    _logger.LogInformation(
                        "Context reset after recommendation context decision. ConversationId={ConversationId}, Decision={Decision}",
                        conversationId,
                        contextResolution.ContextDecision);
                }
                else if (shouldPreserveContextForBudgetOnly)
                {
                    _logger.LogInformation(
                        "Skip context reset because current turn is budget-only pivot. ConversationId={ConversationId}, Decision={Decision}",
                        conversationId,
                        contextResolution.ContextDecision);
                }

                mergedProfile = await _conversationPreferenceService.MergeAsync(
                    conversationId,
                    effectiveIntent);

                baseRouting = !semanticResult.IsFallback && IsKnownFlowType(semanticResult.FlowType)
                    ? RouteFromSemanticIntent(
                        semanticResult,
                        new FlowRoutingResult
                        {
                            FlowType = ChatFlowType.Unknown,
                            ShouldUseDeterministicFlow = false,
                            ShouldUseAiFallback = true,
                            ShouldUseRag = false,
                            Reason = "Semantic-first routing"
                        })
                    : _chatFlowRouter.Route(
                        normalizedMessage,
                        effectiveIntent,
                        mergedProfile);

                finalRouting = _flowDecisionService.ResolveFinalRouting(
                    normalizedMessage,
                    effectiveIntent,
                    mergedProfile,
                    contextResolution.ContextDecision,
                    baseRouting);
            }

            mergedProfile.ActiveFlow = finalRouting.FlowType;
            mergedProfile.UpdatedAtUtc = DateTime.UtcNow;
            await _conversationPreferenceService.SetSemanticContextAsync(conversationId, semanticResult);

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
                contextResolution.ContextDecision,
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
                SemanticQuery = semanticQuery,
                ExistingProfile = mergedProfile,
                ParsedIntent = parsedIntent,
                EffectiveIntent = effectiveIntent,
                BaseRouting = baseRouting,
                FinalRouting = finalRouting
            };
        }

        private static string BuildSemanticMessage(string normalizedMessage, SemanticResult semanticResult)
        {
            if (!string.IsNullOrWhiteSpace(semanticResult.NormalizedMeaning))
            {
                return semanticResult.NormalizedMeaning.Trim();
            }

            return normalizedMessage;
        }

        private static void ApplyExplicitNegations(string normalizedMessage, SemanticResult semanticResult)
        {
            if (semanticResult == null)
                return;

            var negation = ExtractExplicitNegations(normalizedMessage);

            semanticResult.ExcludedBrands ??= new List<string>();
            semanticResult.ExcludedCategories ??= new List<string>();
            semanticResult.ExcludedProducts ??= new List<string>();
            semanticResult.RequestedStyles ??= new List<string>();

            foreach (var brand in negation.Brands)
            {
                if (!semanticResult.ExcludedBrands.Contains(brand, StringComparer.OrdinalIgnoreCase))
                    semanticResult.ExcludedBrands.Add(brand);
            }

            foreach (var category in negation.Categories)
            {
                if (!semanticResult.ExcludedCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
                    semanticResult.ExcludedCategories.Add(category);
            }

            foreach (var product in negation.Products)
            {
                if (!semanticResult.ExcludedProducts.Contains(product, StringComparer.OrdinalIgnoreCase))
                    semanticResult.ExcludedProducts.Add(product);
            }

            if (!string.IsNullOrWhiteSpace(semanticResult.Brand) &&
                negation.Brands.Contains(semanticResult.Brand, StringComparer.OrdinalIgnoreCase))
            {
                semanticResult.Brand = null;
            }

            if (!string.IsNullOrWhiteSpace(semanticResult.Category) &&
                negation.Categories.Any(category => IsSameNegatedCategory(semanticResult.Category, category)))
            {
                semanticResult.Category = null;
            }

            if (!string.IsNullOrWhiteSpace(semanticResult.Target) &&
                negation.Targets.Contains(semanticResult.Target, StringComparer.OrdinalIgnoreCase))
            {
                semanticResult.Target = null;
            }

            if (negation.InstallmentNegated)
            {
                semanticResult.Installment = "khong";
                semanticResult.Interest = "khong_ro";
            }

            semanticResult.MentionedProducts = semanticResult.MentionedProducts
                .Where(x => !negation.Brands.Any(brand => x.Contains(brand, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !negation.Products.Any(product => IsSameNegatedProduct(x, product)))
                .ToList();
        }

        private static void ApplyExplicitNegations(string normalizedMessage, ParsedIntent parsedIntent)
        {
            if (parsedIntent == null)
                return;

            var negation = ExtractExplicitNegations(normalizedMessage);

            parsedIntent.ExcludedBrands.UnionWith(negation.Brands);
            parsedIntent.ExcludedCategories.UnionWith(negation.Categories);
            parsedIntent.ExcludedProducts.UnionWith(negation.Products);

            if (!string.IsNullOrWhiteSpace(parsedIntent.Brand) &&
                negation.Brands.Contains(parsedIntent.Brand, StringComparer.OrdinalIgnoreCase))
            {
                parsedIntent.Brand = null;
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Category) &&
                negation.Categories.Any(category => IsSameNegatedCategory(parsedIntent.Category, category)))
            {
                parsedIntent.Category = null;
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Target) &&
                negation.Targets.Contains(parsedIntent.Target, StringComparer.OrdinalIgnoreCase))
            {
                parsedIntent.Target = null;
                parsedIntent.PrefersFemaleStyle = false;
                parsedIntent.PrefersMaleStyle = false;
            }

            if (negation.InstallmentNegated)
            {
                parsedIntent.ExcludedCategories.Add("trả góp");
            }

            foreach (var style in negation.Styles)
            {
                parsedIntent.RequestedStyles.RemoveWhere(x => string.Equals(x, style, StringComparison.OrdinalIgnoreCase));
            }

            parsedIntent.MentionedProducts = parsedIntent.MentionedProducts
                .Where(x => !negation.Brands.Any(brand => x.Contains(brand, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !negation.Products.Any(product => IsSameNegatedProduct(x, product)))
                .ToList();
        }

        private static void ApplyPreviousProductReferenceContext(
            string normalizedMessage,
            SemanticResult semanticResult,
            CustomerPreferenceProfile? profile)
        {
            if (semanticResult == null || !LooksLikePreviousProductReference(normalizedMessage))
                return;

            var productName = ResolveReferencedProductName(profile);
            if (string.IsNullOrWhiteSpace(productName))
                return;

            semanticResult.MentionedProducts ??= new List<string>();
            if (!semanticResult.MentionedProducts.Contains(productName, StringComparer.OrdinalIgnoreCase))
                semanticResult.MentionedProducts.Add(productName);

            var brand = InferBrandFromProductName(productName);
            if (string.IsNullOrWhiteSpace(semanticResult.Brand) && !string.IsNullOrWhiteSpace(brand))
                semanticResult.Brand = brand;

            if (!string.IsNullOrWhiteSpace(semanticResult.NormalizedMeaning) &&
                !semanticResult.NormalizedMeaning.Contains(productName, StringComparison.OrdinalIgnoreCase))
            {
                semanticResult.NormalizedMeaning = $"{semanticResult.NormalizedMeaning} cho {productName}";
            }
        }

        private static void ApplyPreviousProductReferenceContext(
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile)
        {
            if (parsedIntent == null || !LooksLikePreviousProductReference(normalizedMessage))
                return;

            var productName = ResolveReferencedProductName(profile);
            if (string.IsNullOrWhiteSpace(productName))
                return;

            parsedIntent.MentionedProducts ??= new List<string>();
            if (!parsedIntent.MentionedProducts.Contains(productName, StringComparer.OrdinalIgnoreCase))
                parsedIntent.MentionedProducts.Add(productName);

            var brand = InferBrandFromProductName(productName);
            if (string.IsNullOrWhiteSpace(parsedIntent.Brand) && !string.IsNullOrWhiteSpace(brand))
                parsedIntent.Brand = brand;

            parsedIntent.LookupTargetType ??= "product";
        }

        private static ChatResponse? TryHandlePreviousProductPurchaseIntent(ChatOrchestrationContext context)
        {
            if (context == null ||
                !LooksLikePreviousProductReference(context.NormalizedMessage) ||
                !LooksLikePurchaseRequest(context.NormalizedMessage))
            {
                return null;
            }

            var productName = ResolveReferencedProductName(context.ExistingProfile);
            if (string.IsNullOrWhiteSpace(productName))
                return null;

            return new ChatResponse
            {
                Success = true,
                UsedAI = false,
                ConversationId = context.ConversationId,
                Reply = $"Dạ được, mình đang hiểu bạn muốn mua {productName}. Bạn muốn thanh toán thẳng hay trả góp để mình hướng dẫn bước tiếp theo cho đúng nhé."
            };
        }

        private static bool LooksLikePreviousProductReference(string? message)
        {
            var text = NormalizeForNegation(message);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(
                text,
                @"\b(?:xe|mau|con|san\s*pham|dong)\s+(?:tren|ben\s+tren|do|nay|vua\s+goi\s+y|vua\s+tu\s+van|vua\s+noi|luc\s+nay)\b|\b(?:xe|mau)\s+ben\s+tren\b|\bben\s+tren\b",
                RegexOptions.IgnoreCase);
        }

        private static bool LooksLikePurchaseRequest(string? message)
        {
            var text = NormalizeForNegation(message);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(
                text,
                @"\b(?:toi|minh|em|anh|chi)?\s*(?:muon|can|dinh|chot|lay|dat)\s+(?:mua|lay|dat|chot)?\b|\b(?:mua|chot|dat\s+coc|lay)\s+(?:xe|mau|con)\b",
                RegexOptions.IgnoreCase);
        }

        private static string? ResolveReferencedProductName(CustomerPreferenceProfile? profile)
        {
            if (profile == null)
                return null;

            if (!string.IsNullOrWhiteSpace(profile.LastLookupProductName))
                return profile.LastLookupProductName.Trim();

            var mentioned = profile.LastMentionedProducts?
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(mentioned))
                return mentioned.Trim();

            var recommended = profile.LastRecommendedProducts?
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(recommended))
                return recommended.Trim();

            return profile.LastSearchProductNames?
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?
                .Trim();
        }

        private static string? InferBrandFromProductName(string? productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
                return null;

            var normalized = NormalizeForNegation(productName);
            foreach (var (token, displayName) in KnownBrandAliases())
            {
                if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase))
                    return displayName;
            }

            return null;
        }

        private static NegationSignals ExtractExplicitNegations(string message)
        {
            var text = NormalizeForNegation(message);
            var result = new NegationSignals();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (var (token, displayName) in KnownBrandAliases())
            {
                if (IsTermExplicitlyNegated(text, token, "hang", "xe"))
                    result.Brands.Add(displayName);
            }

            foreach (var (token, displayName) in KnownCategoryAliases())
            {
                if (IsTermExplicitlyNegated(text, token, "loai", "dong", "xe"))
                    result.Categories.Add(displayName);
            }

            foreach (var (token, displayName) in KnownProductAliases())
            {
                if (IsTermExplicitlyNegated(text, token, "mau", "xe", "dong"))
                    result.Products.Add(displayName);
            }

            foreach (var (token, displayName) in KnownTargetAliases())
            {
                if (IsTermExplicitlyNegated(text, token, "doi tuong", "phong cach", "xe"))
                    result.Targets.Add(displayName);
            }

            foreach (var (token, displayName) in KnownStyleAliases())
            {
                if (IsTermExplicitlyNegated(text, token, "phong cach", "kieu", "xe"))
                    result.Styles.Add(displayName);
            }

            result.InstallmentNegated =
                IsTermExplicitlyNegated(text, "tra gop", "hinh thuc") ||
                Regex.IsMatch(text, @"\b(?:khong|ko|k|chua)\s+(?:can|muon|thich|lay|chon|mua)?\s*tra\s*gop\b", RegexOptions.IgnoreCase);

            return result;
        }

        private static bool IsTermExplicitlyNegated(string text, string term, params string[] optionalNouns)
        {
            var escaped = Regex.Escape(term);
            var nounPattern = optionalNouns.Length == 0
                ? string.Empty
                : $"(?:(?:{string.Join("|", optionalNouns.Select(Regex.Escape))})\\s+)?";

            var patterns = new[]
            {
                $@"\bkhong\s+phai\s+{nounPattern}{escaped}\b",
                $@"\bchu\s+khong\s+phai\s+{nounPattern}{escaped}\b",
                $@"\bkhac\s+khong\s+phai\s+{nounPattern}{escaped}\b",
                $@"\bkhong\s+(?:lay|chon|mua|thich|muon|can|uu\s+tien|goi\s+y|tu\s+van)?\s*{nounPattern}{escaped}\b",
                $@"\bdung\s+(?:lay|chon|mua|goi\s+y|tu\s+van|dua|de\s+xuat)\s+{nounPattern}{escaped}\b",
                $@"\b(?:tru|ngoai\s+tru|ne|bo|loai|loai\s+tru)\s+{nounPattern}{escaped}\b"
            };

            return patterns.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));
        }

        private static IEnumerable<(string Token, string DisplayName)> KnownBrandAliases()
        {
            yield return ("honda", "Honda");
            yield return ("yamaha", "Yamaha");
            yield return ("suzuki", "Suzuki");
            yield return ("sym", "SYM");
            yield return ("piaggio", "Piaggio");
        }

        private static IEnumerable<(string Token, string DisplayName)> KnownCategoryAliases()
        {
            yield return ("xe ga", "xe ga");
            yield return ("tay ga", "xe ga");
            yield return ("xe so", "xe số");
            yield return ("con tay", "côn tay");
            yield return ("xe con", "côn tay");
        }

        private static IEnumerable<(string Token, string DisplayName)> KnownTargetAliases()
        {
            yield return ("nu", "nữ");
            yield return ("nam", "nam");
            yield return ("sinh vien", "sinh viên");
            yield return ("hoc sinh", "sinh viên");
        }

        private static IEnumerable<(string Token, string DisplayName)> KnownStyleAliases()
        {
            yield return ("cao cap", "premium");
            yield return ("xe xin", "premium");
            yield return ("sang", "premium");
            yield return ("the thao", "sporty");
            yield return ("ham ho", "aggressive");
            yield return ("cop rong", "large_storage");
            yield return ("tiet kiem xang", "fuel_saving");
        }

        private static IEnumerable<(string Token, string DisplayName)> KnownProductAliases()
        {
            var products = new[]
            {
                "vision", "air blade", "airblade", "freego", "latte", "grande", "zip", "future", "wave",
                "sirius", "address", "impulse", "janus", "lead", "vario", "winner", "winner x",
                "exciter", "pcx", "sh", "sh mode", "shark", "shark mini", "attila", "attila venus"
            };

            foreach (var product in products)
                yield return (product, ToDisplayProductName(product));
        }

        private static string ToDisplayProductName(string product)
        {
            return product switch
            {
                "airblade" => "Air Blade",
                "air blade" => "Air Blade",
                "winner x" => "Winner X",
                "sh mode" => "SH Mode",
                "shark mini" => "Shark Mini",
                "attila venus" => "Attila Venus",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(product)
            };
        }

        private static bool IsSameNegatedCategory(string value, string excluded)
        {
            var actual = NormalizeForNegation(value);
            var negated = NormalizeForNegation(excluded);

            if (negated.Contains("ga"))
                return actual.Contains("ga");

            if (negated.Contains("so"))
                return actual.Contains("so");

            if (negated.Contains("con"))
                return actual.Contains("con");

            return actual.Contains(negated, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSameNegatedProduct(string value, string excluded)
        {
            var actual = NormalizeForNegation(value);
            var negated = NormalizeForNegation(excluded);

            return actual.Equals(negated, StringComparison.OrdinalIgnoreCase) ||
                   actual.Contains(negated, StringComparison.OrdinalIgnoreCase) ||
                   negated.Contains(actual, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class NegationSignals
        {
            public HashSet<string> Brands { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Products { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Styles { get; } = new(StringComparer.OrdinalIgnoreCase);
            public bool InstallmentNegated { get; set; }
        }

        private static string NormalizeForNegation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var formD = value.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);

            foreach (var ch in formD)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return Regex.Replace(
                    sb.ToString()
                        .Normalize(NormalizationForm.FormC)
                        .Replace('đ', 'd')
                        .Replace('Đ', 'D')
                        .ToLowerInvariant(),
                    @"\s+",
                    " ")
                .Trim();
        }

        private static string BuildSemanticQuery(
            string normalizedMessage,
            SemanticResult semanticResult,
            CustomerPreferenceProfile? profile)
        {
            var currentMeaning = !string.IsNullOrWhiteSpace(semanticResult?.NormalizedMeaning)
                ? semanticResult.NormalizedMeaning.Trim()
                : normalizedMessage.Trim();

            currentMeaning = EnrichSemanticQueryWithSlots(currentMeaning, semanticResult);

            var previousMeaning = profile?.LastSemanticMeaning?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(previousMeaning))
                return currentMeaning;

            if (!ShouldBlendPreviousSemanticContext(currentMeaning, semanticResult))
                return currentMeaning;

            return MergeSemanticQueryTerms(currentMeaning, previousMeaning);
        }

        private static string EnrichSemanticQueryWithSlots(string currentMeaning, SemanticResult? semanticResult)
        {
            if (semanticResult == null)
                return currentMeaning;

            var parts = new List<string> { currentMeaning };

            if (!string.IsNullOrWhiteSpace(semanticResult.Brand))
                parts.Add(semanticResult.Brand);

            if (!string.IsNullOrWhiteSpace(semanticResult.Category))
                parts.Add(semanticResult.Category);

            if (!string.IsNullOrWhiteSpace(semanticResult.Target))
                parts.Add(semanticResult.Target);

            if (!string.IsNullOrWhiteSpace(semanticResult.LookupField))
                parts.Add(semanticResult.LookupField);
                
            if (!string.IsNullOrWhiteSpace(semanticResult.PolicySlot))
                parts.Add(semanticResult.PolicySlot);

            foreach (var product in semanticResult.MentionedProducts ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(product))
                    parts.Add(product);
            }

            if (string.Equals(semanticResult.Installment, "co", StringComparison.OrdinalIgnoreCase))
                parts.Add("trả góp lãi suất trả trước vay ngân hàng công ty tài chính");

            if (string.Equals(semanticResult.Interest, "co", StringComparison.OrdinalIgnoreCase) ||
                currentMeaning.Contains("lãi suất", StringComparison.OrdinalIgnoreCase) ||
                currentMeaning.Contains("lai suat", StringComparison.OrdinalIgnoreCase))
                parts.Add("lãi suất trả góp");

            return MergeSemanticQueryTerms(string.Join(' ', parts), string.Empty);
        }

        private static bool ShouldBlendPreviousSemanticContext(string currentMeaning, SemanticResult? semanticResult)
        {
            if (string.IsNullOrWhiteSpace(currentMeaning))
                return false;

            var wordCount = currentMeaning
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;

            if (wordCount <= 5)
                return true;

            return semanticResult != null &&
                (string.Equals(semanticResult.Intent, "unknown", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(semanticResult.Intent, "refine", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(semanticResult.FlowType, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase));
        }

        private static string MergeSemanticQueryTerms(string currentMeaning, string previousMeaning)
        {
            var terms = new List<string>();

            foreach (var term in currentMeaning.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Concat(previousMeaning.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                {
                    terms.Add(term);
                }
            }

            return string.Join(' ', terms);
        }

        private static SemanticResult MergeSemanticWithSessionContext(
            SemanticResult current,
            CustomerPreferenceProfile? profile)
        {
            if (current == null || current.IsFallback || profile?.LastSemanticResult == null)
                return current;

            var previous = profile.LastSemanticResult;
            bool shouldReuseContext =
                string.Equals(current.Intent, "unknown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.Intent, "refine", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.FlowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.FlowType, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase) ||
                IsSameIntentFamily(current.Intent, previous.Intent);

            if (!shouldReuseContext)
                return current;

            current.Brand ??= previous.Brand;
            current.Category ??= previous.Category;
            current.Target ??= previous.Target;
            current.PriceMin ??= previous.PriceMin;
            current.PriceMax ??= previous.PriceMax;
            current.TargetPrice ??= previous.TargetPrice;

            if (string.Equals(current.PriceFilterType, "none", StringComparison.OrdinalIgnoreCase))
                current.PriceFilterType = previous.PriceFilterType;

            if (string.Equals(current.PriceLevel, "khong_ro", StringComparison.OrdinalIgnoreCase))
                current.PriceLevel = previous.PriceLevel;

            if (string.Equals(current.Installment, "khong_ro", StringComparison.OrdinalIgnoreCase))
                current.Installment = previous.Installment;

            if (string.Equals(current.Interest, "khong_ro", StringComparison.OrdinalIgnoreCase))
                current.Interest = previous.Interest;

            current.ForWork = current.ForWork || previous.ForWork;
            current.ForSchool = current.ForSchool || previous.ForSchool;
            current.ForCity = current.ForCity || previous.ForCity;
            current.ForTour = current.ForTour || previous.ForTour;
            current.WantsFuelSaving = current.WantsFuelSaving || previous.WantsFuelSaving;
            current.WantsLargeStorage = current.WantsLargeStorage || previous.WantsLargeStorage;
            current.WantsEasyControl = current.WantsEasyControl || previous.WantsEasyControl;
            current.NeedsLowSeat = current.NeedsLowSeat || previous.NeedsLowSeat;
            current.HeightCm ??= previous.HeightCm;
            current.ComparisonFeature ??= previous.ComparisonFeature;
            current.LookupField ??= previous.LookupField;
            current.PolicySlot ??= previous.PolicySlot;

            if (!current.MentionedProducts.Any())
                current.MentionedProducts = new List<string>(previous.MentionedProducts);

            current.ExcludedBrands = current.ExcludedBrands
                .Concat(previous.ExcludedBrands)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            current.ExcludedCategories = current.ExcludedCategories
                .Concat(previous.ExcludedCategories)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            current.ExcludedProducts = current.ExcludedProducts
                .Concat(previous.ExcludedProducts)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            current.RequestedStyles = current.RequestedStyles
                .Concat(previous.RequestedStyles)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (string.IsNullOrWhiteSpace(current.Preference))
                current.Preference = previous.Preference;

            return current;
        }

        private static void ApplyStoredIntentContext(ParsedIntent parsedIntent, SemanticResult previousSemantic)
        {
            if (parsedIntent == null || previousSemantic == null)
                return;

            parsedIntent.Brand ??= previousSemantic.Brand;
            parsedIntent.Category ??= previousSemantic.Category;
            parsedIntent.Target ??= previousSemantic.Target;
            parsedIntent.PriceMin ??= previousSemantic.PriceMin;
            parsedIntent.PriceMax ??= previousSemantic.PriceMax;
            parsedIntent.TargetPrice ??= previousSemantic.TargetPrice;

            if (parsedIntent.FilterType == PriceFilterType.None)
                parsedIntent.FilterType = ParseSemanticPriceFilterType(previousSemantic.PriceFilterType);

            parsedIntent.ForWork = parsedIntent.ForWork || previousSemantic.ForWork;
            parsedIntent.ForSchool = parsedIntent.ForSchool || previousSemantic.ForSchool;
            parsedIntent.ForCity = parsedIntent.ForCity || previousSemantic.ForCity;
            parsedIntent.ForTour = parsedIntent.ForTour || previousSemantic.ForTour;
            parsedIntent.WantsFuelSaving = parsedIntent.WantsFuelSaving || previousSemantic.WantsFuelSaving;
            parsedIntent.WantsLargeStorage = parsedIntent.WantsLargeStorage || previousSemantic.WantsLargeStorage;
            parsedIntent.WantsEasyControl = parsedIntent.WantsEasyControl || previousSemantic.WantsEasyControl;
            parsedIntent.NeedsLowSeat = parsedIntent.NeedsLowSeat || previousSemantic.NeedsLowSeat;
            parsedIntent.HeightCm ??= previousSemantic.HeightCm;
            parsedIntent.LookupField ??= previousSemantic.LookupField;
            parsedIntent.ComparisonFeature ??= previousSemantic.ComparisonFeature;

            parsedIntent.ExcludedBrands.UnionWith(CleanList(previousSemantic.ExcludedBrands));
            parsedIntent.ExcludedCategories.UnionWith(CleanList(previousSemantic.ExcludedCategories));
            parsedIntent.ExcludedProducts.UnionWith(CleanList(previousSemantic.ExcludedProducts));
            parsedIntent.RequestedStyles.UnionWith(CleanList(previousSemantic.RequestedStyles));

            if (!parsedIntent.MentionedProducts.Any())
            {
                parsedIntent.MentionedProducts = previousSemantic.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static bool IsSameIntentFamily(string? currentIntent, string? previousIntent)
        {
            if (string.IsNullOrWhiteSpace(currentIntent) || string.IsNullOrWhiteSpace(previousIntent))
                return false;

            if (string.Equals(currentIntent, previousIntent, StringComparison.OrdinalIgnoreCase))
                return true;

            bool currentIsRecommendationFamily =
                string.Equals(currentIntent, "recommend", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentIntent, "refine", StringComparison.OrdinalIgnoreCase);

            bool previousIsRecommendationFamily =
                string.Equals(previousIntent, "recommend", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(previousIntent, "refine", StringComparison.OrdinalIgnoreCase);

            return currentIsRecommendationFamily && previousIsRecommendationFamily;
        }

        private static bool IsServiceOrPolicyIntent(string? intentType)
        {
            return string.Equals(intentType, ChatFlowType.ServiceInfo, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intentType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetServiceFlowType(string? intentType)
        {
            return string.Equals(intentType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase)
                ? ChatFlowType.PolicyInfo
                : ChatFlowType.ServiceInfo;
        }

        private static ParsedIntent BuildIntentFromSemantic(
            SemanticResult semanticResult,
            string semanticMessage)
        {
            var parsedIntent = new ParsedIntent
            {
                RawMessage = semanticMessage,
                IntentType = semanticResult.Intent,
                RouteFlow = semanticResult.FlowType,
                Brand = semanticResult.Brand,
                Category = semanticResult.Category,
                Target = semanticResult.Target,
                PriceMin = semanticResult.PriceMin,
                PriceMax = semanticResult.PriceMax,
                TargetPrice = semanticResult.TargetPrice,
                FilterType = ParseSemanticPriceFilterType(semanticResult.PriceFilterType),
                ForWork = semanticResult.ForWork,
                ForSchool = semanticResult.ForSchool,
                ForCity = semanticResult.ForCity,
                ForTour = semanticResult.ForTour,
                WantsFuelSaving = semanticResult.WantsFuelSaving,
                WantsLargeStorage = semanticResult.WantsLargeStorage,
                WantsEasyControl = semanticResult.WantsEasyControl,
                NeedsLowSeat = semanticResult.NeedsLowSeat,
                HeightCm = semanticResult.HeightCm,
                MentionedProducts = semanticResult.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ComparisonFeature = semanticResult.ComparisonFeature,
                LookupField = semanticResult.LookupField,
                PolicySlot = semanticResult.PolicySlot
            };

            parsedIntent.ExcludedBrands.UnionWith(CleanList(semanticResult.ExcludedBrands));
            parsedIntent.ExcludedCategories.UnionWith(CleanList(semanticResult.ExcludedCategories));
            parsedIntent.ExcludedProducts.UnionWith(CleanList(semanticResult.ExcludedProducts));
            parsedIntent.RequestedStyles.UnionWith(CleanList(semanticResult.RequestedStyles));

            parsedIntent.PrefersFemaleStyle = string.Equals(parsedIntent.Target, "nữ", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(parsedIntent.Target, "nu", StringComparison.OrdinalIgnoreCase);
            parsedIntent.PrefersMaleStyle = string.Equals(parsedIntent.Target, "nam", StringComparison.OrdinalIgnoreCase);

            ApplyFlowFlagsFromFlowType(parsedIntent, semanticResult.FlowType);
            return parsedIntent;
        }

        private static void ApplyFlowFlagsFromFlowType(ParsedIntent parsedIntent, string? flowType)
        {
            parsedIntent.IsDirectProductLookup = false;
            parsedIntent.IsProductSearch = false;
            parsedIntent.IsOpenRecommendation = false;
            parsedIntent.IsDirectCompare = false;
            parsedIntent.IsBrandSwitch = false;

            switch (flowType)
            {
                case ChatFlowType.Greeting:
                    parsedIntent.IsGreeting = true;
                    break;
                case ChatFlowType.OutOfScope:
                    parsedIntent.IsOutOfScope = true;
                    break;
                case ChatFlowType.OrderLookup:
                    parsedIntent.IsOrderLookup = true;
                    parsedIntent.LookupTargetType = "order";
                    parsedIntent.HasDeterministicProductIntent = false;
                    break;
                case ChatFlowType.ProductLookup:
                    parsedIntent.IsDirectProductLookup = true;
                    parsedIntent.LookupTargetType = "product";
                    parsedIntent.HasDeterministicProductIntent = true;
                    break;
                case ChatFlowType.ProductSearch:
                    parsedIntent.IsProductSearch = true;
                    parsedIntent.HasDeterministicProductIntent = true;
                    break;
                case ChatFlowType.Recommendation:
                    parsedIntent.IsOpenRecommendation = true;
                    parsedIntent.HasDeterministicProductIntent = false;
                    break;
                case ChatFlowType.ServiceInfo:
                case ChatFlowType.PolicyInfo:
                    parsedIntent.HasDeterministicProductIntent = false;
                    break;
                case ChatFlowType.Refinement:
                case ChatFlowType.RecommendationFollowUp:
                    parsedIntent.IsFollowUp = true;
                    parsedIntent.FollowUpType ??= "refine";
                    parsedIntent.HasDeterministicProductIntent = true;
                    break;
                case ChatFlowType.Compare:
                    parsedIntent.IsDirectCompare = true;
                    parsedIntent.HasDeterministicProductIntent = true;
                    break;
            }
        }

        private static FlowRoutingResult RouteFromSemanticIntent(
            SemanticResult semanticResult,
            FlowRoutingResult baseRouting)
        {
            if (!IsKnownFlowType(semanticResult.FlowType))
                return baseRouting;

            var route = DeterministicRoute(semanticResult.FlowType, "Semantic flow_type");
            if (string.Equals(semanticResult.FlowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase))
            {
                route.ShouldUseRag = true;
            }

            return route;
        }

        private static FlowRoutingResult DeterministicRoute(string flowType, string reason)
        {
            return new FlowRoutingResult
            {
                FlowType = flowType,
                ShouldUseDeterministicFlow = true,
                ShouldUseAiFallback = false,
                ShouldUseRag = false,
                Reason = reason
            };
        }

        private static bool IsKnownFlowType(string? flowType)
        {
            return string.Equals(flowType, ChatFlowType.Greeting, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.OrderLookup, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.ProductLookup, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.ProductSearch, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.Recommendation, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.Compare, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.RecommendationFollowUp, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.Refinement, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.ServiceInfo, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flowType, ChatFlowType.OutOfScope, StringComparison.OrdinalIgnoreCase);
        }

        private static PriceFilterType ParseSemanticPriceFilterType(string? value)
        {
            return value switch
            {
                "max_only" => PriceFilterType.MaxOnly,
                "min_only" => PriceFilterType.MinOnly,
                "range" => PriceFilterType.Range,
                "around" => PriceFilterType.Around,
                _ => PriceFilterType.None
            };
        }

        private static IEnumerable<string> CleanList(IEnumerable<string>? values)
        {
            return values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                ?? Enumerable.Empty<string>();
        }

        private async Task<ChatResponse> ExecuteFlowAsync(ChatOrchestrationContext context)
        {
            var flowType = context.FinalRouting.FlowType;

            var previousProductPurchase = TryHandlePreviousProductPurchaseIntent(context);
            if (previousProductPurchase != null)
                return previousProductPurchase;

            if (ShouldForceCompareFollowUp(
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

            if (string.Equals(flowType, ChatFlowType.ServiceInfo, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(flowType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase))
            {
                return await _serviceInfoFlowService.HandleAsync(
                    context.Request,
                    context.ConversationId,
                    context.NormalizedMessage,
                    context.SemanticQuery,
                    context.OriginalMessage,
                    context.EffectiveIntent,
                    context.ExistingProfile);
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

            if (context.FinalRouting.ShouldUseAiFallback)
            {
                _logger.LogInformation(
                    "Executing AI fallback. ConversationId={ConversationId}, FlowType={FlowType}, Message={Message}",
                    context.ConversationId,
                    flowType,
                    context.NormalizedMessage);

                return await ExecuteAiFallbackAsync(context);
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
    ParsedIntent effectiveIntent,
    CustomerPreferenceProfile profile)
        {
            if (profile == null)
                return false;

            if (!profile.HasActiveCompareContext || profile.LastComparedProducts == null || profile.LastComparedProducts.Count < 2)
                return false;

            bool mentionsLessThanTwoNewProducts =
                effectiveIntent.MentionedProducts == null ||
                effectiveIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() < 2;

            bool looksLikeCompareFollowUp =
                !string.IsNullOrWhiteSpace(effectiveIntent.ComparisonFeature) ||
                !string.IsNullOrWhiteSpace(effectiveIntent.LookupField) ||
                string.Equals(effectiveIntent.IntentType, "compare", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(effectiveIntent.IntentType, "refine", StringComparison.OrdinalIgnoreCase);

            return mentionsLessThanTwoNewProducts && looksLikeCompareFollowUp;
        }

        private async Task<ChatResponse> ExecuteAiFallbackAsync(ChatOrchestrationContext context)
        {
            string? ragContext = null;
            bool shouldUseRag = context.FinalRouting.ShouldUseRag;

            if (shouldUseRag)
            {
                try
                {
                    var ragQuery = string.IsNullOrWhiteSpace(context.SemanticQuery)
                        ? context.NormalizedMessage
                        : context.SemanticQuery;

                    var ragResult = await _ragService.QueryAsync(ragQuery, topK: 8);
                    if (ragResult?.Success == true && RagContextPostProcessor.HasUsableContext(ragResult.Context))
                    {
                        ragContext = RagContextPostProcessor.Filter(
                            ragResult.Context,
                            context.NormalizedMessage,
                            ragQuery,
                            context.EffectiveIntent,
                            context.ExistingProfile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "RAG fallback query failed. ConversationId={ConversationId}",
                        context.ConversationId);
                }
            }

            var effectivePrompt = BuildFallbackPrompt(context.NormalizedMessage, context.SemanticQuery, context.EffectiveIntent, context.ExistingProfile);

            var aiContext = new AIRequestContext
            {
                ConversationId = context.ConversationId,
                Channel = context.Request.Channel ?? "web",
                UserId = context.Request.UserId,
                OriginalUserMessage = context.OriginalMessage,
                EffectivePrompt = effectivePrompt,
                RagContext = ragContext
            };

            var aiResult = await _openAIService.AskAsync(aiContext);
            if ((!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Reply) || RagContextPostProcessor.LooksLikeNoDataReply(aiResult.Reply)) &&
                RagContextPostProcessor.HasUsableContext(ragContext))
            {
                aiResult.Success = true;
                aiResult.UsedAI = false;
                aiResult.Reply = BuildRagOnlyReply(ragContext!);
            }

            if (string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                aiResult.Reply = "Mình chưa có đủ dữ liệu để trả lời chính xác. Bạn có thể nói rõ hơn nhu cầu hoặc mẫu xe để mình hỗ trợ sát hơn nhé.";
            }

            return aiResult;
        }

        private static string BuildFallbackPrompt(
            string normalizedMessage,
            string semanticQuery,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile profile)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Người dùng đang hỏi về xe máy / dịch vụ liên quan tại cửa hàng.");
            sb.AppendLine($"Câu hỏi: {normalizedMessage}");
            sb.AppendLine($"Ý nghĩa dùng để truy xuất tri thức: {semanticQuery}");

            var profileSummary = profile == null
                ? string.Empty
                : $"Ngữ cảnh hội thoại: {profile.Target}, hãng ưu tiên: {profile.PreferredBrand}, loại ưu tiên: {profile.PreferredCategory}";

            if (!string.IsNullOrWhiteSpace(profileSummary))
            {
                sb.AppendLine(profileSummary);
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Brand))
            {
                sb.AppendLine($"Hãng người dùng đề cập: {parsedIntent.Brand}");
            }

            if (!string.IsNullOrWhiteSpace(parsedIntent.Category))
            {
                sb.AppendLine($"Loại xe người dùng đề cập: {parsedIntent.Category}");
            }

            sb.AppendLine("Chỉ trả lời đúng phần người dùng hỏi, không mở rộng sang thông tin khác nếu chưa được hỏi.");
            sb.AppendLine("Nếu RAG có dữ liệu cụ thể như số năm, số km, mức phí, lãi suất, thời gian hoặc điều kiện thì bắt buộc dùng đúng số liệu đó.");
            sb.AppendLine("Nếu RAG có dữ liệu liên quan hoặc gần đúng thì trích ý chính để trả lời khoảng 3 câu, giọng tự nhiên như nhân viên tư vấn.");
            sb.AppendLine("Chỉ nói chưa có đủ dữ liệu khi context hoàn toàn không liên quan; không được trả lời chung chung nếu context đã có thông tin cụ thể.");
            sb.AppendLine("Không bịa dữ liệu giá, tồn kho, chính sách.");

            return sb.ToString().Trim();
        }

        private static string BuildRagOnlyReply(string ragContext)
        {
            return RagContextPostProcessor.BuildGroundedReply(ragContext);
        }
    }
}
