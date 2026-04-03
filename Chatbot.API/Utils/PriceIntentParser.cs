using System.Text.RegularExpressions;

namespace Chatbot.API.Utils
{
    public class PriceRange
    {
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
    }

    public static class PriceIntentParser
    {
        public static PriceRange Parse(string message)
        {
            message = message.ToLower();

            var matches = Regex.Matches(message, @"\d+");
            var numbers = matches.Select(m => int.Parse(m.Value)).ToList();

            if (!numbers.Any())
                return new PriceRange();

            if (message.Contains("từ") && message.Contains("đến") && numbers.Count >= 2)
            {
                return new PriceRange
                {
                    MinPrice = numbers[0],
                    MaxPrice = numbers[1]
                };
            }

            if (message.Contains("dưới") || message.Contains("<= "))
            {
                return new PriceRange
                {
                    MaxPrice = numbers[0]
                };
            }

            if (message.Contains("trên") || message.Contains(">="))
            {
                return new PriceRange
                {
                    MinPrice = numbers[0]
                };
            }

            if (message.Contains("tầm") || message.Contains("khoảng") || message.Contains("tầm giá"))
            {
                var value = numbers[0];

                return new PriceRange
                {
                    MinPrice = value - 5,
                    MaxPrice = value + 5
                };
            }

            return new PriceRange
            {
                MaxPrice = numbers[0]
            };
        }
    }
}