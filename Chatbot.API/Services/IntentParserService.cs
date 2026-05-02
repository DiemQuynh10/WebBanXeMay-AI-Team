using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Chatbot.API.Helpers;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;
using static Chatbot.API.Models.Intent.ParsedIntent;

namespace Chatbot.API.Services
{
    public class IntentParserService : IIntentParserService
    {
        private readonly IProductNameResolverService _productNameResolver;

        public IntentParserService(IProductNameResolverService productNameResolver)
        {
            _productNameResolver = productNameResolver;
        }
        private static readonly Dictionary<string, string> ProductAliasMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vision"] = "Honda Vision",
            ["honda vision"] = "Honda Vision",
            ["air blade"] = "Honda Air Blade",
            ["ab"] = "Honda Air Blade",
            ["honda air blade"] = "Honda Air Blade",
            ["freego"] = "Yamaha Freego",
            ["yamaha freego"] = "Yamaha Freego",
            ["latte"] = "Yamaha Latte",
            ["yamaha latte"] = "Yamaha Latte",
            ["grande"] = "Yamaha Grande",
            ["yamaha grande"] = "Yamaha Grande",
            ["zip"] = "Piaggio Zip 100",
            ["zip 100"] = "Piaggio Zip 100",
            ["piaggio zip"] = "Piaggio Zip 100",
            ["piaggio zip 100"] = "Piaggio Zip 100",
            ["future"] = "Honda Future",
            ["honda future"] = "Honda Future",
            ["wave"] = "Honda Wave",
            ["honda wave"] = "Honda Wave",
            ["sirius"] = "Yamaha Sirius",
            ["yamaha sirius"] = "Yamaha Sirius",
            ["address"] = "Suzuki Address 110",
            ["address 110"] = "Suzuki Address 110",
            ["suzuki address"] = "Suzuki Address 110",
            ["suzuki address 110"] = "Suzuki Address 110",
            ["impulse"] = "Suzuki Impulse 125",
            ["impulse 125"] = "Suzuki Impulse 125",
            ["suzuki impulse"] = "Suzuki Impulse 125",
            ["suzuki impulse 125"] = "Suzuki Impulse 125",
            ["janus"] = "Yamaha Janus",
            ["yamaha janus"] = "Yamaha Janus",
            ["lead"] = "Honda Lead",
            ["honda lead"] = "Honda Lead",
            ["vario"] = "Honda Vario",
            ["honda vario"] = "Honda Vario",
            ["winner"] = "Honda Winner X",
            ["winner x"] = "Honda Winner X",
            ["honda winner"] = "Honda Winner X",
            ["honda winner x"] = "Honda Winner X",
            ["exciter"] = "Yamaha Exciter",
            ["yamaha exciter"] = "Yamaha Exciter",
            ["pcx"] = "Honda PCX",
            ["honda pcx"] = "Honda PCX",
            ["sh"] = "Honda SH 150i",
            ["sh 150i"] = "Honda SH 150i",
            ["honda sh"] = "Honda SH 150i",
            ["honda sh 150i"] = "Honda SH 150i",
            ["shark"] = "SYM Shark Mini",
            ["shark mini"] = "SYM Shark Mini",
            ["sym shark"] = "SYM Shark Mini",
            ["sym shark mini"] = "SYM Shark Mini",
            ["husky"] = "SYM Husky",
            ["sym husky"] = "SYM Husky",
            ["attila"] = "SYM Attila Venus",
            ["attila venus"] = "SYM Attila Venus",
            ["sym attila"] = "SYM Attila Venus",
            ["sym attila venus"] = "SYM Attila Venus",
            ["vison"] = "Honda Vision",
            ["vission"] = "Honda Vision",
            ["visison"] = "Honda Vision",

            ["visoin"] = "Honda Vision",
            ["airblade"] = "Honda Air Blade"
        };

        private static readonly string[] KnownProducts = ProductAliasMap.Keys
            .OrderByDescending(x => x.Length)
            .ToArray();

        private static readonly string[] KnownBrands = { "Honda", "Yamaha", "Suzuki", "SYM", "Piaggio" };

        public async Task<ParsedIntent> ParseAsync(string message)
        {
            var result = new ParsedIntent
            {
                RawMessage = message ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
                return result;

            var text = Normalize(message);
            ParseExcludedCategory(text, result);
            ParseExcludedBrand(text, result);

            ParseCategory(text, result);
            ParseBrand(text, result);

            ParseTarget(text, result);
            ParseUseCases(text, result);
            ParsePreferenceFeatures(text, result);
            ParseHeightAndSeat(text, result);
            ParseStyles(text, result);
            await ParseMentionedProductsAsync(text, result);
            ApplyProductMentionSafety(text, result);
            ParseExcludedProducts(text, result);
            ParseComparisonFeature(text, result);

            ResolveBrandAndCategoryConflicts(result);
            RecommendationConstraintExtractor.Apply(text, result);
            ParseRecommendationContextSignals(text, result);

            ParseGreeting(text, result);
            ParseAck(text, result);
            ParseOutOfScope(text, result);
            ParseNoise(text, result);

            if (result.IsOutOfScope || result.IsNoise || result.IsAck)
                return result;

            ParseOrderLookup(text, result);
            ParseLookupSignals(text, result);
            if (LooksLikeRestartRecommendationRequest(text))
            {
                if (result.ExcludedBrands.Any() ||
                    result.ExcludedProducts.Any() ||
                    result.ExcludedCategories.Any())
                {
                    result.IntentType = "recommend";
                    result.IsOpenRecommendation = true;
                    result.IsFollowUp = false;
                    result.FollowUpType = null;
                    result.HasFreshConsultationSignal = true;
                    result.RouteFlow = ChatFlowType.Recommendation;
                    result.HasDeterministicProductIntent = false;
                    return result;
                }

                result.IntentType = "followup";
                result.IsFollowUp = true;
                result.FollowUpType = "restart_recommendation";
                result.RouteFlow = ChatFlowType.Recommendation;
                result.HasDeterministicProductIntent = true;
                return result;
            }
            ParseIntentType(text, result);
            ParseFollowUp(text, result);
            ParseRouteFlow(text, result);
            Console.WriteLine(
    $"[INTENT PARSER] Text={text} | IntentType={result.IntentType} | RouteFlow={result.RouteFlow} | FollowUpType={result.FollowUpType} | " +
    $"ExcludedBrands={string.Join(",", result.ExcludedBrands)} | " +
    $"ExcludedProducts={string.Join(",", result.ExcludedProducts)} | " +
    $"ExcludedCategories={string.Join(",", result.ExcludedCategories)} | " +
    $"MentionedProducts={string.Join(",", result.MentionedProducts)}");
            return result;
        }

        private static void ParseGreeting(string text, ParsedIntent result)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var trimmed = text.Trim();

            if (MatchesWholeText(trimmed,
                "hello",
                "hi",
                "alo",
                "chao",
                "xin chao",
                "chao shop",
                "xin chao shop",
                "shop oi",
                "ad oi",
                "chao ad",
                "xin chao ad"))
            {
                result.IsGreeting = true;
                return;
            }

            bool startsWithGreeting =
                trimmed.StartsWith("chao ") ||
                trimmed.StartsWith("xin chao ") ||
                trimmed.StartsWith("hello ") ||
                trimmed.StartsWith("hi ") ||
                trimmed.StartsWith("alo ");

            bool hasBusinessIntent =
                ContainsAny(trimmed,
                    "gia",
                    "bao nhieu",
                    "tu van",
                    "goi y",
                    "xe",
                    "con hang",
                    "ton kho",
                    "don hang",
                    "ma don",
                    "tra don",
                    "so sanh");

            if (startsWithGreeting && !hasBusinessIntent)
            {
                result.IsGreeting = true;
            }
        }

        private static void ParseRecommendationContextSignals(string text, ParsedIntent result)
        {
            bool hasBudgetSignal =
                result.PriceMin.HasValue ||
                result.PriceMax.HasValue ||
                result.TargetPrice.HasValue ||
                LooksLikeBudgetFragment(text);

            bool hasUseCaseSignal =
                !string.IsNullOrWhiteSpace(result.Target) ||
                result.ForSchool ||
                result.ForWork ||
                result.ForCity ||
                result.ForTour;

            bool hasHardConstraintSignal =
     !string.IsNullOrWhiteSpace(result.Brand) ||
     !string.IsNullOrWhiteSpace(result.Category) ||
     result.ExcludedBrands.Any() ||
     result.ExcludedCategories.Any() ||
     result.ExcludedProducts.Any();

            bool hasStrongPreferenceSignal =
                result.WantsLargeStorage ||
                result.WantsFuelSaving ||
                result.NeedsLowSeat ||
                result.WantsEasyControl ||
                result.RequestedStyles.Any();

            bool looksStandaloneFresh =
                text.StartsWith("tu van") ||
                text.StartsWith("xe ") ||
                text.StartsWith("cho minh ") ||
                text.StartsWith("minh can ") ||
                text.StartsWith("toi muon ") ||
                text.StartsWith("cho nu ") ||
                text.StartsWith("cho nam ") ||
                text.StartsWith("tim xe ") ||
                text.StartsWith("can xe ");

            bool looksExpandFollowUp =
                text.StartsWith("neu ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("chi ") ||
                text.StartsWith("bo ") ||
                text.StartsWith("khong thich ") ||
                text.StartsWith("khong muon ") ||
                text.StartsWith("chi lay ") ||
                text.StartsWith("xe ga ") ||
                text.StartsWith("xe so ") ||
                text.StartsWith("con tay ") ||
                text.StartsWith("quanh ") ||
                text.StartsWith("tam ") ||
                text.StartsWith("khoang ") ||
                text.StartsWith("doi sang ") ||
                text.StartsWith("con ");

            bool looksNarrowFollowUp =
                ContainsAny(text,
                    "hon",
                    "nao hon",
                    "tot hon",
                    "hop hon",
                    "rong hon",
                    "thap hon",
                    "em hon",
                    "gon hon",
                    "nhe hon",
                    "thi sao",
                    "trong nhom nay",
                    "trong may mau nay",
                    "mau nao");

            if (looksStandaloneFresh && (hasUseCaseSignal || hasBudgetSignal || hasHardConstraintSignal || hasStrongPreferenceSignal))
            {
                result.HasFreshConsultationSignal = true;
                result.RecommendationContextActionHint = "fresh";
                return;
            }

            if (looksExpandFollowUp && (hasBudgetSignal || hasHardConstraintSignal || hasStrongPreferenceSignal))
            {
                result.HasExpandRecommendationSignal = true;
                result.RecommendationContextActionHint = "expand";
                return;
            }

            if (looksNarrowFollowUp && (result.ComparisonFeature != null || hasStrongPreferenceSignal || result.MentionedProducts.Count <= 1))
            {
                result.HasNarrowRefinementSignal = true;
                result.RecommendationContextActionHint = "narrow";
            }
        }

        private static void ParseOutOfScope(string text, ParsedIntent result)
        {
            if (ContainsAny(text,
    "khong thich xe may",
    "khong muon xe may",
    "khong can xe may",
    "khong mua xe may"))
            {
                result.IsOutOfScope = true;
                result.IntentType = "out_of_scope";
                result.RouteFlow = ChatFlowType.OutOfScope;
                return;
            }
            if (ContainsAny(text,
    "xe dien",
    "xe dap",
    "xe dap dien",
    "o to",
    "oto",
    "o tu",
    "xe hoi",
    "xe bon banh",
    "co o to",
    "co oto",
    "co o tu",
    "co xe hoi",
    "shop co o to",
    "shop co oto",
    "shop co o tu",
    "cua hang co o to",
    "cua hang co oto",
    "cua hang co o tu",
    "co o to khong",
    "co oto khong",
    "co o tu khong",
    "shop co o tu khong",
    "cua hang co o tu khong"))
            {
                result.IsOutOfScope = true;
                result.IntentType = "out_of_scope";
                result.RouteFlow = ChatFlowType.OutOfScope;
                return;
            }
            if (ContainsAny(text, "xe bay", "may bay", "oto bay", "o to bay"))
            {
                result.IsOutOfScope = true;
                result.IntentType = "out_of_scope";
                result.RouteFlow = ChatFlowType.OutOfScope;
                return;
            }
            if (text.Contains("mau khac") ||
    text.Contains("xe khac") ||
    text.Contains("khac di") ||
    text.Contains("doi mau") ||
    text.Contains("goi y khac"))
            {
                return;
            }
            if (ContainsAny(text,
    "xe bay",
    "oto bay",
    "ô tô bay",
    "may bay",
    "máy bay"))
            {
                result.IsOutOfScope = true;
                result.IntentType = "out_of_scope";
                result.RouteFlow = ChatFlowType.OutOfScope;
                return;
            }
            bool hasMotorbikeSignal =
                result.MentionedProducts.Any()
                || result.ExcludedProducts.Any()
|| result.ExcludedBrands.Any()
|| result.ExcludedCategories.Any()
                || !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category)
                || !string.IsNullOrWhiteSpace(result.Target)
                || result.PriceMin.HasValue
                || result.PriceMax.HasValue
                || result.TargetPrice.HasValue
                || result.ForSchool
                || result.ForWork
                || result.ForCity
                || result.ForTour
                || result.WantsEasyControl
                || result.WantsFuelSaving
                || result.WantsLargeStorage
                || result.NeedsLowSeat
                || result.RequestedStyles.Count > 0
                || ContainsAny(text,
                    "xe",
                    "xe may",
                    "xe ga",
                    "xe so",
                    "con tay",
                    "tu van",
                    "goi y",
                    "phu hop",
                    "nen mua",
                    "gia",
                    "bao nhieu",
                    "ton kho",
                    "con hang",
                    "don hang",
                    "ma don",
                    "tra don",
                    "kiem tra don");

            if (hasMotorbikeSignal)
                return;

            if (ContainsAny(text,
     "thoi tiet",
     "thoi su",
     "bitcoin",
     "crypto",
     "chung khoan",
     "co phieu",
     "bong da",
     "the thao",
     "lap trinh",
     "viet code",
     "code java",
     "code python",
     "toan",
     "ly",
     "hoa",
     "phim",
     "game",
     "tu vi",
     "tinh yeu"))
            {
                result.IsOutOfScope = true;
                result.IntentType = "out_of_scope";
                result.RouteFlow = ChatFlowType.OutOfScope;
                return;
            }
        }

        private static void ParseOrderLookup(string text, ParsedIntent result)
        {
            if (ContainsAny(text,
                    "don hang",
                    "ma don",
                    "kiem tra don",
                    "tra don",
                    "tinh trang don",
                    "don cua toi"))
            {
                result.IsOrderLookup = true;
                result.LookupTargetType = "order";
                result.IntentType = "order_lookup";
                result.HasDeterministicProductIntent = false;
            }
        }

        private static void ParseLookupSignals(string text, ParsedIntent result)
        {
            if (result.ExcludedProducts.Any())
            {
                result.IsDirectProductLookup = false;
                result.LookupTargetType = null;
                result.LookupField = null;
                return;
            }

            if (LooksLikeExplicitProductListRequest(text))
            {
                result.IsDirectProductLookup = false;
                result.LookupTargetType = null;
                result.LookupField = null;
                result.MentionedProducts.Clear();

                result.IntentType = "product_search";
                result.IsProductSearch = true;
                result.HasDeterministicProductIntent = true;
                return;
            }

            bool hasMentionedProduct = result.MentionedProducts.Count >= 1;

            bool hasRecommendationCue = ContainsAny(text,
                "tu van",
                "goi y",
                "phu hop",
                "nen mua",
                "xe nao",
                "chon xe",
                "mua xe");

            bool hasCompareCue = ContainsAny(text,
                "so sanh",
                "so voi",
                "khac nhau",
                "uu nhuoc",
                "tot hon",
                "hop hon",
                "re hon",
                "dat hon",
                "rong hon",
                "thap hon");

            if (hasRecommendationCue || hasCompareCue)
                return;

            bool asksCc = Regex.IsMatch(text, @"\bcc\b", RegexOptions.IgnoreCase)
               || ContainsAny(text, "bao nhieu cc", "bao nhieu phan khoi", "dung tich", "phan khoi");
            bool asksStock = ContainsAny(text,
      "con hang",
      "ton kho",
      "con khong",
      "con ko",
      "con k",
      "co khong",
      "co ko",
      "co k",
      "het hang",
      "co san",
      "con may chiec",
      "may chiec",
      "bao nhieu chiec",
      "con bao nhieu",
      "so luong",
      "ton bao nhieu")
      || Regex.IsMatch(text, @"\bcon\s+.+\s+(khong|ko|k)\b", RegexOptions.IgnoreCase)
      || Regex.IsMatch(text, @"\b.+\s+con\s+(khong|ko|k)\b", RegexOptions.IgnoreCase)
      || Regex.IsMatch(text, @"\b.+\s+co\s+(khong|ko|k)\b", RegexOptions.IgnoreCase)
      || Regex.IsMatch(text, @"\b.+\s+con\s+hang\s+(khong|ko|k)\b", RegexOptions.IgnoreCase);

            bool asksPrice = ContainsAny(text, "gia", "may tien")
                             || (text.Contains("bao nhieu") && !asksCc);

            bool asksDetail = ContainsAny(text,
                "chi tiet",
                "thong tin",
                "mo ta",
                "co gi",
                "co gi noi bat",
                "noi bat",
                "tu van ve",
                "tu van them",
                "noi them",
                "review",
                "danh gia",
                "xe nay the nao",
                "mau nay the nao",
                "xem chi tiet");

            if (asksCc)
                result.LookupField = "cc";
            else if (asksStock)
                result.LookupField = "stock";
            else if (asksPrice)
                result.LookupField = "price";
            else if (asksDetail)
                result.LookupField = "detail";

            bool looksLikeSpecificUnknownProductLookup =
       !hasMentionedProduct &&
       !string.IsNullOrWhiteSpace(result.LookupField) &&
       Regex.IsMatch(
           text,
           @"^(?:xe\s+)?[a-z0-9\s]{2,80}\s+(?:co|con|con hang)\s+(?:khong|ko|k)$",
           RegexOptions.IgnoreCase);

            if (looksLikeSpecificUnknownProductLookup)
            {
                result.IsDirectProductLookup = true;
                result.LookupTargetType = "product";
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (hasMentionedProduct && !string.IsNullOrWhiteSpace(result.LookupField))
            {
                result.IsDirectProductLookup = true;
                result.LookupTargetType = "product";
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (hasMentionedProduct &&
                result.MentionedProducts.Count == 1 &&
                IsBareProductMention(text, result.MentionedProducts[0]))
            {
                result.IsDirectProductLookup = true;
                result.LookupTargetType = "product";
                result.LookupField = "detail";
                result.HasDeterministicProductIntent = true;
            }
        }

        private static void ParseIntentType(string text, ParsedIntent result)
        {
           
            if (result.IsGreeting)
            {
                result.IntentType = "greeting";
                return;
            }

            if (result.IsOutOfScope)
            {
                result.IntentType = "out_of_scope";
                return;
            }

            if (result.IsOrderLookup)
            {
                result.IntentType = "order_lookup";
                return;
            }
            if (IsExplicitCompareIntent(text, result.MentionedProducts.Count, result.ComparisonFeature))
            {
                result.IntentType = "compare";
                result.IsDirectCompare = true;
                result.HasDeterministicProductIntent = true;

                result.ExcludedBrands.Clear();
                result.ExcludedProducts.Clear();
                result.ExcludedCategories.Clear();

                return;
            }

            if (LooksLikeCompareWithBrandOnly(text, result))
            {
                result.IntentType = "compare";
                result.IsDirectCompare = false;
                result.IsFollowUp = false;
                result.FollowUpType = "compare_missing_product";
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (LooksLikeExplicitProductListRequest(text))
            {
                result.IntentType = "product_search";
                result.IsProductSearch = true;
                result.IsOpenRecommendation = false;
                result.IsFollowUp = false;
                result.FollowUpType = null;
                result.Action = ConversationAction.None;
                result.KeepConstraints = false;
                result.ExcludePreviousProducts = false;
                result.ExcludePreviousBrands = false;
                result.HasFreshConsultationSignal = false;
                result.HasExpandRecommendationSignal = false;
                result.HasNarrowRefinementSignal = false;
                result.HasDeterministicProductIntent = true;

                result.ExcludedProducts.Clear();

                return;
            }
            if (LooksLikePickBestRequest(text))
            {
                result.IntentType = "followup";
                result.IsFollowUp = true;
                result.FollowUpType = "pick_best";
                result.RouteFlow = ChatFlowType.RecommendationFollowUp;
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (IsFollowUpCompareQuestion(text))
            {
                result.IntentType = "followup";
                result.IsFollowUp = true;
                result.FollowUpType = "compare_feature";
                result.RouteFlow = ChatFlowType.Compare;

                if (ContainsAny(text, "re hon", "gia"))
                    result.ComparisonFeature = "price";

                return;
            }
            if (result.ExcludedProducts.Any() ||
      result.ExcludedBrands.Any() ||
      result.ExcludedCategories.Any())
            {
                if (result.HasFreshConsultationSignal ||
                    !string.IsNullOrWhiteSpace(result.Category) ||
                    !string.IsNullOrWhiteSpace(result.Brand) ||
                    !string.IsNullOrWhiteSpace(result.Target) ||
                    result.ForSchool ||
                    result.ForWork ||
                    result.ForCity ||
                    result.ForTour ||
                    result.PriceMin.HasValue ||
                    result.PriceMax.HasValue ||
                    result.TargetPrice.HasValue)
                {
                    result.IntentType = "recommend";
                    result.IsOpenRecommendation = true;
                    result.IsFollowUp = false;
                    result.FollowUpType = null;
                    result.HasDeterministicProductIntent = false;
                    return;
                }

                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "exclude";
                result.IsDirectCompare = false;
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (IsSwitchBrandIntent(text, result.Brand))
            {
                result.IntentType = "brand_switch";
                result.IsBrandSwitch = true;
                result.IsFollowUp = true;
                result.FollowUpType = "switch_brand";
                result.IsDirectCompare = false;
                result.ComparisonFeature = null;
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (result.ExcludedProducts.Any())
            {
                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "exclude_product";
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (LooksLikeChangeProductIntent(text))
            {
                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "change_product";

                result.Action = ConversationAction.ChangeProduct;
                result.KeepConstraints = true;
                result.ExcludePreviousProducts = true;

                result.HasNarrowRefinementSignal = true;
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (result.IsDirectProductLookup)
            {
                result.IntentType = "product_lookup";
                return;
            }
            if (result.HasExpandRecommendationSignal)
            {
                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "expand_recommendation";
                result.HasDeterministicProductIntent = true;
                return;
            }
            if (result.HasNarrowRefinementSignal && LooksLikeContextDependentFollowUp(text, result))
            {
                result.IntentType = "followup";
                result.IsFollowUp = true;
                result.FollowUpType = "narrow_refinement";
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (IsRefineIntent(text))
            {
                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "refine";
                result.HasDeterministicProductIntent = true;
                return;
            }
         
            if (IsRecommendationFollowUpIntent(text, result))
            {
                result.IntentType = "followup";
                result.IsFollowUp = true;
                result.FollowUpType = "rerank_previous_list";
                result.HasDeterministicProductIntent = true;
                return;
            }
 
            if (result.HasFreshConsultationSignal)
            {
                result.IntentType = "recommend";
                result.IsOpenRecommendation = true;
                result.HasDeterministicProductIntent = false;
                return;
            }

            if (LooksLikeProductSearch(text, result))
            {
                result.IntentType = "product_search";
                result.IsProductSearch = true;
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (LooksLikeOpenRecommendation(text, result))
            {
                result.IntentType = "recommend";
                result.IsOpenRecommendation = true;
                return;
            }

            result.IntentType = "unknown";
        }

        private static void ParseFollowUp(string text, ParsedIntent result)
        {
            if (result.IntentType == "compare")
            {
                result.IsFollowUp = result.MentionedProducts.Count < 2;
                if (result.IsFollowUp)
                    result.FollowUpType ??= "compare";
                return;
            }

            if (result.IntentType == "refine" ||
                result.IntentType == "followup" ||
                result.IntentType == "brand_switch")
            {
                result.IsFollowUp = true;
            }

            if (result.HasFreshConsultationSignal)
            {
                result.IsFollowUp = false;
            }
        }
        private static bool LooksLikePickBestRequest(string text)
        {
            return ContainsAny(text,
                "chon giup 1 xe",
                "chon giup mot xe",
                "chon cho toi 1 xe",
                "chon cho minh 1 xe",
                "chon 1 xe tot nhat",
                "chon mot xe tot nhat",
                "chon ra 1 mau",
                "chon ra mot mau",
                "chon ra 1 xe",
                "chon ra mot xe",
                "chon 1 mau phu hop nhat",
                "chon mot mau phu hop nhat",
                "chon ra 1 mau phu hop nhat",
                "chon ra mot mau phu hop nhat",
                "xe nao tot nhat",
                "xe nao dang mua nhat",
                "mau nao tot nhat",
                "mau nao dang mua nhat",
                "mau nao phu hop nhat",
                "xe nao phu hop nhat",
                "chot giup 1 xe",
                "chot giup mot xe",
                "nen chon xe nao",
                "nen mua xe nao");
        }
        private static void ParseRouteFlow(string text, ParsedIntent result)
        {
            if (result.IsGreeting)
            {
                result.RouteFlow = ChatFlowType.Greeting;
                return;
            }

            if (result.IsOutOfScope)
            {
                result.RouteFlow = ChatFlowType.OutOfScope;
                return;
            }

            if (result.IsOrderLookup)
            {
                result.RouteFlow = ChatFlowType.OrderLookup;
                return;
            }
            if (string.Equals(result.FollowUpType, "compare_missing_product", StringComparison.OrdinalIgnoreCase))
            {
                result.RouteFlow = ChatFlowType.Unknown;
                return;
            }
            if (result.IsDirectCompare && result.MentionedProducts.Count >= 2)
            {
                result.RouteFlow = ChatFlowType.Compare;
                return;
            }

            if (result.IsDirectProductLookup)
            {
                result.RouteFlow = ChatFlowType.ProductLookup;
                return;
            }
            if (string.Equals(result.FollowUpType, "restart_recommendation", StringComparison.OrdinalIgnoreCase))
            {
                result.RouteFlow = ChatFlowType.Recommendation;
                return;
            }
            if (string.Equals(result.FollowUpType, "compare_feature", StringComparison.OrdinalIgnoreCase))
            {
                result.RouteFlow = ChatFlowType.Compare;
                return;
            }
            if (result.IntentType == "followup")
            {
                result.RouteFlow = ChatFlowType.RecommendationFollowUp;
                return;
            }

            if (result.IntentType == "refine")
            {
                result.RouteFlow = ChatFlowType.Refinement;
                return;
            }

            if (result.IntentType == "brand_switch")
            {
                result.RouteFlow = ChatFlowType.BrandSwitch;
                return;
            }

            if (result.IsProductSearch)
            {
                result.RouteFlow = ChatFlowType.ProductSearch;
                return;
            }

            if (result.HasFreshConsultationSignal || result.IsOpenRecommendation)
            {
                result.RouteFlow = ChatFlowType.Recommendation;
                return;
            }

            result.RouteFlow = ChatFlowType.Unknown;
        }

        private static void ResolveBrandAndCategoryConflicts(ParsedIntent result)
        {
            if (!string.IsNullOrWhiteSpace(result.Brand) &&
                result.ExcludedBrands.Contains(result.Brand))
            {
                result.Brand = null;
            }

            if (!string.IsNullOrWhiteSpace(result.Category) &&
                result.ExcludedCategories.Contains(result.Category))
            {
                result.Category = null;
            }
        }

        private static void ParseCategory(string text, ParsedIntent result)
        {
            if (HasPositiveCategorySignal(text, "xe ga"))
            {
                result.Category = "xe ga";
                result.ExcludedCategories.Remove("xe ga");
                return;
            }

            if (HasPositiveCategorySignal(text, "xe số"))
            {
                result.Category = "xe số";
                result.ExcludedCategories.Remove("xe số");
                return;
            }

            if (HasPositiveCategorySignal(text, "côn tay"))
            {
                result.Category = "côn tay";
                result.ExcludedCategories.Remove("côn tay");
                return;
            }

            if (!result.ExcludedCategories.Contains("xe ga") &&
                ContainsAny(text, "xe ga", "tay ga", "scooter"))
            {
                result.Category = "xe ga";
                return;
            }

            if (!result.ExcludedCategories.Contains("xe số") &&
                ContainsAny(text, "xe so", "xe số"))
            {
                result.Category = "xe số";
                return;
            }

            if (!result.ExcludedCategories.Contains("côn tay") &&
                ContainsAny(text, "con tay", "côn tay", "xe con", "xe côn"))
            {
                result.Category = "côn tay";
            }
        }
        private static bool IsBareProductMention(string text, string productName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(productName))
                return false;

            var normalizedProduct = Normalize(productName);

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        normalizedProduct
    };

            foreach (var alias in ProductAliasMap)
            {
                if (string.Equals(alias.Value, productName, StringComparison.OrdinalIgnoreCase))
                    allowed.Add(alias.Key);
            }

            return allowed.Any(x => string.Equals(text.Trim(), x, StringComparison.OrdinalIgnoreCase));
        }
        private static void ParseExcludedCategory(string text, ParsedIntent result)
        {
            bool likesXeGa = HasPositiveCategorySignal(text, "xe ga");
            bool likesXeSo = HasPositiveCategorySignal(text, "xe số");
            bool likesConTay = HasPositiveCategorySignal(text, "côn tay");

            if (!likesXeGa && ContainsAny(text,
                "khong thich xe ga",
                "khong muon xe ga",
                "ne xe ga",
                "ghet xe ga",
                "dung xe ga",
                "bo xe ga",
                "loai xe ga"))
            {
                result.ExcludedCategories.Add("xe ga");
            }

            if (!likesXeSo && ContainsAny(text,
                "khong thich xe so",
                "khong muon xe so",
                "ne xe so",
                "ghet xe so",
                "dung xe so",
                "bo xe so",
                "loai xe so"))
            {
                result.ExcludedCategories.Add("xe số");
            }

            if (!likesConTay && ContainsAny(text,
                "khong thich xe con",
                "khong muon xe con",
                "ne xe con",
                "ghet xe con",
                "khong thich con tay",
                "dung xe con",
                "dung con tay",
                "bo xe con",
                "bo con tay",
                "loai xe con",
                "loai con tay"))
            {
                result.ExcludedCategories.Add("côn tay");
            }
        }

        private static void ParseBrand(string text, ParsedIntent result)
        {
            if (!result.ExcludedBrands.Contains("Honda") && HasWholeWord(text, "honda"))
            {
                result.Brand = "Honda";
                return;
            }

            if (!result.ExcludedBrands.Contains("Yamaha") && HasWholeWord(text, "yamaha"))
            {
                result.Brand = "Yamaha";
                return;
            }

            if (!result.ExcludedBrands.Contains("Suzuki") && HasWholeWord(text, "suzuki"))
            {
                result.Brand = "Suzuki";
                return;
            }

            if (!result.ExcludedBrands.Contains("SYM") && HasWholeWord(text, "sym"))
            {
                result.Brand = "SYM";
                return;
            }

            if (!result.ExcludedBrands.Contains("Piaggio") && HasWholeWord(text, "piaggio"))
            {
                result.Brand = "Piaggio";
            }
        }

        private static void ParseExcludedBrand(string text, ParsedIntent result)
        {
            if (text.Contains("co phai") || text.Contains("phai khong"))
                return;

            if (!HasNegativePreferenceSignal(text))
                return;

            foreach (var brand in KnownBrands)
            {
                var normalizedBrand = Normalize(brand);

                if (MentionedAfterNegativeSignal(text, normalizedBrand) ||
                    Regex.IsMatch(text, $@"\b{Regex.Escape(normalizedBrand)}\b", RegexOptions.IgnoreCase))
                {
                    result.ExcludedBrands.Add(brand);

                    if (string.Equals(result.Brand, brand, StringComparison.OrdinalIgnoreCase))
                        result.Brand = null;
                }
            }
        }
        private static bool IsFollowUpCompareQuestion(string text)
        {
            return ContainsAny(text,
                "xe nao re hon",
                "gia xe nao re hon",
                "mau nao re hon",
                "con nao re hon",
                "cai nao re hon",
                "xe nao dat hon",
                "gia xe nao dat hon",
                "xe nao tot hon",
                "xe nao on hon",
                "xe nao di xa tot hon",
                "xe nao di tot hon",
                "xe nao di xe tot hon",
                "xe nao dang mua hon");
        }
        private static void ParseTarget(string text, ParsedIntent result)
        {
            var targets = new List<string>();

            if (ContainsAny(text, "sinh vien", "hoc sinh"))
                targets.Add("sinh viên");

            if (HasExplicitFemaleSignal(text))
                targets.Add("nữ");

            if (HasExplicitMaleSignal(text))
                targets.Add("nam");

            if (targets.Any())
                result.Target = string.Join(" ", targets.Distinct());

            result.PrefersFemaleStyle = targets.Contains("nữ");
            result.PrefersMaleStyle = targets.Contains("nam");
        }

        private static void ParseUseCases(string text, ParsedIntent result)
        {
            result.ForSchool = ContainsAny(text, "di hoc", "hoc hang ngay", "den truong");
            result.ForWork = ContainsAny(text,
                "di lam",
                "di cong so",
                "cong so",
                "di lam hang ngay",
                "chay grab",
                "chay dich vu",
                "dich vu",
                "di nhieu",
                "chay hang ngay");
            result.ForCity = ContainsAny(text, "di pho", "noi thanh", "do thi", "trong pho");
            result.ForTour = ContainsAny(text, "di tour", "duong dai", "di xa", "phuot");
        }

        private static void ParsePreferenceFeatures(string text, ParsedIntent result)
        {
            result.WantsEasyControl = ContainsAny(text,
                "de di",
                "de dieu khien",
                "de chong chan",
                "nhe",
                "gon",
                "linh hoat");

            result.WantsFuelSaving = ContainsAny(text,
                "tiet kiem xang",
                "it ton xang",
                "hao xang thap",
                "ben xang");

            result.WantsLargeStorage = ContainsAny(text,
                "cop rong",
                "de do",
                "chua do");

            if (ContainsAny(text, "ben", "it hong", "de bao duong", "de sua", "thuc dung"))
            {
                result.ForWork = true;
            }
        }

        private static void ParseHeightAndSeat(string text, ParsedIntent result)
        {
            result.HeightCm = ExtractHeightCm(text);

            if (result.HeightCm.HasValue && result.HeightCm.Value <= 150)
            {
                result.NeedsLowSeat = true;
                result.WantsEasyControl = true;
            }

            if (ContainsAny(text, "nguoi thap", "nho con"))
            {
                result.NeedsLowSeat = true;
                result.WantsEasyControl = true;
            }

            if (ContainsAny(text, "de chong chan", "yen thap"))
            {
                result.NeedsLowSeat = true;
                result.WantsEasyControl = true;
            }
        }

        private static void ParseStyles(string text, ParsedIntent result)
        {
            if (ContainsAny(text, "the thao", "nang dong"))
                result.RequestedStyles.Add("sporty");

            if (ContainsAny(text, "thanh lich", "nhe nhang", "sang", "dep", "dep hon"))
                result.RequestedStyles.Add("elegant");

            if (ContainsAny(text, "ca tinh", "manh me", "ham ho"))
                result.RequestedStyles.Add("aggressive");

            if (ContainsAny(text, "nho gon", "gon", "linh hoat"))
                result.RequestedStyles.Add("compact");
        }

        private async Task ParseMentionedProductsAsync(string text, ParsedIntent result)
        {
            var matches = new List<(int Index, string DisplayName)>();

            foreach (var product in KnownProducts)
            {
                var matchIndex = IndexOfWholePhrase(text, product);
                if (matchIndex >= 0 && ProductAliasMap.TryGetValue(product, out var displayName))
                {
                    matches.Add((matchIndex, displayName));
                }
            }

            if (matches.Count < 2)
            {
                foreach (var inferred in ExtractProductsFromCompareFragments(text))
                {
                    var idx = text.IndexOf(Normalize(inferred), StringComparison.OrdinalIgnoreCase);
                    matches.Add((idx < 0 ? int.MaxValue : idx, inferred));
                }
            }

            result.MentionedProducts = matches
                .OrderBy(x => x.Index)
                .Select(x => x.DisplayName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolvedFromDb = await _productNameResolver.ResolveMentionedProductNamesAsync(text);

            foreach (var productName in resolvedFromDb)
            {
                if (!result.MentionedProducts.Contains(productName, StringComparer.OrdinalIgnoreCase))
                {
                    result.MentionedProducts.Add(productName);
                }
            }
        }
        private static void ParseExcludedProducts(string text, ParsedIntent result)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (result.MentionedProducts == null || result.MentionedProducts.Count == 0)
                return;

            bool hasNegativeSignal = HasNegativePreferenceSignal(text);

            if (!hasNegativeSignal)
                return;

            foreach (var product in result.MentionedProducts)
            {
                if (!string.IsNullOrWhiteSpace(product))
                {
                    result.ExcludedProducts.Add(product);
                }
            }

            result.IsDirectProductLookup = false;
            result.LookupTargetType = null;
            result.LookupField = null;

            result.IntentType = "refine";
            result.IsFollowUp = true;
            result.FollowUpType = "exclude_product";
            result.HasDeterministicProductIntent = true;
        }
        private static int IndexOfWholePhrase(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
                return -1;

            var pattern = $@"(?<!\w){Regex.Escape(phrase)}(?!\w)";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Index : -1;
        }

        private static List<string> ExtractProductsFromCompareFragments(string text)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return results;

            var normalized = text.Trim();
            var connectorPattern = @"\b(?:so voi|voi|va|hay)\b";
            var parts = Regex.Split(normalized, connectorPattern, RegexOptions.IgnoreCase)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            foreach (var part in parts)
            {
                foreach (var alias in KnownProducts)
                {
                    if (IndexOfWholePhrase(part, alias) >= 0 && ProductAliasMap.TryGetValue(alias, out var displayName))
                    {
                        results.Add(displayName);
                    }
                }
            }

            return results
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        private static bool LooksLikeContextDependentFollowUp(string text, ParsedIntent result)
        {
            if (string.IsNullOrWhiteSpace(text) || result == null)
                return false;

            bool hasReferenceSignal = ContainsAny(text,
                "thi sao",
                "the con",
                "vay con",
                "con nay",
                "con do",
                "mau nay",
                "mau do",
                "xe nay",
                "xe do",
                "trong nhom nay",
                "trong may mau nay",
                "mau nao",
                "xe nao");

            bool startsLikeFollowUp =
                text.StartsWith("con ") ||
                text.StartsWith("neu ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("chi lay ") ||
                text.StartsWith("bo ") ||
                text.StartsWith("loai ") ||
                text.StartsWith("doi sang ") ||
                text.StartsWith("giam xuong ") ||
                text.StartsWith("len ");

            return hasReferenceSignal || startsLikeFollowUp || result.IsFollowUp;
        }
        private static void ParseComparisonFeature(string text, ParsedIntent result)
        {
            if (ContainsAny(text, "cop rong", "de do", "chua do"))
            {
                result.ComparisonFeature = "storage";
                return;
            }

            if (ContainsAny(text, "tiet kiem xang", "it ton xang"))
            {
                result.ComparisonFeature = "fuel_saving";
                return;
            }

            if (ContainsAny(text, "de chong chan", "yen thap", "nguoi thap", "nho con"))
            {
                result.ComparisonFeature = "low_seat";
                return;
            }

            if (HasExplicitFemaleSignal(text))
            {
                result.ComparisonFeature = "female_fit";
                return;
            }

            if (ContainsAny(text, "di em", "em hon", "vanh em", "ngoi em"))
            {
                result.ComparisonFeature = "ride_comfort";
                return;
            }

            if (ContainsAny(text, "thuc dung", "on dinh", "de dung hang ngay", "di lam", "cong so"))
            {
                result.ComparisonFeature = "work_fit";
                return;
            }

            if (ContainsAny(text, "di hoc", "sinh vien"))
            {
                result.ComparisonFeature = "school_fit";
            }
        }

        private static bool LooksLikeBudgetFragment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text,
                       @"\b(tam|khoang|quanh)\s*\d+([.,]\d+)?\s*(trieu|tr|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(text,
                       @"^(duoi|tren|toi da|khong qua|it nhat|tro len)\s*\d+([.,]\d+)?\s*(trieu|tr|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(text,
                       @"^\d+([.,]\d+)?\s*(trieu|tr|cu|chai)\b",
                       RegexOptions.IgnoreCase);
        }

        private static bool HasExplicitFemaleSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"(^|\s)(nu)(\s|$)", RegexOptions.IgnoreCase)
                || text.Contains("cho nu")
                || text.Contains("xe nu")
                || text.Contains("hop nu")
                || text.Contains("nu tinh")
                || text.Contains("phu nu");
        }

        private static bool HasExplicitMaleSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"(^|\s)(nam)(\s|$)", RegexOptions.IgnoreCase)
                || text.Contains("cho nam")
                || text.Contains("xe nam")
                || text.Contains("hop nam")
                || text.Contains("nam tinh");
        }

        private static bool HasPositiveCategorySignal(string text, string category)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return category switch
            {
                "xe ga" => ContainsAny(text,
                    "thich xe ga",
                    "muon xe ga",
                    "uu tien xe ga",
                    "xe ga di",
                    "chon xe ga",
                    "lay xe ga"),
                "xe số" => ContainsAny(text,
                    "thich xe so",
                    "muon xe so",
                    "uu tien xe so",
                    "xe so di",
                    "chon xe so",
                    "lay xe so"),
                "côn tay" => ContainsAny(text,
                    "thich con tay",
                    "muon con tay",
                    "uu tien con tay",
                    "xe con di",
                    "chon con tay",
                    "lay con tay"),
                _ => false
            };
        }

        private static int? ExtractHeightCm(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var patterns = new[]
            {
                @"cao\s*(\d{3})\s*cm",
                @"(\d{3})\s*cm",
                @"1m(\d{2})",
                @"m(\d{2})"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                if (pattern == @"1m(\d{2})" || pattern == @"m(\d{2})")
                {
                    if (int.TryParse(match.Groups[1].Value, out var sub))
                        return 100 + sub;
                }
                else
                {
                    if (int.TryParse(match.Groups[1].Value, out var cm) && cm >= 120 && cm <= 220)
                        return cm;
                }
            }

            return null;
        }
        private static bool LooksLikeExplicitProductListRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasListVerb = ContainsAny(text,
                "dua ra",
                "liệt kê",
                "liet ke",
                "ke ra",
                "cho xem",
                "xem danh sach",
                "danh sach",
                "shop co",
                "cua hang co",
                "co nhung xe nao",
                "co xe nao",
                "nhung xe nao",
                "tat ca xe",
                "toan bo xe");

            bool hasProductSignal = ContainsAny(text,
                "xe",
                "xe may",
                "mau",
                "san pham",
                "shop",
                "cua hang");

            bool hasFilterSignal =
                ContainsAny(text, "tu", "den", "khoang", "tam", "duoi", "tren", "trieu", "xe ga", "xe so", "con tay") ||
                Regex.IsMatch(text, @"\d+", RegexOptions.IgnoreCase);

            return hasListVerb && (hasProductSignal || hasFilterSignal);
        }
        private static bool LooksLikeProductSearch(string text, ParsedIntent result)
        {
            if (result.IsDirectProductLookup || result.IsOrderLookup || result.IsGreeting || result.IsOutOfScope)
                return false;
            if (LooksLikeExplicitProductListRequest(text))
                return true;
            bool hasHardSearchConstraint =
                !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category)
                || result.PriceMin.HasValue
                || result.PriceMax.HasValue
                || result.TargetPrice.HasValue;

            bool hasDirectLookupPhrase = !string.IsNullOrWhiteSpace(result.LookupField);
            if (hasDirectLookupPhrase)
                return false;

            bool hasHumanNeed =
                !string.IsNullOrWhiteSpace(result.Target)
                || result.ForSchool
                || result.ForWork
                || result.ForCity
                || result.ForTour
                || result.WantsEasyControl
                || result.WantsFuelSaving
                || result.WantsLargeStorage
                || result.NeedsLowSeat
                || result.RequestedStyles.Count > 0;

            bool hasRecommendationCue =
                ContainsAny(text,
                    "tu van",
                    "goi y",
                    "phu hop",
                    "nen mua",
                    "xe nao",
                    "cho nu",
                    "cho nam",
                    "sinh vien",
                    "di hoc",
                    "di lam",
                    "tiet kiem xang",
                    "cop rong",
                    "de chong chan");
            if (hasHardSearchConstraint && (hasHumanNeed || hasRecommendationCue))
                return false;

            return hasHardSearchConstraint;
        }

        private static bool LooksLikeOpenRecommendation(string text, ParsedIntent result)
        {
            if (result.IsDirectProductLookup || result.IsOrderLookup || result.IsGreeting || result.IsOutOfScope)
                return false;

            bool hasHardSearchConstraint =
                result.TargetPrice.HasValue
                || result.PriceMin.HasValue
                || result.PriceMax.HasValue
                || !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category);

            bool hasHumanNeed =
                !string.IsNullOrWhiteSpace(result.Target)
                || result.ForSchool
                || result.ForWork
                || result.ForCity
                || result.ForTour
                || result.WantsEasyControl
                || result.WantsFuelSaving
                || result.WantsLargeStorage
                || result.NeedsLowSeat
                || result.RequestedStyles.Count > 0;

            bool hasRecommendationCue =
                ContainsAny(text,
                    "tu van",
                    "goi y",
                    "phu hop",
                    "nen mua",
                    "xe nao",
                    "cho nu",
                    "cho nam",
                    "sinh vien",
                    "di hoc",
                    "di lam",
                    "tiet kiem xang",
                    "cop rong",
                    "de chong chan");

            bool isHardFilterOnly = hasHardSearchConstraint && !hasHumanNeed && !hasRecommendationCue;
            if (isHardFilterOnly)
                return false;

            return (hasHumanNeed && hasHardSearchConstraint) || hasRecommendationCue || hasHumanNeed;
        }
        private static bool LooksLikeChangeProductIntent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ContainsAny(text,
                "doi mau khac",
                "doi con khac",
                "doi xe khac",
                "mau khac",
                "con khac",
                "xe khac",
                "khac xem",
                "khac di",
                "mau nao khac",
                "con nao khac",
                "xe nao khac",
                "goi y mau khac",
                "goi y con khac",
                "con lua chon nao khac",
                "lua chon khac");
        }
        private static bool IsExplicitCompareIntent(
      string text,
      int mentionedProductCount,
      string? comparisonFeature)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (ContainsAny(text,
                "khong thich",
                "khong muon",
                "khong lay",
                "khong chon",
                "ghet",
                "ne ",
                "bo ",
                "loai"))
            {
                return false;
            }

            bool hasExplicitCompareVerb = ContainsAny(text,
                "so sanh",
                "so voi",
                "khac nhau",
                "uu nhuoc diem",
                "ưu nhược điểm");

            if (hasExplicitCompareVerb)
                return mentionedProductCount >= 2;

            if (mentionedProductCount < 2)
                return false;

            bool hasComparePhrase =
                Regex.IsMatch(text, @"\b(nao hon|tot hon|re hon|dat hon|rong hon|thap hon|nhe hon|em hon|hop hon|gon hon)\b",
                    RegexOptions.IgnoreCase);

            return !string.IsNullOrWhiteSpace(comparisonFeature) && hasComparePhrase;
        }
        private static bool IsRefineIntent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ContainsAny(text,
                "khong thich",
                "khong muon",
                "dung",
                "ne",
                "ghet",
                "uu tien",
                "bot",
                "them dieu kien")
                || text.StartsWith("uu tien ")
                || text.StartsWith("né ")
                || text.StartsWith("ne ")
                || text.StartsWith("khong thich ")
                || text.StartsWith("khong muon ");
        }

        private static bool IsSwitchBrandIntent(string text, string? detectedBrand)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(detectedBrand))
                return false;

            var normalizedBrand = NormalizeBrandForText(detectedBrand);

            if (string.Equals(text.Trim(), normalizedBrand, StringComparison.OrdinalIgnoreCase))
                return true;

            if (Regex.IsMatch(text, $@"\b(co|có|con|còn)\s+{Regex.Escape(normalizedBrand)}\s*(khong|không)?\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(text, $@"\b{Regex.Escape(normalizedBrand)}\s+(co|có|con|còn)\s*(khong|không)?\b", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(text, $@"\b(doi sang|đổi sang|chuyen sang|chuyển sang|sang)\s+{Regex.Escape(normalizedBrand)}\b", RegexOptions.IgnoreCase))
                return true;
            
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(normalizedBrand)}\s+(thi sao|thì sao|duoc khong|được không|on khong|ổn không)\b", RegexOptions.IgnoreCase))
                return true;

            return false;
        }

        private static bool IsRecommendationFollowUpIntent(string text, ParsedIntent result)
        {
            if (result.MentionedProducts.Count >= 2)
                return false;

            bool hasComparativeTone = ContainsAny(text,
                "hon",
                "nao hon",
                "tot hon",
                "hop hon",
                "rong hon",
                "thap hon",
                "em hon",
                "gon hon",
                "nhe hon",
                "re hon",
                "dat hon",
                "dep hon",
                "thi sao",
                "neu",
                "uu tien",
                "chi lay",
                "bo ",
                "loai khac",
                "xe khac",
                "mau khac",
                "tang budget",
                "them ngan sach");

            bool hasFeature =
                !string.IsNullOrWhiteSpace(result.ComparisonFeature)
                || result.WantsLargeStorage
                || result.WantsFuelSaving
                || result.NeedsLowSeat
                || result.WantsEasyControl
                || result.ForWork
                || result.ForSchool
                || result.RequestedStyles.Any()
                || !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category)
                || result.PriceMin.HasValue
                || result.PriceMax.HasValue
                || result.TargetPrice.HasValue;

            bool looksLikeShortFollowUp =
                text.StartsWith("neu ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("chi lay ") ||
                text.StartsWith("bo ") ||
                text.StartsWith("con ") ||
                text.StartsWith("the ") ||
                text.StartsWith("vay ") ||
                text.StartsWith("doi sang ") ||
                text.StartsWith("giam xuong ") ||
                text.StartsWith("len ");

            bool budgetOnlyFragment =
                LooksLikeBudgetFragment(text) &&
                (looksLikeShortFollowUp || text.StartsWith("quanh ") || text.StartsWith("tam ") || text.StartsWith("khoang "));

            return (hasComparativeTone && hasFeature) || budgetOnlyFragment;
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
        }

        private static string Normalize(string input)
        {
            var text = input.Trim().ToLowerInvariant();
            text = RemoveVietnameseSigns(text);
            text = Regex.Replace(text, @"\s+", " ");
            return text;
        }

        private static bool HasWholeWord(string text, string word)
        {
            return Regex.IsMatch(
                text,
                $@"(?<!\p{{L}}|\p{{N}}){Regex.Escape(word)}(?!\p{{L}}|\p{{N}})",
                RegexOptions.IgnoreCase);
        }

        private static bool HasWholePhrase(string text, string phrase)
        {
            return Regex.IsMatch(
                text,
                $@"(?<!\p{{L}}|\p{{N}}){Regex.Escape(phrase)}(?!\p{{L}}|\p{{N}})",
                RegexOptions.IgnoreCase);
        }

        private static bool MatchesWholeText(string text, params string[] options)
        {
            var trimmed = (text ?? string.Empty).Trim();
            return options.Any(x => string.Equals(trimmed, x, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeBrandForText(string brand)
        {
            return Normalize(brand).Trim();
        }

        private static string RemoveVietnameseSigns(string text)
        {
            var map = new Dictionary<char, char>
            {
                ['à'] = 'a',
                ['á'] = 'a',
                ['ạ'] = 'a',
                ['ả'] = 'a',
                ['ã'] = 'a',
                ['â'] = 'a',
                ['ầ'] = 'a',
                ['ấ'] = 'a',
                ['ậ'] = 'a',
                ['ẩ'] = 'a',
                ['ẫ'] = 'a',
                ['ă'] = 'a',
                ['ằ'] = 'a',
                ['ắ'] = 'a',
                ['ặ'] = 'a',
                ['ẳ'] = 'a',
                ['ẵ'] = 'a',
                ['è'] = 'e',
                ['é'] = 'e',
                ['ẹ'] = 'e',
                ['ẻ'] = 'e',
                ['ẽ'] = 'e',
                ['ê'] = 'e',
                ['ề'] = 'e',
                ['ế'] = 'e',
                ['ệ'] = 'e',
                ['ể'] = 'e',
                ['ễ'] = 'e',
                ['ì'] = 'i',
                ['í'] = 'i',
                ['ị'] = 'i',
                ['ỉ'] = 'i',
                ['ĩ'] = 'i',
                ['ò'] = 'o',
                ['ó'] = 'o',
                ['ọ'] = 'o',
                ['ỏ'] = 'o',
                ['õ'] = 'o',
                ['ô'] = 'o',
                ['ồ'] = 'o',
                ['ố'] = 'o',
                ['ộ'] = 'o',
                ['ổ'] = 'o',
                ['ỗ'] = 'o',
                ['ơ'] = 'o',
                ['ờ'] = 'o',
                ['ớ'] = 'o',
                ['ợ'] = 'o',
                ['ở'] = 'o',
                ['ỡ'] = 'o',
                ['ù'] = 'u',
                ['ú'] = 'u',
                ['ụ'] = 'u',
                ['ủ'] = 'u',
                ['ũ'] = 'u',
                ['ư'] = 'u',
                ['ừ'] = 'u',
                ['ứ'] = 'u',
                ['ự'] = 'u',
                ['ử'] = 'u',
                ['ữ'] = 'u',
                ['ỳ'] = 'y',
                ['ý'] = 'y',
                ['ỵ'] = 'y',
                ['ỷ'] = 'y',
                ['ỹ'] = 'y',
                ['đ'] = 'd'
            };

            var chars = text.Select(c => map.ContainsKey(c) ? map[c] : c).ToArray();
            return new string(chars);
        }
        
        private static void ParseAck(string text, ParsedIntent result)
        {
            if (text is "ok" or "oke" or "oki" or "ừ" or "uhm" or "dạ" or "da" or "rồi" or "roi")
            {
                result.IsAck = true;
                result.IntentType = "unknown";
            }

        }
        private static bool HasNegativePreferenceSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (IsQuestionNegativeParticle(text))
                return false;

            return HasTrueNegativePreferenceSignal(text);
        }
        private static bool IsQuestionNegativeParticle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text,
                @"\b(co|con|con hang|ton kho|het hang|ban|shop co|cua hang co)\b.*\b(khong|ko|k)\b",
                RegexOptions.IgnoreCase)
                ||
                Regex.IsMatch(text,
                @"\b(khong|ko|k)\b\s*$",
                RegexOptions.IgnoreCase);
        }

        private static bool HasTrueNegativePreferenceSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text,
                @"\b(khong thich|khong muon|khong lay|khong chon|khong can|ko thich|ko muon|k thich|k muon|ghet|ne|bo|loai|loai ra|bo qua|tru|ngoai tru|mien khong|mien la khong|khong phai|khong la)\b",
                RegexOptions.IgnoreCase);
        }
        private static bool MentionedAfterNegativeSignal(string text, string keyword)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
                return false;

            var normalizedKeyword = Normalize(keyword);

            var negativeWords =
     @"khong thich|khong muon|khong lay|khong chon|khong can|ko thich|ko muon|k thich|k muon|ghet|ne|bo|loai|loai ra|bo qua|tru|ngoai tru|mien khong|mien la khong|khong phai|khong la";

            return Regex.IsMatch(text,
                $@"\b({negativeWords})\b\s+\b{Regex.Escape(normalizedKeyword)}\b",
                RegexOptions.IgnoreCase)
                ||
                Regex.IsMatch(text,
                $@"\b{Regex.Escape(normalizedKeyword)}\b\s+\b({negativeWords})\b",
                RegexOptions.IgnoreCase);
        }
        private static void ParseNoise(string text, ParsedIntent result)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                result.IsNoise = true;
                result.IntentType = "unknown";
                return;
            }

            if (Regex.IsMatch(text, @"^[.,?!_/@#$%^&*()+=-]+$"))
            {
                result.IsNoise = true;
                result.IntentType = "unknown";
                return;
            }

            if (Regex.IsMatch(text, @"^0\d{8,9}$"))
            {
                return;
            }

            if (Regex.IsMatch(text, @"^[a-z0-9]{4,}$"))
            {
                bool knownUsefulToken =
                    text.Contains("vision") ||
                    text.Contains("winner") ||
                    text.Contains("blade") ||
                    text.Contains("honda") ||
                    text.Contains("yamaha") ||
                    text.Contains("gia") ||
                    text.Contains("xe") ||
                    text.Contains("don") ||
                    text.Contains("air") ||
                    text.Contains("latte") ||
                    text.Contains("future") ||
                    text.Contains("wave");

                if (!knownUsefulToken)
                {
                    result.IsNoise = true;
                    result.IntentType = "unknown";
                }
            }
        }
        private static void ApplyProductMentionSafety(string text, ParsedIntent result)
        {
            if (result == null || result.MentionedProducts == null)
                return;

            bool hasRecommendationCue = ContainsAny(text,
                "tu van",
                "goi y",
                "nen mua",
                "phu hop",
                "xe nao",
                "chon xe",
                "mua xe");

            bool hasCompareCue = ContainsAny(text,
                "so sanh",
                "so voi",
                "khac nhau",
                "uu nhuoc",
                "cai nao hon",
                "xe nao hon",
                "mau nao hon",
                "tot hon",
                "hop hon");

            bool hasLookupCue = ContainsAny(text,
     "gia",
     "bao nhieu",
     "con hang",
     "con khong",
     "co khong",
     "ton kho",
     "chi tiet",
     "thong tin",
     "bao nhieu cc",
     "phan khoi",
     "dung tich");

            if (hasRecommendationCue || hasCompareCue)
            {
                result.IsDirectProductLookup = false;
                result.LookupField = null;
                result.LookupTargetType = null;
                return;
            }

            if (!hasLookupCue && result.MentionedProducts.Count > 1)
            {
                result.IsDirectProductLookup = false;
                result.LookupField = null;
                result.LookupTargetType = null;
            }
        }
        private static bool LooksLikeRestartRecommendationRequest(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ContainsAny(text,
                "tu van lai",
                "tư vấn lại",
                "goi y lai",
                "gợi ý lại",
                "chon lai",
                "chọn lại",
                "loc lai",
                "lọc lại",
                "tim lai",
                "tìm lại");
        }
        private static bool LooksLikeCompareWithBrandOnly(string text, ParsedIntent result)
        {
            if (string.IsNullOrWhiteSpace(text) || result == null)
                return false;

            bool hasCompareSignal =
                text.Contains("so sanh") ||
                text.Contains("so voi") ||
                text.Contains("voi") ||
                text.Contains("khac nhau");

            bool hasOneProduct =
                result.MentionedProducts != null &&
                result.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == 1;

            bool hasBrand =
                !string.IsNullOrWhiteSpace(result.Brand);

            return hasCompareSignal && hasOneProduct && hasBrand;
        }
    }
}
