using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class IntentParserService : IIntentParserService
    {
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
            ["attila"] = "SYM Attila Venus",
            ["attila venus"] = "SYM Attila Venus",
            ["sym attila"] = "SYM Attila Venus",
            ["sym attila venus"] = "SYM Attila Venus"
        };

        private static readonly string[] KnownProducts = ProductAliasMap.Keys
            .OrderByDescending(x => x.Length)
            .ToArray();

        private static readonly string[] KnownBrands = { "Honda", "Yamaha", "Suzuki", "SYM", "Piaggio" };

        public Task<ParsedIntent> ParseAsync(string message)
        {
            var result = new ParsedIntent
            {
                RawMessage = message ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
                return Task.FromResult(result);

            var text = Normalize(message);
            Console.WriteLine("INTENT PARSER VERSION = step-noise-ack-clean");
            Console.WriteLine("NORMALIZED TEXT = " + text);
            ParseExcludedCategory(text, result);
            ParseExcludedBrand(text, result);

            ParseCategory(text, result);
            ParseBrand(text, result);

            ParseTarget(text, result);
            ParseUseCases(text, result);
            ParsePreferenceFeatures(text, result);
            ParseHeightAndSeat(text, result);
            ParseStyles(text, result);
            ParseMentionedProducts(text, result);
            ParseComparisonFeature(text, result);

            ResolveBrandAndCategoryConflicts(result);
            ParseRecommendationContextSignals(text, result);

            ParseGreeting(text, result);
            ParseAck(text, result);
            ParseOutOfScope(text, result);
            ParseNoise(text, result);

            if (result.IsOutOfScope || result.IsNoise || result.IsAck)
                return Task.FromResult(result);

            ParseOrderLookup(text, result);
            ParseLookupSignals(text, result);
            ParsePolicyServiceSignals(text, result);
            ParseIntentType(text, result);
            ParseFollowUp(text, result);
            ParseRouteFlow(text, result);

            return Task.FromResult(result);
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
                result.ExcludedCategories.Any();

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
            Console.WriteLine("PARSE OUT OF SCOPE TEXT = " + text);
            if (text.Contains("mau khac") ||
    text.Contains("xe khac") ||
    text.Contains("khac di") ||
    text.Contains("doi mau") ||
    text.Contains("goi y khac"))
            {
                return;
            }
            bool hasMotorbikeSignal =
                result.MentionedProducts.Any()
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
            Console.WriteLine("OUT OF SCOPE MATCHED");

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
            bool hasMentionedProduct = result.MentionedProducts.Count >= 1;

            bool asksCc = Regex.IsMatch(text, @"\bcc\b", RegexOptions.IgnoreCase)
               || ContainsAny(text, "bao nhieu cc", "bao nhieu phan khoi", "dung tich", "phan khoi");

            bool asksStock = ContainsAny(text,
                "con hang",
                "ton kho",
                "con khong",
                "het hang",
                "co san",
                "con may chiec",
                "may chiec",
                "bao nhieu chiec",
                "con bao nhieu",
                "so luong",
                "ton bao nhieu");

            bool asksPrice = ContainsAny(text, "gia", "may tien")
                             || (text.Contains("bao nhieu") && !asksCc);

            bool asksDetail = ContainsAny(text,
                "chi tiet",
                "thong tin",
                "mo ta",
                "co gi",
                "xem chi tiet");

            if (asksCc)
                result.LookupField = "cc";
            else if (asksStock)
                result.LookupField = "stock";
            else if (asksPrice)
                result.LookupField = "price";
            else if (asksDetail)
                result.LookupField = "detail";

            if (hasMentionedProduct && !string.IsNullOrWhiteSpace(result.LookupField))
            {
                result.IsDirectProductLookup = true;
                result.LookupTargetType = "product";
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (hasMentionedProduct &&
                result.MentionedProducts.Count == 1 &&
                !ContainsAny(text, "tu van", "goi y", "phu hop", "nen mua", "xe nao", "so sanh"))
            {
                result.IsDirectProductLookup = true;
                result.LookupTargetType = "product";
                result.LookupField ??= "detail";
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

            if (string.Equals(result.IntentType, ChatFlowType.ServiceInfo, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.IntentType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsExplicitCompareIntent(text, result.MentionedProducts.Count, result.ComparisonFeature))
            {
                result.IntentType = "compare";
                result.IsDirectCompare = true;
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (IsSwitchBrandIntent(text, result.Brand))
            {
                result.IntentType = "brand_switch";
                result.IsBrandSwitch = true;
                result.IsFollowUp = true;
                result.FollowUpType = "switch_brand";
                result.HasDeterministicProductIntent = true;
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

            if (result.HasNarrowRefinementSignal)
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

            if (result.IsDirectProductLookup)
            {
                result.IntentType = "product_lookup";
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

        private static void ParsePolicyServiceSignals(string text, ParsedIntent result)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (LooksLikeProductRecommendationOrSearch(text, result))
                return;

            var slot = InferPolicySlot(text);
            var hasKnowledgeQuestion =
                !string.IsNullOrWhiteSpace(slot) ||
                ContainsAny(text,
                    "tra gop", "lai suat", "tra truoc", "vay", "ngan hang", "tin dung",
                    "bao hanh", "bao duong", "sua chua", "cuu ho",
                    "giay to", "bien so", "ca vet", "dang ky", "truoc ba",
                    "giao hang", "van chuyen", "bao hiem", "khuyen mai",
                    "dat coc", "hoan coc", "doi tra", "thu cu", "doi moi",
                    "thanh toan", "quet the", "lai thu", "test ride");

            if (!hasKnowledgeQuestion)
                return;

            result.IntentType = IsOperationalService(text)
                ? ChatFlowType.ServiceInfo
                : ChatFlowType.PolicyInfo;
            result.RouteFlow = result.IntentType;
            result.PolicySlot = slot;
            result.IsProductSearch = false;
            result.IsOpenRecommendation = false;
            result.IsFollowUp = false;
            result.HasDeterministicProductIntent = false;
        }

        private static string? InferPolicySlot(string text)
        {
            if (ContainsAny(text, "ho so", "giay to can chuan bi", "can chuan bi", "cmnd", "cccd", "ho khau", "kt3", "sao ke", "hop dong lao dong"))
                return "documents";

            if (ContainsAny(text, "dieu kien", "thu nhap", "do tuoi", "no xau", "cic", "bao lanh"))
                return "conditions";

            if (ContainsAny(text, "quy trinh", "thu tuc", "cac buoc", "lam sao", "dang ky the nao"))
                return "process";

            if (ContainsAny(text, "lai suat", "0%", "uu dai"))
                return "interest";

            if (ContainsAny(text, "ky han", "ki han", "bao lau", "thoi gian vay", "may thang"))
                return "loan_term";

            if (ContainsAny(text, "tra truoc", "down payment"))
                return "down_payment";

            if (ContainsAny(text, "hang thang", "moi thang", "tra moi thang"))
                return "monthly_payment";

            if (ContainsAny(text, "tat toan", "tra som", "phi phat"))
                return "early_settlement";

            if (ContainsAny(text, "bao hanh", "bao hanh bao lau", "may nam", "km"))
                return "warranty_period";

            if (ContainsAny(text, "khong bao hanh", "hao mon", "lop xe", "ma phanh", "bugi", "bong den", "dau nhot"))
                return "warranty_exclusion";

            if (ContainsAny(text, "goi bao duong", "bao duong co ban", "bao duong nang cao", "bao duong cao cap"))
                return "service_package";

            if (ContainsAny(text, "lich bao duong", "dinh ky", "bao duong khi nao", "500km", "3000km", "6000km"))
                return "maintenance_schedule";

            if (ContainsAny(text, "phi", "bao nhieu tien", "gia dich vu", "phi giao", "phi bien so"))
                return "fee";

            if (ContainsAny(text, "bam bien", "ca vet", "bao lau co bien", "bao lau co ca vet"))
                return "registration_time";

            if (ContainsAny(text, "dat coc", "giu xe"))
                return "deposit";

            if (ContainsAny(text, "hoan", "hoan coc", "doi tra", "tra xe"))
                return "refund";

            return null;
        }

        private static bool IsOperationalService(string text)
        {
            return ContainsAny(text,
                "bao duong", "sua chua", "cuu ho", "giao hang", "van chuyen",
                "lai thu", "test ride", "bao hiem", "thanh toan");
        }

        private static bool LooksLikeProductRecommendationOrSearch(string text, ParsedIntent result)
        {
            bool hasRecommendationCue = ContainsAny(text,
                "tu van xe", "goi y xe", "nen mua xe", "tim xe", "mau xe nao", "xe nao");

            bool hasProductSearchCue =
                result.IsProductSearch ||
                result.IsOpenRecommendation ||
                result.PriceMin.HasValue ||
                result.PriceMax.HasValue ||
                result.TargetPrice.HasValue;

            return hasRecommendationCue || hasProductSearchCue;
        }

        private static void ParseFollowUp(string text, ParsedIntent result)
        {
            if (result.IntentType == "compare")
            {
                result.IsFollowUp = result.MentionedProducts.Count < 2;
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

            if (string.Equals(result.IntentType, ChatFlowType.ServiceInfo, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.IntentType, ChatFlowType.PolicyInfo, StringComparison.OrdinalIgnoreCase))
            {
                result.RouteFlow = result.IntentType;
                return;
            }

            if (result.IsDirectCompare)
            {
                result.RouteFlow = ChatFlowType.Compare;
                return;
            }

            if (result.IsDirectProductLookup)
            {
                result.RouteFlow = ChatFlowType.ProductLookup;
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
            if (ContainsAny(text,
                "khong thich honda",
                "khong muon honda",
                "ne honda",
                "ghet honda",
                "dung honda",
                "bo honda",
                "bo honda di",
                "khong lay honda",
                "khong lay honda nua",
                "loai honda",
                "loai honda ra"))
            {
                result.ExcludedBrands.Add("Honda");
            }

            if (ContainsAny(text, "khong thich yamaha", "khong muon yamaha", "ne yamaha", "ghet yamaha", "dung yamaha", "bo yamaha", "loai yamaha"))
                result.ExcludedBrands.Add("Yamaha");

            if (ContainsAny(text, "khong thich suzuki", "khong muon suzuki", "ne suzuki", "ghet suzuki", "dung suzuki", "bo suzuki", "loai suzuki"))
                result.ExcludedBrands.Add("Suzuki");

            if (ContainsAny(text, "khong thich sym", "khong muon sym", "ne sym", "ghet sym", "dung sym", "bo sym", "loai sym"))
                result.ExcludedBrands.Add("SYM");

            if (ContainsAny(text, "khong thich piaggio", "khong muon piaggio", "ne piaggio", "ghet piaggio", "dung piaggio", "bo piaggio", "loai piaggio"))
                result.ExcludedBrands.Add("Piaggio");
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

        private static void ParseMentionedProducts(string text, ParsedIntent result)
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

        private static bool LooksLikeProductSearch(string text, ParsedIntent result)
        {
            if (result.IsDirectProductLookup || result.IsOrderLookup || result.IsGreeting || result.IsOutOfScope)
                return false;

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

        private static bool IsExplicitCompareIntent(string text, int mentionedProductCount, string? comparisonFeature)
        {
            if (ContainsAny(text, "so sanh", "so voi", "khac nhau"))
                return true;

            if (mentionedProductCount >= 2 &&
                (!string.IsNullOrWhiteSpace(comparisonFeature)
                 || ContainsAny(text, "hon", "nao hon", "tot hon", "re hon", "dat hon", "rong hon", "thap hon")))
            {
                return true;
            }

            return false;
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
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (ContainsAny(text,
                "doi sang honda",
                "doi sang yamaha",
                "doi sang suzuki",
                "doi sang sym",
                "doi sang piaggio"))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(detectedBrand))
                return false;

            var normalizedBrand = NormalizeBrandForText(detectedBrand);
            return Regex.IsMatch(text, $@"\b(con|còn|doi sang|đổi sang)\s+{Regex.Escape(normalizedBrand)}\b")
                   || Regex.IsMatch(text, $@"\b{Regex.Escape(normalizedBrand)}\s+(thi sao|thì sao)\b");
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
    }
}
