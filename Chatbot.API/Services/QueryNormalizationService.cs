using System.Text.RegularExpressions;
using Chatbot.API.Models.Normalization;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class QueryNormalizationService : IQueryNormalizationService
    {
        private static readonly Dictionary<string, string> ReplacementMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["rer"] = "rẻ",
                ["re"] = "rẻ",
                ["gia"] = "giá",
                ["ton kho"] = "tồn kho",
                ["con hang"] = "còn hàng",
                ["het hang"] = "hết hàng",
                ["bn"] = "bao nhiêu",
                ["ntn"] = "như thế nào",
                ["ko"] = "không",
                ["k"] = "không",
                ["dc"] = "được",
                ["vison"] = "vision",
                ["visionn"] = "vision",
                ["air blaed"] = "air blade",
                ["airblade"] = "air blade",
                ["ab"] = "air blade",
                ["hondaa"] = "honda",
                ["yamahaa"] = "yamaha",
                ["sv"] = "sinh viên",
                ["tu van"] = "tư vấn",
                ["phu hop"] = "phù hợp"
            };

        private static readonly HashSet<string> HighRiskTokens =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "rer", "re", "vison", "visionn", "air blaed", "ab", "ko", "k", "bn", "sv"
            };

        public NormalizationResult Analyze(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new NormalizationResult
                {
                    OriginalText = input ?? string.Empty,
                    NormalizedText = string.Empty,
                    HasChanges = false,
                    NeedsConfirmation = false
                };
            }

            var original = input.Trim();
            var normalized = original.ToLowerInvariant();
            normalized = Regex.Replace(normalized, @"\s+", " ");

            foreach (var kvp in ReplacementMap
                         .Where(x => x.Key.Contains(' '))
                         .OrderByDescending(x => x.Key.Length))
            {
                normalized = ReplaceWholePhrase(normalized, kvp.Key, kvp.Value);
            }

            var originalWords = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var changedWords = new List<string>();

            var newWords = originalWords.Select(word =>
            {
                if (ReplacementMap.TryGetValue(word, out var replacement))
                {
                    if (!string.Equals(word, replacement, StringComparison.OrdinalIgnoreCase))
                    {
                        changedWords.Add(word);
                    }
                    return replacement;
                }

                return word;
            }).ToArray();

            normalized = string.Join(' ', newWords);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            var hasChanges = !string.Equals(original, normalized, StringComparison.OrdinalIgnoreCase);

            var riskyChange = changedWords.Any(w => HighRiskTokens.Contains(w));

            // Hỏi lại nếu:
            // - có thay đổi
            // - và có ít nhất 1 token rủi ro cao
            var needsConfirmation = hasChanges && riskyChange;

            return new NormalizationResult
            {
                OriginalText = original,
                NormalizedText = normalized,
                HasChanges = hasChanges,
                NeedsConfirmation = needsConfirmation,
                Reason = needsConfirmation
                    ? "Phát hiện từ khóa có thể bị gõ sai hoặc viết tắt."
                    : null
            };
        }

        private static string ReplaceWholePhrase(string input, string oldPhrase, string newPhrase)
        {
            var pattern = $@"\b{Regex.Escape(oldPhrase)}\b";
            return Regex.Replace(input, pattern, newPhrase, RegexOptions.IgnoreCase);
        }
    }
}