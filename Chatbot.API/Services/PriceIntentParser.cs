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
            if (LooksLikeHeightOrPhysicalPreference(text))
            {
                return result;
            }

            // 1) từ x đến y / x-y triệu
            var rangePatterns = new[]
            {
                @"tu\s+(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|củ|chai)?\s+den\s+(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|củ|chai)?",
                @"(\d+(?:[.,]\d+)?)\s*[-~]\s*(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|củ|chai)?"
            };

            foreach (var pattern in rangePatterns)
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var g1 = match.Groups[1].Value.Replace(",", ".");
                    var g2 = match.Groups[2].Value.Replace(",", ".");

                    if (decimal.TryParse(g1, NumberStyles.Any, CultureInfo.InvariantCulture, out var minVal) &&
                        decimal.TryParse(g2, NumberStyles.Any, CultureInfo.InvariantCulture, out var maxVal))
                    {
                        result.FilterType = PriceFilterType.Range;
                        result.MinPrice = minVal * 1_000_000m;
                        result.MaxPrice = maxVal * 1_000_000m;
                        return result;
                    }
                }
            }

            // 2) 3x triệu -> coi như khoảng 35 triệu
            var xMatch = Regex.Match(text, @"(\d)x\s*(trieu|tr|cu|củ|chai)?", RegexOptions.IgnoreCase);
            if (xMatch.Success && int.TryParse(xMatch.Groups[1].Value, out var firstDigit))
            {
                var target = (firstDigit * 10 + 5) * 1_000_000m;
                var delta = 5_000_000m;

                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = target;
                result.MinPrice = target - delta;
                result.MaxPrice = target + delta;
                return result;
            }

            // 3) số đơn
            var matchSingle = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(trieu|tr|cu|củ|chai)?", RegexOptions.IgnoreCase);
            if (!matchSingle.Success)
                return result;

            var rawNumber = matchSingle.Groups[1].Value.Replace(",", ".");
            if (!decimal.TryParse(rawNumber, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return result;

            var amount = value * 1_000_000m;

            // khoảng / tầm / quanh / cỡ
            if (ContainsAny(text, "khoang", "tam", "quanh", "co"))
            {
                var delta = GetAroundDelta(amount);
                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = amount;
                result.MinPrice = Math.Max(0, amount - delta);
                result.MaxPrice = amount + delta;
                return result;
            }

            // dưới / tối đa / không quá
            if (ContainsAny(text, "duoi", "toi da", "khong qua"))
            {
                result.FilterType = PriceFilterType.MaxOnly;
                result.MaxPrice = amount;
                return result;
            }

            // trên / ít nhất / trở lên
            if (ContainsAny(text, "tren", "it nhat", "tro len"))
            {
                result.FilterType = PriceFilterType.MinOnly;
                result.MinPrice = amount;
                return result;
            }

            return result;
        }
        private static string Normalize(string input)
        {
            var text = input.Trim().ToLowerInvariant()
                .Replace("củ", "cu")
                .Replace("chai", "chai")
                .Replace("triệu", "trieu")
                .Replace("đ", "")
                .Replace("₫", "");

            text = RemoveVietnameseSigns(text);
            text = Regex.Replace(text, @"\s+", " ");
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