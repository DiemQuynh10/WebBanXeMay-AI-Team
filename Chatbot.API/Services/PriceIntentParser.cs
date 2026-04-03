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

            var text = message.ToLowerInvariant();

            // bắt số kiểu "40 triệu", "45tr", "30"
            var match = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(triệu|tr)?");
            if (!match.Success)
                return result;

            var rawNumber = match.Groups[1].Value.Replace(",", ".");
            if (!decimal.TryParse(rawNumber, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return result;
            }

            var amount = value * 1_000_000m;

            // 1) khoảng / tầm / cỡ / quanh
            if (text.Contains("khoảng") || text.Contains("tầm") || text.Contains("cỡ") || text.Contains("quanh"))
            {
                var delta = GetAroundDelta(amount);

                result.FilterType = PriceFilterType.Around;
                result.TargetPrice = amount;
                result.MinPrice = amount - delta;
                result.MaxPrice = amount + delta;
                return result;
            }

            // 2) dưới / không quá / tối đa
            if (text.Contains("dưới") || text.Contains("không quá") || text.Contains("tối đa"))
            {
                result.FilterType = PriceFilterType.MaxOnly;
                result.MaxPrice = amount;
                return result;
            }

            // 3) trên / từ ... trở lên / ít nhất
            if (text.Contains("trên") || text.Contains("trở lên") || text.Contains("ít nhất"))
            {
                result.FilterType = PriceFilterType.MinOnly;
                result.MinPrice = amount;
                return result;
            }

            // 4) từ x đến y
            var rangeMatch = Regex.Match(text, @"từ\s+(\d+(?:[.,]\d+)?)\s*(triệu|tr)?\s+đến\s+(\d+(?:[.,]\d+)?)\s*(triệu|tr)?");
            if (rangeMatch.Success)
            {
                var rawMin = rangeMatch.Groups[1].Value.Replace(",", ".");
                var rawMax = rangeMatch.Groups[3].Value.Replace(",", ".");

                if (decimal.TryParse(rawMin, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var minVal) &&
                    decimal.TryParse(rawMax, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var maxVal))
                {
                    result.FilterType = PriceFilterType.Range;
                    result.MinPrice = minVal * 1_000_000m;
                    result.MaxPrice = maxVal * 1_000_000m;
                    return result;
                }
            }

            return result;
        }

        private static decimal GetAroundDelta(decimal amount)
        {
            if (amount <= 20_000_000m) return 3_000_000m;
            if (amount <= 50_000_000m) return 5_000_000m;
            return 10_000_000m;
        }
    }
}