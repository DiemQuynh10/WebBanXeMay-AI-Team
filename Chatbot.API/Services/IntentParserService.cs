using System.Text.RegularExpressions;
using System.Linq;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class IntentParserService : IIntentParserService
    {
        private static readonly string[] KnownProducts =
        {
            "vision",
            "air blade",
            "freego",
            "latte",
            "grande",
            "zip",
            "future",
            "wave",
            "sirius",
            "address",
            "impulse",
            "janus",
            "lead",
            "vario",
            "winner",
            "exciter",
            "pcx",
            "sh",
            "shark",
            "shark mini",
            "attila",
            "attila venus"
        };

        private static readonly (string DisplayName, string[] Aliases)[] KnownBrands =
        {
            ("Honda", new[] { "honda" }),
            ("Yamaha", new[] { "yamaha" }),
            ("Suzuki", new[] { "suzuki" }),
            ("SYM", new[] { "sym" }),
            ("Piaggio", new[] { "piaggio" })
        };

        public Task<ParsedIntent> ParseAsync(string message)
        {
            var result = new ParsedIntent
            {
                RawMessage = message ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
                return Task.FromResult(result);

            var text = Normalize(message);

            // 1. Parse các thuộc tính nền
            ParseExcludedCategory(text, result);
            ParseExcludedBrand(text, result);
            ParseExcludedProducts(text, result);

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


            // 2. Parse flow-level signals
            ParseGreeting(text, result);
            ParseOutOfScope(text, result);
            ParseOrderLookup(text, result);
            ParseLookupSignals(text, result);
            ParseIntentType(text, result);
            ParseFollowUp(text, result);
            ParseRouteFlow(text, result);

            return Task.FromResult(result);
        }

        private static void ParseGreeting(string text, ParsedIntent result)
        {
            if (MatchesWholeText(text, "hello", "hi", "alo", "chao", "xin chao", "ad oi", "shop oi"))
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
                text.StartsWith("dung goi y ") ||
                text.StartsWith("ngoai tru ") ||
                text.StartsWith("tru ");

            bool looksExpandFollowUp =
                text.StartsWith("neu ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("chi ") ||
                text.StartsWith("bo ") ||
                text.StartsWith("khong thich ") ||
                text.StartsWith("khong muon ") ||
                text.StartsWith("dung goi y ") ||
                text.StartsWith("ngoai tru ") ||
                text.StartsWith("tru ") ||
                text.StartsWith("chi lay ") ||
                text.StartsWith("xe ga ") ||
                text.StartsWith("xe so ") ||
                text.StartsWith("con tay ") ||
                text.StartsWith("quanh ") ||
                text.StartsWith("tam ") ||
                text.StartsWith("khoang ");

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

            // 1. Fresh consultation:
            // câu standalone + có use case / target / budget / category / brand
            if (looksStandaloneFresh && (hasUseCaseSignal || hasBudgetSignal || hasHardConstraintSignal || hasStrongPreferenceSignal))
            {
                result.HasFreshConsultationSignal = true;
                result.RecommendationContextActionHint = "fresh";
                return;
            }

            // 2. Expand recommendation:
            // follow-up nhưng có constraint làm đổi candidate set
            if (looksExpandFollowUp && (hasBudgetSignal || hasHardConstraintSignal || hasStrongPreferenceSignal))
            {
                result.HasExpandRecommendationSignal = true;
                result.RecommendationContextActionHint = "expand";
                return;
            }

            // 3. Narrow refinement:
            // câu hỏi chọn sâu hơn trong nhóm hiện có
            if (looksNarrowFollowUp && (result.ComparisonFeature != null || hasStrongPreferenceSignal || result.MentionedProducts.Count <= 1))
            {
                result.HasNarrowRefinementSignal = true;
                result.RecommendationContextActionHint = "narrow";
                return;
            }
        }

        private static void ParseOutOfScope(string text, ParsedIntent result)
        {
            // Nếu có bất kỳ tín hiệu nào liên quan tới xe / tư vấn xe thì không được coi là out-of-scope
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
                    "tra gop",
                    "gop",
                    "lai suat",
                    "bao duong",
                    "bao hanh",
                    "dich vu",
                    "thu tuc",
                    "ho so",
                    "quy trinh",
                    "cac buoc",
                    "chinh sach",
                    "don hang",
                    "ma don",
                    "tra don",
                    "kiem tra don");

            if (hasMotorbikeSignal)
                return;

            if (ContainsAny(text,
        "thoi tiet",
        "bong da",
        "chung khoan",
        "bitcoin",
        "lap trinh",
        "code ho",
        "viet ho bai",
        "viet code",
        "viết code",
        "code c#",
        "code c sharp",
        "code java",
        "code python",
        "toan",
        "ly",
        "hoa",
        "am nhac",
        "phim",
        "game",
        "tinh yeu",
        "tu vi"))
            {
                result.IsOutOfScope = true;
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

            // hỏi 1 mẫu mà không có từ khóa quá rõ nhưng vẫn rất giống lookup
            if (hasMentionedProduct &&
                result.MentionedProducts.Count == 1 &&
                !ContainsAny(text, "tu van", "goi y", "phu hop", "nen mua", "xe nao"))
            {
                if (ContainsAny(text, "vision", "latte", "freego", "air blade", "janus", "lead", "vario", "zip", "sirius", "wave", "future"))
                {
                    result.IsDirectProductLookup = true;
                    result.LookupTargetType = "product";
                    result.LookupField ??= "detail";
                    result.HasDeterministicProductIntent = true;
                }
            }
        }

        private static void ParseIntentType(string text, ParsedIntent result)
        {
            // Ưu tiên: greeting / out-of-scope / order / compare / brand switch / refine / follow-up / lookup / search / recommend

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

            if (IsExplicitCompareIntent(text, result.MentionedProducts.Count))
            {
                result.IntentType = "compare";
                result.IsDirectCompare = true;
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (IsSwitchBrandIntent(text))
            {
                result.IntentType = "brand_switch";
                result.IsBrandSwitch = true;
                result.IsFollowUp = true;
                result.FollowUpType = "switch_brand";
                result.HasDeterministicProductIntent = true;
                return;
            }

            if (result.HasExpandRecommendationSignal && result.HasFreshConsultationSignal)
            {
                result.IntentType = "recommend";
                result.IsOpenRecommendation = true;
                result.IsFollowUp = false;
                result.FollowUpType = null;
                result.HasDeterministicProductIntent = false;
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

            if (IsRecommendationFollowUpIntent(text, result.MentionedProducts.Count))
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
            if (result.HasFreshConsultationSignal)
            {
                result.RouteFlow = ChatFlowType.Recommendation;
                return;
            }
            if (result.IsOpenRecommendation)
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

            if (result.ExcludedProducts.Any() && result.MentionedProducts.Any())
            {
                result.MentionedProducts = result.MentionedProducts
                    .Where(x => !result.ExcludedProducts.Contains(x))
                    .ToList();
            }
        }

        private static void ParseCategory(string text, ParsedIntent result)
        {
            // Ưu tiên tín hiệu khẳng định rõ trước
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
                HasNonExcludedPhrase(text, "xe ga", "tay ga", "scooter"))
            {
                result.Category = "xe ga";
                return;
            }

            if (!result.ExcludedCategories.Contains("xe số") &&
                HasNonExcludedPhrase(text, "xe so", "xe số"))
            {
                result.Category = "xe số";
                return;
            }

            if (!result.ExcludedCategories.Contains("côn tay") &&
                HasNonExcludedPhrase(text, "con tay", "côn tay", "xe con", "xe côn"))
            {
                result.Category = "côn tay";
            }
        }

        private static void ParseExcludedCategory(string text, ParsedIntent result)
        {
            bool likesXeGa = HasPositiveCategorySignal(text, "xe ga");
            bool likesXeSo = HasPositiveCategorySignal(text, "xe số");
            bool likesConTay = HasPositiveCategorySignal(text, "côn tay");

            if (!likesXeGa && IsCategoryExplicitlyExcluded(text, "xe ga"))
            {
                result.ExcludedCategories.Add("xe ga");
            }

            if (!likesXeSo && IsCategoryExplicitlyExcluded(text, "xe so"))
            {
                result.ExcludedCategories.Add("xe số");
            }

            if (!likesConTay && (IsCategoryExplicitlyExcluded(text, "con tay") || IsCategoryExplicitlyExcluded(text, "xe con")))
            {
                result.ExcludedCategories.Add("côn tay");
            }
        }
        private static void ParseBrand(string text, ParsedIntent result)
        {
            if (!result.ExcludedBrands.Contains("Honda") && HasNonExcludedWholeWord(text, "honda"))
            {
                result.Brand = "Honda";
                return;
            }

            if (!result.ExcludedBrands.Contains("Yamaha") && HasNonExcludedWholeWord(text, "yamaha"))
            {
                result.Brand = "Yamaha";
                return;
            }

            if (!result.ExcludedBrands.Contains("Suzuki") && HasNonExcludedWholeWord(text, "suzuki"))
            {
                result.Brand = "Suzuki";
                return;
            }

            if (!result.ExcludedBrands.Contains("SYM") && HasNonExcludedWholeWord(text, "sym"))
            {
                result.Brand = "SYM";
                return;
            }

            if (!result.ExcludedBrands.Contains("Piaggio") && HasNonExcludedWholeWord(text, "piaggio"))
            {
                result.Brand = "Piaggio";
            }
        }

        private static void ParseExcludedBrand(string text, ParsedIntent result)
        {
            foreach (var brand in KnownBrands)
            {
                if (brand.Aliases.Any(alias => IsBrandExplicitlyExcluded(text, alias)))
                    result.ExcludedBrands.Add(brand.DisplayName);
            }
        }

        private static void ParseExcludedProducts(string text, ParsedIntent result)
        {
            foreach (var product in KnownProducts.OrderByDescending(x => x.Length))
            {
                if (product == "sh" && !IsExplicitShMention(text))
                    continue;

                if (IsProductExplicitlyExcluded(text, product))
                {
                    result.ExcludedProducts.Add(ToDisplayProductName(product));
                }
            }
        }
        private static void ParseTarget(string text, ParsedIntent result)
        {
            var targets = new List<string>();

            if (ContainsAny(text, "sinh vien", "hoc sinh"))
                targets.Add("sinh viên");

            if (HasExplicitFemaleSignal(text) && !IsTargetExplicitlyNegated(text, "nu"))
                targets.Add("nữ");

            if (HasExplicitMaleSignal(text) && !IsTargetExplicitlyNegated(text, "nam"))
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

            // đẩy thêm các từ khóa thực dụng về hướng đi làm / dịch vụ
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
            foreach (var product in KnownProducts
                         .OrderByDescending(x => x.Length))
            {
                if (product == "sh" && !IsExplicitShMention(text))
                {
                    continue;
                }

                if (HasWholePhrase(text, product))
                {
                    result.MentionedProducts.Add(ToDisplayProductName(product));
                }
            }

            // Fallback: detect concatenated forms (no spaces) and simple fuzzy matches
            if (!result.MentionedProducts.Any())
            {
                var textNoSpaces = Regex.Replace(text, "\\s+", "");
                foreach (var product in KnownProducts.OrderByDescending(x => x.Length))
                {
                    var pNoSpaces = Regex.Replace(product, "\\s+", "");
                    // Tránh match token quá ngắn (ví dụ "sh") vào giữa từ không liên quan.
                    if (pNoSpaces.Length <= 2)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(pNoSpaces) && textNoSpaces.Contains(pNoSpaces))
                    {
                        result.MentionedProducts.Add(ToDisplayProductName(product));
                    }
                }
            }

            // Further fallback: fuzzy match single tokens to tolerate small typos
            if (!result.MentionedProducts.Any())
            {
                var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens.Where(t => t.Length >= 3))
                {
                    foreach (var product in KnownProducts)
                    {
                        var pNorm = Regex.Replace(product, "\\s+", "");
                        if (pNorm.Length <= 2)
                        {
                            continue;
                        }

                        if (LevenshteinDistance(token.ToLowerInvariant(), pNorm.ToLowerInvariant()) <= 1)
                        {
                            result.MentionedProducts.Add(ToDisplayProductName(product));
                        }
                    }
                }
            }

            result.MentionedProducts = result.MentionedProducts
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

            if (ContainsAny(text, "thuc dung", "on dinh", "de dung hang ngay"))
            {
                result.ComparisonFeature = "work_fit";
                return;
            }

            if (ContainsAny(text, "di lam", "cong so"))
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

            return Regex.IsMatch(
                       text,
                       @"\b(tam|khoang|quanh)\s*\d+([.,]\d+)?\s*(trieu|tr|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
                       @"^(duoi|tren|toi da|khong qua|it nhat|tro len)\s*\d+([.,]\d+)?\s*(trieu|tr|cu|chai)\b",
                       RegexOptions.IgnoreCase)
                   || Regex.IsMatch(
                       text,
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

            if (hasHardSearchConstraint)
                return true;

            return false;
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

            bool isHardFilterOnly =
     hasHardSearchConstraint &&
     !hasHumanNeed &&
     !hasRecommendationCue;

            if (isHardFilterOnly)
                return false;

            if (hasHumanNeed && hasHardSearchConstraint)
                return true;

            if (hasRecommendationCue)
                return true;

            if (hasHumanNeed)
                return true;

            return false;
        }
        private static bool IsExplicitCompareIntent(string text, int mentionedProductCount)
        {
            if (mentionedProductCount >= 2)
                return true;

            return ContainsAny(text,
                "so sanh",
                "so voi",
                "khac nhau",
                "con nao hon",
                "cai nao hon",
                "tot hon");
        }

        private static bool IsRefineIntent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ContainsAny(text,
                "khong thich",
                "khong muon",
                "dung",
                "dung goi y",
                "ne",
                "ghet",
                "ngoai tru",
                "tru",
                "uu tien",
                "bot",
                "them dieu kien")
                || text.StartsWith("uu tien ")
                || text.StartsWith("né ")
                || text.StartsWith("ne ")
                || text.StartsWith("khong thich ")
                || text.StartsWith("khong muon ");
        }

        private static bool IsSwitchBrandIntent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return ContainsAny(text,
                "con honda thi sao",
                "con yamaha thi sao",
                "con suzuki thi sao",
                "con sym thi sao",
                "con piaggio thi sao",
                "còn honda thì sao",
                "còn yamaha thì sao",
                "còn suzuki thì sao",
                "còn sym thì sao",
                "còn piaggio thì sao",
                "doi sang honda",
                "doi sang yamaha",
                "doi sang suzuki",
                "doi sang sym",
                "doi sang piaggio");
        }

        private static bool IsRecommendationFollowUpIntent(string text, int mentionedProductCount)
        {
            if (mentionedProductCount >= 2)
                return false;

            bool hasComparativeTone =
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
        "len 40 trieu",
        "len 40",
        "them ngan sach");

            bool hasFeature =
                ContainsAny(text,
                    "cop rong",
                    "de chong chan",
                    "tiet kiem xang",
                    "hop nu",
                    "di em",
                    "thuc dung",
                    "di lam",
                    "di hoc",
                    "gon",
                    "nhe",
                    "re",
                    "dep",
                    "khac",
                    "budget",
                    "ngan sach");

            bool looksLikeShortFollowUp =
                text.StartsWith("neu ") ||
                text.StartsWith("uu tien ") ||
                text.StartsWith("chi lay ") ||
                text.StartsWith("bo ") ||
                text.StartsWith("con ") ||
                text.StartsWith("the ") ||
                text.StartsWith("vay ");

            bool budgetOnlyFragment =
                LooksLikeBudgetFragment(text) &&
                (looksLikeShortFollowUp || text.StartsWith("quanh ") || text.StartsWith("tam ") || text.StartsWith("khoang "));

            if (hasComparativeTone && hasFeature)
                return true;

            if (budgetOnlyFragment)
                return true;
            if (ContainsAny(text,
     "re hon",
     "dat hon",
     "dep hon",
     "loai khac",
     "xe khac",
     "mau khac",
     "cho t xem loai khac",
     "cho toi xem loai khac",
     "xem loai khac",
     "doi loai khac",
     "tang budget",
     "them ngan sach"))
            {
                return true;
            }

            return ContainsAny(text,
                "con nao cop rong hon",
                "xe nao cop rong hon",
                "con nao de chong chan hon",
                "xe nao de chong chan hon",
                "con nao tiet kiem xang hon",
                "xe nao tiet kiem xang hon",
                "con nao hop nu hon",
                "xe nao hop nu hon",
                "con nao di lam on hon",
                "con nao di hoc on hon",
                "con nao gon hon",
                "con nao nhe hon",
                "con nao di em hon",
                "xe nao di em hon",
                "con nao thuc dung hon",
                "xe nao thuc dung hon",
                "cop rong hon",
                "de chong chan hon",
                "tiet kiem xang hon",
                "hop nu hon",
                "di em hon",
                "thuc dung hon");
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k));
        }

        private static bool HasNonExcludedWholeWord(string text, string entity)
        {
            return HasWholeWord(text, entity) && !IsEntityInExclusionScope(text, entity);
        }

        private static bool HasNonExcludedPhrase(string text, params string[] phrases)
        {
            return phrases.Any(phrase => HasWholePhrase(text, phrase) && !IsEntityInExclusionScope(text, phrase));
        }

        private static bool IsBrandExplicitlyExcluded(string text, string brand)
        {
            var escaped = Regex.Escape(brand);
            var patterns = new[]
            {
                $@"\b(khong\s+thich|khong\s+muon|ghet|ne|bo|dung\s+goi\s+y|dung\s+de\s+xuat|loai\s+tru)\s+(xe\s+)?{escaped}\b",
                $@"\b(ngoai\s+tru|tru)\s+(xe\s+)?{escaped}\b",
                $@"\b{escaped}\s+(khong\s+duoc\s+goi\s+y|khong\s+duoc\s+de\s+xuat)\b"
            };

            return patterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase))
                   || IsEntityInExclusionScope(text, brand);
        }

        private static bool IsCategoryExplicitlyExcluded(string text, string category)
        {
            var escaped = Regex.Escape(category);
            var patterns = new[]
            {
                $@"\b(khong\s+thich|khong\s+muon|ghet|ne|bo|dung\s+goi\s+y|dung\s+de\s+xuat|loai\s+tru)\s+{escaped}\b",
                $@"\b(ngoai\s+tru|tru)\s+{escaped}\b"
            };

            return patterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase))
                   || IsEntityInExclusionScope(text, category);
        }

        private static bool IsProductExplicitlyExcluded(string text, string product)
        {
            var escaped = Regex.Escape(product);
            var patterns = new[]
            {
                $@"\b(khong\s+thich|khong\s+muon|ghet|ne|bo|dung\s+goi\s+y|dung\s+de\s+xuat|loai\s+tru)\s+(xe\s+)?{escaped}\b",
                $@"\b(ngoai\s+tru|tru)\s+(xe\s+)?{escaped}\b",
                $@"\b{escaped}\s+(khong\s+duoc\s+goi\s+y|khong\s+duoc\s+de\s+xuat)\b"
            };

            return patterns.Any(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase))
                   || IsEntityInExclusionScope(text, product);
        }

        private static bool IsTargetExplicitlyNegated(string text, string target)
        {
            return IsEntityInExclusionScope(text, target) ||
                   Regex.IsMatch(
                       text,
                       $@"\b(khong\s+phai|khong\s+can|khong\s+cho|khong\s+hop)\s+(la\s+)?{Regex.Escape(target)}\b",
                       RegexOptions.IgnoreCase);
        }

        private static bool IsEntityInExclusionScope(string text, string entity)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(entity))
                return false;

            var pattern = $@"(?<!\p{{L}}|\p{{N}}){Regex.Escape(entity)}(?!\p{{L}}|\p{{N}})";
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
            {
                var prefixStart = Math.Max(0, match.Index - 80);
                var prefix = text[prefixStart..match.Index];

                var lastNegative = LastIndexOfAny(prefix,
                    "khong thich",
                    "khong muon",
                    "khong can",
                    "khong lay",
                    "khong chon",
                    "khong goi y",
                    "khong de xuat",
                    "khong phai",
                    "khong hop",
                    "khong nen",
                    "khong",
                    "dung goi y",
                    "dung de xuat",
                    "dung lay",
                    "dung chon",
                    "dung",
                    "ngoai tru",
                    "loai tru",
                    "tru",
                    "tranh",
                    "ghet",
                    "ne",
                    "bo");

                if (lastNegative < 0)
                    continue;

                var lastPositive = LastIndexOfAny(prefix,
                    "nhung muon",
                    "nhung thich",
                    "ma muon",
                    "ma thich",
                    "muon",
                    "thich",
                    "uu tien",
                    "chon",
                    "lay",
                    "can",
                    "tu van",
                    "goi y");

                if (lastNegative >= lastPositive)
                    return true;
            }

            return false;
        }

        private static int LastIndexOfAny(string text, params string[] values)
        {
            var result = -1;

            foreach (var value in values)
            {
                var index = text.LastIndexOf(value, StringComparison.OrdinalIgnoreCase);
                if (index > result)
                    result = index;
            }

            return result;
        }

        private static string Normalize(string input)
        {
            var text = input.Trim().ToLowerInvariant();
            text = RemoveVietnameseSigns(text);
            text = Regex.Replace(text, "\\s+", " ");
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

        private static bool IsExplicitShMention(string text)
        {
            return Regex.IsMatch(
                text,
                @"(?<!\p{L}|\p{N})(honda\s+)?sh(\s*(mode|125|125i|150|150i|160|160i|350|350i))?(?!\p{L}|\p{N})",
                RegexOptions.IgnoreCase);
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

        private static string ToDisplayProductName(string normalizedName)
        {
            return normalizedName switch
            {
                "vision" => "Honda Vision",
                "air blade" => "Honda Air Blade",
                "freego" => "Yamaha Freego",
                "latte" => "Yamaha Latte",
                "grande" => "Yamaha Grande",
                "zip" => "Piaggio Zip 100",
                "future" => "Honda Future",
                "wave" => "Honda Wave",
                "sirius" => "Yamaha Sirius",
                "address" => "Suzuki Address 110",
                "impulse" => "Suzuki Impulse 125",
                "lead" => "Honda Lead",
                "janus" => "Yamaha Janus",
                "vario" => "Honda Vario",
                "winner" => "Honda Winner X",
                "exciter" => "Yamaha Exciter",
                "pcx" => "Honda PCX",
                "sh" => "Honda SH",
                "shark" => "SYM Shark Mini",
                "shark mini" => "SYM Shark Mini",
                "attila" => "SYM Attila Venus",
                "attila venus" => "SYM Attila Venus",
                _ => normalizedName
            };
        }

        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            var n = s.Length;
            var m = t.Length;
            var d = new int[n + 1, m + 1];

            for (var i = 0; i <= n; i++) d[i, 0] = i;
            for (var j = 0; j <= m; j++) d[0, j] = j;

            for (var i = 1; i <= n; i++)
            {
                for (var j = 1; j <= m; j++)
                {
                    var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
