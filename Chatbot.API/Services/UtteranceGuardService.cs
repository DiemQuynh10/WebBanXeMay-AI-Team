using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class UtteranceGuardService : IUtteranceGuardService
    {
        private static readonly HashSet<string> AckTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "ok", "oke", "oki", "okie", "vâng", "vang", "ừ", "uh", "uhm", "dạ", "da",
            "ờ", "a", "à", "uhh", "yes", "roi", "rồi", "duoc", "được"
        };

        public UtteranceGuardResult Analyze(string? normalizedMessage, ParsedIntent? parsedIntent)
        {
            var text = (normalizedMessage ?? string.Empty).Trim().ToLowerInvariant();
            parsedIntent ??= new ParsedIntent();

            if (string.IsNullOrWhiteSpace(text))
            {
                return new UtteranceGuardResult
                {
                    Kind = UtteranceKind.Noise,
                    ShouldAskClarification = true,
                    ClarificationMessage = "Mình chưa thấy nội dung câu hỏi. Bạn thử hỏi như: giá Vision bao nhiêu, còn Air Blade không, hoặc tư vấn xe cho nữ tầm 40 triệu nhé."
                };
            }

            if (parsedIntent.IsGreeting)
            {
                return new UtteranceGuardResult { Kind = UtteranceKind.Greeting };
            }

            if (parsedIntent.IsOutOfScope)
            {
                return new UtteranceGuardResult
                {
                    Kind = UtteranceKind.OutOfScope,
                    ShouldAskClarification = false,
                    ClarificationMessage = "Mình hiện chỉ hỗ trợ về xe máy, sản phẩm trong hệ thống, tồn kho, so sánh và tra cứu đơn hàng."
                };
            }

            if (IsAckOnly(text))
            {
                return new UtteranceGuardResult
                {
                    Kind = UtteranceKind.Ack,
                    ShouldAskClarification = false
                };
            }

            if (IsLikelyNoise(text, parsedIntent))
            {
                return new UtteranceGuardResult
                {
                    Kind = UtteranceKind.Noise,
                    ShouldAskClarification = true,
                    ClarificationMessage = "Mình chưa hiểu ý bạn. Bạn thử nói rõ hơn như: giá Vision bao nhiêu, còn xe ga Honda nào tầm 40 triệu, hoặc kiểm tra đơn hàng giúp mình."
                };
            }

            if (HasDomainSignal(parsedIntent, text))
            {
                return new UtteranceGuardResult { Kind = UtteranceKind.Domain };
            }

            return new UtteranceGuardResult
            {
                Kind = UtteranceKind.Unknown,
                ShouldAskClarification = true,
                ClarificationMessage = "Bạn nói rõ hơn giúp mình nhé. Mình đang hỗ trợ tra giá xe, tồn kho, so sánh xe, tư vấn mẫu phù hợp và tra cứu đơn hàng."
            };
        }

        private static bool IsAckOnly(string text)
        {
            if (AckTokens.Contains(text))
                return true;

            return text.Length <= 6 && AckTokens.Contains(text.Replace(".", "").Replace("!", "").Replace("?", ""));
        }

        private static bool IsLikelyNoise(string text, ParsedIntent parsedIntent)
        {
            if (HasDomainSignal(parsedIntent, text))
                return false;

            if (Regex.IsMatch(text, @"^[\.\,\?\!\-\_\/\\\@\#\$\%\^\&\*\(\)\+\=]+$"))
                return true;

            if (Regex.IsMatch(text, @"^[a-z]{4,}$") && !LooksLikeMeaningfulAsciiWord(text))
                return true;

            if (Regex.IsMatch(text, @"^(.)\1{2,}$"))
                return true;

            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 1)
            {
                var token = tokens[0];
                if (token.Length >= 4 && Regex.IsMatch(token, @"^[a-z0-9]+$") && !LooksLikeMeaningfulAsciiWord(token))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeMeaningfulAsciiWord(string token)
        {
            return token is "vision" or "vario" or "winner" or "blade" or "honda" or "yamaha"
                or "sym" or "xe" or "gia" or "giao" or "don" or "hang" or "ga" or "so";
        }

        private static bool HasDomainSignal(ParsedIntent intent, string text)
        {
            return intent.IsDirectProductLookup
                   || intent.IsProductSearch
                   || intent.IsOpenRecommendation
                   || intent.IsDirectCompare
                   || intent.IsOrderLookup
                   || !string.IsNullOrWhiteSpace(intent.Brand)
                   || !string.IsNullOrWhiteSpace(intent.Category)
                   || !string.IsNullOrWhiteSpace(intent.Target)
                   || intent.PriceMin.HasValue
                   || intent.PriceMax.HasValue
                   || intent.TargetPrice.HasValue
                   || intent.MentionedProducts.Count > 0
                   || text.Contains("xe")
                   || text.Contains("giá")
                   || text.Contains("gia")
                   || text.Contains("tồn kho")
                   || text.Contains("ton kho")
                   || text.Contains("đơn hàng")
                   || text.Contains("don hang");
        }
    }
}