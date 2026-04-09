using System.Text.RegularExpressions;
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

        private static void ParseOutOfScope(string text, ParsedIntent result)
        {
            // Chỉ đánh dấu out-of-scope khi không có tín hiệu xe máy / sản phẩm / đơn hàng
            bool hasMotorbikeSignal =
                result.MentionedProducts.Any()
                || !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category)
                || ContainsAny(text,
                    "xe may", "xe ga", "xe so", "con tay",
                    "gia", "bao nhieu", "ton kho", "con hang",
                    "don hang", "ma don", "tra don", "kiem tra don");

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

            bool asksPrice = ContainsAny(text, "gia", "bao nhieu", "may tien");
            bool asksStock = ContainsAny(text, "con hang", "ton kho", "con khong", "het hang", "co san");
            bool asksCc = Regex.IsMatch(text, @"\bcc\b", RegexOptions.IgnoreCase)
                          || ContainsAny(text, "bao nhieu phan khoi", "dung tich", "phan khoi");
            bool asksDetail = ContainsAny(text,
                "chi tiet",
                "thong tin",
                "mo ta",
                "co gi",
                "xem chi tiet");

            if (asksPrice)
                result.LookupField = "price";
            else if (asksStock)
                result.LookupField = "stock";
            else if (asksCc)
                result.LookupField = "cc";
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
        }

        private static void ParseCategory(string text, ParsedIntent result)
        {
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
            if (ContainsAny(text, "khong thich xe ga", "khong muon xe ga", "ne xe ga", "ghet xe ga"))
                result.ExcludedCategories.Add("xe ga");

            if (ContainsAny(text, "khong thich xe so", "khong muon xe so", "ne xe so", "ghet xe so"))
                result.ExcludedCategories.Add("xe số");

            if (ContainsAny(text, "khong thich xe con", "khong muon xe con", "ne xe con", "ghet xe con", "khong thich con tay"))
                result.ExcludedCategories.Add("côn tay");
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
            if (ContainsAny(text, "khong thich honda", "khong muon honda", "ne honda", "ghet honda"))
                result.ExcludedBrands.Add("Honda");
            if (ContainsAny(text, "khong thich yamaha", "khong muon yamaha", "ne yamaha", "ghet yamaha"))
                result.ExcludedBrands.Add("Yamaha");
            if (ContainsAny(text, "khong thich suzuki", "khong muon suzuki", "ne suzuki", "ghet suzuki"))
                result.ExcludedBrands.Add("Suzuki");
            if (ContainsAny(text, "khong thich sym", "khong muon sym", "ne sym", "ghet sym"))
                result.ExcludedBrands.Add("SYM");
            if (ContainsAny(text, "khong thich piaggio", "khong muon piaggio", "ne piaggio", "ghet piaggio"))
                result.ExcludedBrands.Add("Piaggio");
        }

        private static void ParseTarget(string text, ParsedIntent result)
        {
            var targets = new List<string>();

            if (ContainsAny(text, "sinh vien", "hoc sinh"))
                targets.Add("sinh viên");

            if (ContainsAny(text, "nu", "phai nu", "phu nu"))
                targets.Add("nữ");

            if (ContainsAny(text, "nam", "phai nam"))
                targets.Add("nam");

            if (targets.Any())
                result.Target = string.Join(" ", targets.Distinct());

            result.PrefersFemaleStyle = targets.Contains("nữ");
            result.PrefersMaleStyle = targets.Contains("nam");
        }

        private static void ParseUseCases(string text, ParsedIntent result)
        {
            result.ForSchool = ContainsAny(text, "di hoc", "hoc hang ngay", "den truong");
            result.ForWork = ContainsAny(text, "di lam", "di cong so", "cong so", "di lam hang ngay");
            result.ForCity = ContainsAny(text, "di pho", "noi thanh", "do thi", "trong pho");
            result.ForTour = ContainsAny(text, "di tour", "duong dai", "di xa", "phuot");
        }

        private static void ParsePreferenceFeatures(string text, ParsedIntent result)
        {
            result.WantsEasyControl = ContainsAny(text, "de di", "de dieu khien", "de chong chan", "nhe", "gon", "linh hoat");
            result.WantsFuelSaving = ContainsAny(text, "tiet kiem xang", "it ton xang", "hao xang thap");
            result.WantsLargeStorage = ContainsAny(text, "cop rong", "de do", "chua do");
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

            if (ContainsAny(text, "thanh lich", "nhe nhang", "sang"))
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
                if (HasWholePhrase(text, product))
                {
                    result.MentionedProducts.Add(ToDisplayProductName(product));
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

            if (ContainsAny(text, "hop nu", "cho nu", "nu", "nu tinh"))
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

            bool hasSearchConstraint =
                !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category)
                || result.PriceMin.HasValue
                || result.PriceMax.HasValue
                || result.TargetPrice.HasValue;

            bool hasOpenRecommendPhrase = ContainsAny(text, "tu van", "goi y", "phu hop", "nen mua", "xe nao");

            bool hasDirectLookupPhrase = !string.IsNullOrWhiteSpace(result.LookupField);

            if (hasDirectLookupPhrase)
                return false;

            if (hasSearchConstraint && !hasOpenRecommendPhrase)
                return true;

            // ví dụ: xe ga yamaha, xe honda dưới 40 triệu
            if (ContainsAny(text, "xe ga", "xe so", "con tay", "honda", "yamaha", "suzuki", "sym", "piaggio") &&
                (ContainsAny(text, "duoi", "tren", "tu", "den", "khoang", "tam", "quanh") || result.PriceMin.HasValue || result.PriceMax.HasValue || result.TargetPrice.HasValue))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.Brand) && !string.IsNullOrWhiteSpace(result.Category))
                return true;

            return false;
        }

        private static bool LooksLikeOpenRecommendation(string text, ParsedIntent result)
        {
            if (result.IsDirectProductLookup || result.IsOrderLookup || result.IsGreeting || result.IsOutOfScope)
                return false;

            if (ContainsAny(text, "tu van", "goi y", "phu hop", "nen mua", "xe nao"))
                return true;

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

            bool hasBudgetOrFilter =
                result.TargetPrice.HasValue
                || result.PriceMin.HasValue
                || result.PriceMax.HasValue
                || !string.IsNullOrWhiteSpace(result.Brand)
                || !string.IsNullOrWhiteSpace(result.Category);

            // ví dụ: xe cho nữ khoảng 40 triệu
            if (hasHumanNeed && hasBudgetOrFilter)
                return true;

            // ví dụ: tư vấn xe đi làm
            if (hasHumanNeed && ContainsAny(text, "xe", "mau xe"))
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
            return ContainsAny(text,
                "khong thich",
                "khong muon",
                "dung",
                "ne",
                "ghet",
                "uu tien",
                "bot",
                "them dieu kien");
        }

        private static bool IsSwitchBrandIntent(string text)
        {
            return text.StartsWith("con ")
                   || text.StartsWith("còn ")
                   || ContainsAny(text,
                       "con honda thi sao",
                       "con yamaha thi sao",
                       "con suzuki thi sao",
                       "con sym thi sao",
                       "con piaggio thi sao",
                       "còn honda thì sao",
                       "còn yamaha thì sao",
                       "còn suzuki thì sao",
                       "còn sym thì sao",
                       "còn piaggio thì sao");
        }

        private static bool IsRecommendationFollowUpIntent(string text, int mentionedProductCount)
        {
            if (mentionedProductCount >= 2)
                return false;

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
                "sh" => "Honda SH 150i",
                "shark" => "SYM Shark Mini",
                "shark mini" => "SYM Shark Mini",
                "attila" => "SYM Attila Venus",
                "attila venus" => "SYM Attila Venus",
                _ => normalizedName
            };
        }
    }
}