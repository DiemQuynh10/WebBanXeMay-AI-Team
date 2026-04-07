using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class IntentParserService : IIntentParserService
    {
        public Task<ParsedIntent> ParseAsync(string message)
        {
            var result = new ParsedIntent
            {
                RawMessage = message ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
                return Task.FromResult(result);

            var text = Normalize(message);

            // Preferred category
            if (ContainsAny(text, "xe ga", "tay ga", "scooter"))
                result.Category = "xe ga";
            else if (ContainsAny(text, "xe so", "xe số"))
                result.Category = "xe số";
            else if (ContainsAny(text, "con tay", "côn tay", "xe con", "xe côn"))
                result.Category = "côn tay";

            // Excluded category
            if (ContainsAny(text, "khong thich xe ga", "khong muon xe ga", "ne xe ga", "ghet xe ga"))
                result.ExcludedCategories.Add("xe ga");

            if (ContainsAny(text, "khong thich xe so", "khong muon xe so", "ne xe so", "ghet xe so"))
                result.ExcludedCategories.Add("xe số");

            if (ContainsAny(text, "khong thich xe con", "khong muon xe con", "ne xe con", "ghet xe con", "khong thich con tay"))
                result.ExcludedCategories.Add("côn tay");

            // Preferred brand
            if (text.Contains("honda"))
                result.Brand = "Honda";
            else if (text.Contains("yamaha"))
                result.Brand = "Yamaha";
            else if (text.Contains("suzuki"))
                result.Brand = "Suzuki";
            else if (text.Contains("sym"))
                result.Brand = "SYM";
            else if (text.Contains("piaggio"))
                result.Brand = "Piaggio";

            // Excluded brand
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

            // Target
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

            // Use case
            result.ForSchool = ContainsAny(text, "di hoc", "hoc hang ngay", "den truong");
            result.ForWork = ContainsAny(text, "di lam", "di cong so", "cong so", "di lam hang ngay");
            result.ForCity = ContainsAny(text, "di pho", "noi thanh", "do thi", "trong pho");
            result.ForTour = ContainsAny(text, "di tour", "duong dai", "di xa", "phuot");

            // Preference features
            result.WantsEasyControl = ContainsAny(text, "de di", "de dieu khien", "de chong chan", "nhe", "gon", "linh hoat");
            result.WantsFuelSaving = ContainsAny(text, "tiet kiem xang", "it ton xang", "hao xang thap");
            result.WantsLargeStorage = ContainsAny(text, "cop rong", "de do", "chua do");

            // Height
            result.HeightCm = ExtractHeightCm(text);
            if (result.HeightCm.HasValue && result.HeightCm.Value <= 150)
            {
                result.NeedsLowSeat = true;
                result.WantsEasyControl = true;
            }

            if (ContainsAny(text, "nguoi thap", "người thấp", "nho con", "nhỏ con"))
            {
                result.NeedsLowSeat = true;
                result.WantsEasyControl = true;
            }

            if (ContainsAny(text, "de chong chan", "dễ chống chân", "yen thap", "yên thấp"))
            {
                result.NeedsLowSeat = true;
                result.WantsEasyControl = true;
            }

            // Styles
            if (ContainsAny(text, "the thao", "nang dong"))
                result.RequestedStyles.Add("sporty");

            if (ContainsAny(text, "thanh lich", "nhe nhang", "sang"))
                result.RequestedStyles.Add("elegant");

            if (ContainsAny(text, "ca tinh", "manh me", "ham ho"))
                result.RequestedStyles.Add("aggressive");

            if (ContainsAny(text, "nho gon", "gon", "linh hoat"))
                result.RequestedStyles.Add("compact");

            return Task.FromResult(result);
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
    }
}