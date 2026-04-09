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
            ParseIntentType(text, result);
            ParseFollowUp(text, result);
            ParseComparisonFeature(text, result);
            ResolveBrandAndCategoryConflicts(result);
            return Task.FromResult(result);
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
            if (!result.ExcludedBrands.Contains("Honda") && text.Contains("honda"))
            {
                result.Brand = "Honda";
                return;
            }

            if (!result.ExcludedBrands.Contains("Yamaha") && text.Contains("yamaha"))
            {
                result.Brand = "Yamaha";
                return;
            }

            if (!result.ExcludedBrands.Contains("Suzuki") && text.Contains("suzuki"))
            {
                result.Brand = "Suzuki";
                return;
            }

            if (!result.ExcludedBrands.Contains("SYM") && text.Contains("sym"))
            {
                result.Brand = "SYM";
                return;
            }

            if (!result.ExcludedBrands.Contains("Piaggio") && text.Contains("piaggio"))
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
            foreach (var product in KnownProducts)
            {
                if (text.Contains(product, StringComparison.OrdinalIgnoreCase))
                {
                    result.MentionedProducts.Add(ToDisplayProductName(product));
                }
            }

            result.MentionedProducts = result.MentionedProducts
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void ParseIntentType(string text, ParsedIntent result)
        {
            if (IsExplicitCompareIntent(text, result.MentionedProducts.Count))
            {
                result.IntentType = "compare";
                return;
            }

            if (IsSwitchBrandIntent(text))
            {
                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "switch_brand";
                return;
            }

            if (IsRefineIntent(text))
            {
                result.IntentType = "refine";
                result.IsFollowUp = true;
                result.FollowUpType = "refine";
                return;
            }

            if (IsRecommendationFollowUpIntent(text, result.MentionedProducts.Count))
            {
                result.IntentType = "followup";
                result.IsFollowUp = true;
                result.FollowUpType = "rerank_previous_list";
                return;
            }

            if (ContainsAny(text, "gia", "bao nhieu", "con hang", "ton kho"))
            {
                result.IntentType = "lookup";
                return;
            }

            if (ContainsAny(text, "tu van", "goi y", "phu hop", "nen mua", "xe nao"))
            {
                result.IntentType = "recommend";
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

            if (result.IntentType == "refine" || result.IntentType == "followup")
            {
                result.IsFollowUp = true;
                return;
            }
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
    }
}