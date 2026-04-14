using System.Globalization;
using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class PriceIntentParser : IPriceIntentParser
    {
        public PriceIntent Parse(string message)
        {
            var result = new PriceIntent
            {
                RawText = message ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
                return result;

            var text = Normalize(message);
            text = NormalizeSpelledPriceWords(text);

            if (LooksLikeHeightOrPhysicalPreference(text))
                return result;
            if (TryParseRestartOverrideTargetPrice(text, out var overrideTarget))
            {
                var delta = GetAroundDelta(overrideTarget);

                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = overrideTarget;
                result.MinPrice = Math.Max(0, overrideTarget - delta);
                result.MaxPrice = overrideTarget + delta;
                return result;
            }
            if (ContainsAny(text, "gia mem", "re thoi", "re re", "mem thoi", "gia de chiu"))
            {
                result.FilterType = PriceFilterType.MaxOnly;
                result.MaxPrice = 30_000_000m;
                result.TargetPrice = null;
                return result;
            }
            //if (TryParseNumericRangeLoosely(text, out var looseMin, out var looseMax))
            //{
            //    result.FilterType = PriceFilterType.Range;
            //    result.MinPrice = looseMin;
            //    result.MaxPrice = looseMax;
            //    result.TargetPrice = null;
            //    return result;
            //}

            if (TryParseRange(text, out var minPrice, out var maxPrice))
            {
                result.FilterType = PriceFilterType.Range;
                result.MinPrice = minPrice;
                result.MaxPrice = maxPrice;
                result.TargetPrice = null;
                return result;
            }
            var maxMatch = Regex.Match(
                text,
                @"\b(duoi|toi da|khong qua)\s+(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|chai)?\b",
                RegexOptions.IgnoreCase);

            if (maxMatch.Success &&
                decimal.TryParse(maxMatch.Groups[2].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxValue))
            {
                result.FilterType = PriceFilterType.MaxOnly;
                result.MaxPrice = maxValue * 1_000_000m;
                result.TargetPrice = null;
                return result;
            }

            var minMatch = Regex.Match(
                text,
                @"\b(tren|it nhat|tro len)\s+(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|chai)?\b",
                RegexOptions.IgnoreCase);

            if (minMatch.Success &&
                decimal.TryParse(minMatch.Groups[2].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var minValue))
            {
                result.FilterType = PriceFilterType.MinOnly;
                result.MinPrice = minValue * 1_000_000m;
                result.TargetPrice = null;
                return result;
            }

            var xMatch = Regex.Match(
                text,
                @"\b(\d)x\s*(trieu|tr|cu|chai)?\b",
                RegexOptions.IgnoreCase);

            if (xMatch.Success && int.TryParse(xMatch.Groups[1].Value, out var firstDigit))
            {
                var target = (firstDigit * 10 + 5) * 1_000_000m;
                var delta = 5_000_000m;

                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = target;
                result.MinPrice = Math.Max(0, target - delta);
                result.MaxPrice = target + delta;
                return result;
            }

            var aroundMatch = Regex.Match(
    text,
    @"\b(khoang|tam|quanh)\s+(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|chai)?\b",
    RegexOptions.IgnoreCase);

            bool containsRangeWord = text.Contains(" den ") || text.Contains("-") || text.Contains("~");

            if (!containsRangeWord &&
                aroundMatch.Success &&
                decimal.TryParse(aroundMatch.Groups[2].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var aroundValue))
            {
                var amount = aroundValue * 1_000_000m;
                var delta = GetAroundDelta(amount);

                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = amount;
                result.MinPrice = Math.Max(0, amount - delta);
                result.MaxPrice = amount + delta;
                return result;
            }

            var singleMatch = Regex.Match(
                text,
                @"\b(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|chai)\b",
                RegexOptions.IgnoreCase);

            if (singleMatch.Success &&
                decimal.TryParse(singleMatch.Groups[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var singleValue))
            {
                var amount = singleValue * 1_000_000m;
                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = amount;

                var delta = GetAroundDelta(amount);
                result.MinPrice = Math.Max(0, amount - delta);
                result.MaxPrice = amount + delta;

                return result;
            }

            return result;
        }
        private static bool TryParseRestartOverrideTargetPrice(string text, out decimal targetPrice)
        {
            targetPrice = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = Regex.Match(
                text,
                @"(?:khong phai\s+\d+(?:[.,]\d+)?\s*(?:trieu|tr|cu|chai)?\s*(?:nua)?[, ]*)?(?:gio|bay gio|y la|doi y|h t muon|vay h t muon)?\s*(?:quanh|khoang|tam)\s+(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|chai)?",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            if (!decimal.TryParse(
                    match.Groups[1].Value.Replace(",", "."),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return false;
            }

            targetPrice = value * 1_000_000m;
            return true;
        }
        private static bool TryParseNumericRangeLoosely(string text, out decimal minPrice, out decimal maxPrice)
        {
            minPrice = 0;
            maxPrice = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Lấy các số kiểu 30, 35, 30.5...
            var matches = Regex.Matches(text, @"\d+(?:[.,]\d+)?");

            if (matches.Count < 2)
                return false;

            // Chỉ lấy 2 số đầu tiên trong câu
            var raw1 = matches[0].Value.Replace(",", ".");
            var raw2 = matches[1].Value.Replace(",", ".");

            if (!decimal.TryParse(raw1, NumberStyles.Any, CultureInfo.InvariantCulture, out var v1))
                return false;

            if (!decimal.TryParse(raw2, NumberStyles.Any, CultureInfo.InvariantCulture, out var v2))
                return false;

            // Chỉ coi là khoảng giá nếu 2 số đều nằm trong miền hợp lý của "triệu"
            if (v1 < 1 || v2 < 1 || v1 > 500 || v2 > 500)
                return false;

            // Tránh hiểu nhầm số điện thoại / mã đơn / cc...
            var between = text.Substring(matches[0].Index, matches[1].Index - matches[0].Index);
            if (between.Length > 25)
                return false;

            minPrice = Math.Min(v1, v2) * 1_000_000m;
            maxPrice = Math.Max(v1, v2) * 1_000_000m;
            if (Math.Abs(v1 - v2) > 30)
                return false;
            return true;
        }
        private static bool TryParseRange(string text, out decimal minPrice, out decimal maxPrice)
        {
            minPrice = 0;
            maxPrice = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var match = Regex.Match(
                text,
                @"(?:khoang\s+)?(?:tu\s+)?(\d+(?:[.,]\d+)?)\s*(?:trieu|tr|cu|chai)?\s*(?:den|-|~)\s*(\d+(?:[.,]\d+)?)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            var rawMin = match.Groups[1].Value.Replace(",", ".");
            var rawMax = match.Groups[2].Value.Replace(",", ".");

            if (!decimal.TryParse(rawMin, NumberStyles.Any, CultureInfo.InvariantCulture, out var minVal))
                return false;

            if (!decimal.TryParse(rawMax, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxVal))
                return false;

            if (minVal > maxVal)
                (minVal, maxVal) = (maxVal, minVal);

            minPrice = minVal * 1_000_000m;
            maxPrice = maxVal * 1_000_000m;
            return true;
        }
        private static string Normalize(string input)
        {
            var text = input.Trim().ToLowerInvariant()
                .Replace("củ", "cu")
                .Replace("chai", "chai")
                .Replace("triệu", "trieu")
               .Replace("đ", "d")
.Replace("₫", "");

            text = RemoveVietnameseSigns(text);
            text = Regex.Replace(text, @"\s+", " ");
            return text;
        }
        private static string NormalizeSpelledPriceWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mười"] = "10",
                ["muoi"] = "10",
                ["hai mươi"] = "20",
                ["hai muoi"] = "20",
                ["ba mươi"] = "30",
                ["ba muoi"] = "30",
                ["bốn mươi"] = "40",
                ["bon muoi"] = "40",
                ["bốn mươi"] = "40",
                ["năm mươi"] = "50",
                ["nam muoi"] = "50",
                ["sáu mươi"] = "60",
                ["sau muoi"] = "60",
                ["bảy mươi"] = "70",
                ["bay muoi"] = "70",
                ["tám mươi"] = "80",
                ["tam muoi"] = "80",
                ["chín mươi"] = "90",
                ["chin muoi"] = "90"
            };

            foreach (var kv in map.OrderByDescending(x => x.Key.Length))
            {
                text = Regex.Replace(text, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value, RegexOptions.IgnoreCase);
            }

            return text;
        }
        private static decimal GetAroundDelta(decimal amount)
        {
            if (amount <= 20_000_000m) return 2_000_000m;
            if (amount <= 50_000_000m) return 3_000_000m;
            if (amount <= 80_000_000m) return 5_000_000m;
            return 7_000_000m;
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
        private static bool LooksLikeHeightOrPhysicalPreference(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"\b1m\d{1,2}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\bm\d{2}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"\b\d{3}\s*cm\b", RegexOptions.IgnoreCase)
                || ContainsAny(text,
                    "cao",
                    "chieu cao",
                    "chiều cao",
                    "nguoi thap",
                    "người thấp",
                    "nho con",
                    "nhỏ con",
                    "de chong chan",
                    "dễ chống chân",
                    "yen thap",
                    "yên thấp");
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k));
        }
    }
}