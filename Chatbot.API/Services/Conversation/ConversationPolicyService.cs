using System;
using System.Collections.Generic;
using System.Linq;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services.Conversation
{
    public class ConversationPolicyService : IConversationPolicyService
    {
        private readonly IConversationContextResolver _conversationContextResolver;

        public ConversationPolicyService(
            IConversationContextResolver conversationContextResolver)
        {
            _conversationContextResolver = conversationContextResolver;
        }

        public (ParsedIntent EffectiveIntent, RecommendationContextDecision ContextDecision) ResolveEffectiveIntent(
    string conversationId,
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? existingProfile)
        {
            var safeProfile = existingProfile ?? new CustomerPreferenceProfile();
            if (parsedIntent == null)
                parsedIntent = new ParsedIntent();

            if (parsedIntent.IsOutOfScope || parsedIntent.IsNoise || parsedIntent.IsGreeting || parsedIntent.IsAck)
            {
                return (parsedIntent.Clone(), RecommendationContextDecision.None);
            }
            if (IsStrongStandaloneIntent(parsedIntent))
            {
                return (parsedIntent.Clone(), RecommendationContextDecision.None);
            }
            var contextResolution = _conversationContextResolver.Resolve(
                normalizedMessage,
                parsedIntent,
                safeProfile,
                safeProfile.ActiveFlow);

            var resolvedIntent = contextResolution.EffectiveIntent ?? parsedIntent;
            var resolvedDecision = contextResolution.ContextDecision;

            var finalDecision = DetermineFinalDecision(
                normalizedMessage,
                parsedIntent,
                safeProfile,
                resolvedDecision);

            var finalIntent = BuildIntentForDecision(
                parsedIntent,
                resolvedIntent,
                safeProfile,
                finalDecision);

            return (finalIntent, finalDecision);
        }

        public bool ShouldForceCompareFollowUp(
            string normalizedMessage,
            ParsedIntent effectiveIntent,
            CustomerPreferenceProfile? profile)
        {
            if (string.IsNullOrWhiteSpace(normalizedMessage) || profile == null)
                return false;

            if (!profile.HasActiveCompareContext || profile.LastComparedProducts == null || profile.LastComparedProducts.Count < 2)
                return false;

            if (effectiveIntent == null)
                return false;
            if (LooksLikeAlternativeRecommendationRequest(normalizedMessage))
                return false;

            if (effectiveIntent.IsDirectCompare)
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
        private static RecommendationContextDecision DetermineFinalDecision(
    string normalizedMessage,
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile safeProfile,
    RecommendationContextDecision resolvedDecision)
        {
            if (LooksLikeExplicitFreshRecommendationRequest(normalizedMessage, parsedIntent))
            {
                if (ShouldOverrideToExpandFromCurrentGoal(
                        normalizedMessage,
                        parsedIntent,
                        safeProfile,
                        RecommendationContextDecision.StartFreshRecommendation))
                {
                    return RecommendationContextDecision.ExpandFromCurrentGoal;
                }

                return RecommendationContextDecision.New;
            }

            if (ShouldOverrideToExpandFromCurrentGoal(
                    normalizedMessage,
                    parsedIntent,
                    safeProfile,
                    resolvedDecision))
            {
                return RecommendationContextDecision.ExpandFromCurrentGoal;
            }

            return resolvedDecision;
        }

        private static ParsedIntent BuildIntentForDecision(
            ParsedIntent parsedIntent,
            ParsedIntent resolvedIntent,
            CustomerPreferenceProfile safeProfile,
            RecommendationContextDecision finalDecision)
        {
            switch (finalDecision)
            {
                case RecommendationContextDecision.StartFreshRecommendation:
                    var freshIntent = SanitizeFreshRecommendationIntent(parsedIntent, resolvedIntent);
                    ApplyCurrentTurnPriceOverride(parsedIntent, freshIntent);
                    return freshIntent;

                case RecommendationContextDecision.ExpandFromCurrentGoal:
                    return MergeExpandFollowUpIntent(parsedIntent, resolvedIntent, safeProfile);

                default:
                    return resolvedIntent;
            }
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

            bool isExplicitNewRecommendation =
                ContainsAny(text,
                    "tư vấn", "tu van",
                    "gợi ý", "goi y",
                    "nên mua", "nen mua",
                    "chọn xe", "chon xe",
                    "tìm xe", "tim xe",
                    "muốn mua", "muon mua",
                    "cần xe", "can xe");

            bool hasReferenceFollowUpSignal =
                text.StartsWith("còn ") ||
                text.StartsWith("con ") ||
                text.Contains("thì sao") ||
                text.Contains("thi sao") ||
                text.Contains("vậy còn") ||
                text.Contains("vay con") ||
                text.Contains("thế còn") ||
                text.Contains("the con") ||
                text.Contains("mẫu đó") ||
                text.Contains("mau do") ||
                text.Contains("xe đó") ||
                text.Contains("xe do") ||
                text.Contains("con đó") ||
                text.Contains("con do") ||
                text.Contains("mẫu khác") ||
                text.Contains("mau khac") ||
                text.Contains("xe khác") ||
                text.Contains("xe khac") ||
                text.Contains("khác đi") ||
                text.Contains("khac di");

            // Nếu user nói rõ "tư vấn/gợi ý..." thì coi là goal mới,
            // không ép thành expand chỉ vì đang có context cũ.
            if (isExplicitNewRecommendation && !hasReferenceFollowUpSignal)
                return false;

            bool hasNewStructuredFilter =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue ||
                parsedIntent.IsBrandSwitch ||
                string.Equals(parsedIntent.IntentType, "brand_switch", StringComparison.OrdinalIgnoreCase);

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
                parsedIntent.NeedsLowSeat ||
                parsedIntent.PrefersMaleStyle ||
                parsedIntent.PrefersFemaleStyle;

            if (!introducesNewGoal && hasReferenceFollowUpSignal)
                return true;

            return introducesNewGoal && hasReferenceFollowUpSignal;
        }

        private static ParsedIntent MergeExpandFollowUpIntent(
     ParsedIntent parsedIntent,
     ParsedIntent effectiveIntent,
     CustomerPreferenceProfile profile)
        {
            var merged = effectiveIntent.Clone();

            bool currentTurnHasPrice =
                parsedIntent.FilterType != PriceFilterType.None ||
                parsedIntent.PriceMin.HasValue ||
                parsedIntent.PriceMax.HasValue ||
                parsedIntent.TargetPrice.HasValue;

            bool currentTurnHasIdentity =
                !string.IsNullOrWhiteSpace(parsedIntent.Brand) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Category) ||
                !string.IsNullOrWhiteSpace(parsedIntent.Target) ||
                parsedIntent.PrefersMaleStyle ||
                parsedIntent.PrefersFemaleStyle;

            bool currentTurnHasUseCase =
                parsedIntent.ForWork ||
                parsedIntent.ForSchool ||
                parsedIntent.ForCity ||
                parsedIntent.ForTour ||
                parsedIntent.WantsFuelSaving ||
                parsedIntent.WantsLargeStorage ||
                parsedIntent.WantsEasyControl ||
                parsedIntent.NeedsLowSeat ||
                parsedIntent.HeightCm.HasValue;

            // Chỉ giữ target/giới tính cũ nếu câu hiện tại không nói identity mới
            if (!currentTurnHasIdentity)
            {
                if (string.IsNullOrWhiteSpace(merged.Target))
                    merged.Target = profile.Target;

                if (!merged.PrefersMaleStyle)
                    merged.PrefersMaleStyle = profile.PrefersMaleStyle;

                if (!merged.PrefersFemaleStyle)
                    merged.PrefersFemaleStyle = profile.PrefersFemaleStyle;
            }

            // Chỉ giữ giá cũ nếu câu hiện tại không có giá mới
            if (!currentTurnHasPrice)
            {
                if (!merged.PriceMin.HasValue)
                    merged.PriceMin = profile.PriceMin;

                if (!merged.PriceMax.HasValue)
                    merged.PriceMax = profile.PriceMax;

                if (!merged.TargetPrice.HasValue)
                    merged.TargetPrice = profile.TargetPrice;

                if (merged.FilterType == PriceFilterType.None)
                    merged.FilterType = profile.FilterType;
            }
            else
            {
                ApplyCurrentTurnPriceOverride(parsedIntent, merged);
            }

            // Chỉ giữ use-case cũ nếu câu hiện tại không nói use-case mới
            if (!currentTurnHasUseCase)
            {
                if (!merged.ForWork)
                    merged.ForWork = profile.ForWork;

                if (!merged.ForSchool)
                    merged.ForSchool = profile.ForSchool;

                if (!merged.ForCity)
                    merged.ForCity = profile.ForCity;

                if (!merged.ForTour)
                    merged.ForTour = profile.ForTour;

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
            }

            return merged;
        }
        private static bool LooksLikeAlternativeRecommendationRequest(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return
                (text.Contains("xe nào") || text.Contains("xe nao") ||
                 text.Contains("mẫu nào") || text.Contains("mau nao") ||
                 text.Contains("con nào") || text.Contains("con nao") ||
                 text.Contains("xe khác") || text.Contains("xe khac") ||
                 text.Contains("mẫu khác") || text.Contains("mau khac"))
                &&
                (text.Contains("rẻ hơn") || text.Contains("re hon") ||
                 text.Contains("mềm hơn") || text.Contains("mem hon") ||
                 text.Contains("thấp hơn") || text.Contains("thap hon") ||
                 text.Contains("ít tiền hơn") || text.Contains("it tien hon"));
        }
        private static ParsedIntent SanitizeFreshRecommendationIntent(
            ParsedIntent parsedIntent,
            ParsedIntent effectiveIntent)
        {
            var clean = effectiveIntent.Clone();

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
                parsedIntent.ExcludedBrands ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            clean.ExcludedCategories = new HashSet<string>(
                parsedIntent.ExcludedCategories ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            clean.RequestedStyles = new HashSet<string>(
                parsedIntent.RequestedStyles ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            clean.MentionedProducts = new List<string>(
                parsedIntent.MentionedProducts ?? Enumerable.Empty<string>());

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
                    effectiveIntent.PriceMin = parsedIntent.PriceMin;
                    effectiveIntent.PriceMax = parsedIntent.PriceMax;
                    effectiveIntent.TargetPrice = parsedIntent.TargetPrice;
                    break;
            }

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
        private static bool IsStrongStandaloneIntent(ParsedIntent intent)
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
        private static bool ContainsAny(string text, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}