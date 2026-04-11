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
                ["honđa"] = "honda",
                ["honad"] = "honda",
                ["yahama"] = "yamaha",

                ["sv"] = "sinh viên",
                ["snh"] = "sinh",
                ["vin"] = "viên",
                ["tu van"] = "tư vấn",
                ["phu hop"] = "phù hợp",

                ["gaaa"] = "ga",
                ["duii"] = "dưới",

                ["triu"] = "triệu",
                ["trieuj"] = "triệu",
                ["trieeuj"] = "triệu",
                ["triêu"] = "triệu",
                ["trieu"] = "triệu",
                ["tr"] = "triệu",
                ["cu"] = "triệu",
                ["củ"] = "triệu",
                ["chai"] = "triệu",

                ["ghet"] = "ghét",
                ["ne"] = "né",
                ["khong khoai"] = "không thích",

                ["nho con"] = "nhỏ con",
                ["de chong chan"] = "dễ chống chân",
                ["yen thap"] = "yên thấp",
                ["cop rong"] = "cốp rộng",
                ["tiet kiem xang"] = "tiết kiệm xăng",
                ["di lam"] = "đi làm",
                ["di hoc"] = "đi học",
                ["di pho"] = "đi phố",
                ["nu"] = "nữ",
                ["nu tinh"] = "nữ tính",
                ["nam tinh"] = "nam tính",

                ["khonagr"] = "khoảng",
                ["khoangr"] = "khoảng",
                ["khoarng"] = "khoảng",
                ["khoanrg"] = "khoảng",
                ["khoang"] = "khoảng",

                ["khoong"] = "không",
                ["khongg"] = "không",
                ["khôg"] = "không",
                ["khog"] = "không",
                ["hong"] = "không",
                ["hông"] = "không",
                ["hem"] = "không",
                ["hok"] = "không",

                ["thik"] = "thích",
                ["thix"] = "thích",
                ["thichh"] = "thích",

                ["khoong thich"] = "không thích",
                ["khongg thich"] = "không thích",
                ["khôg thích"] = "không thích",
                ["hong thich"] = "không thích",
                ["hong thích"] = "không thích",
                ["hông thích"] = "không thích",
                ["ko thich"] = "không thích",
                ["k thich"] = "không thích",
                ["kg"] = "không",
                ["khum"] = "không",
                ["khong muon"] = "không muốn",
                ["khoong muon"] = "không muốn",
                ["hong muon"] = "không muốn",
                ["ko muon"] = "không muốn",
                ["k muon"] = "không muốn",

                ["tư ván"] = "tư vấn",
                ["tu vans"] = "tư vấn",
                ["tu ván"] = "tư vấn",
                ["tuvan"] = "tư vấn",
                ["goi y"] = "gợi ý",
                ["goi ys"] = "gợi ý",
                ["phu hp"] = "phù hợp",
                ["phù hp"] = "phù hợp",
                ["phu hơp"] = "phù hợp",
                ["xee"] = "xe",
                ["khoarng"] = "khoảng",
                ["khoan"] = "khoảng",
                ["tamf"] = "tầm",
                ["namm"] = "nam",
                ["nux"] = "nữ",
                ["nuw"] = "nữ"
            };

        private static readonly HashSet<string> HighRiskTokens =
     new(StringComparer.OrdinalIgnoreCase)
     {
        "vison",
        "visionn",
        "air blaed",
        "ab",
        "honad",
        "yahama",
        "snh",
        "vin",
        "tư ván",
        "tu ván",
        "tu vans",
        "tuvan",
        "goi y",
        "phu hp",
        "phù hp",
        "phu hơp"
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

            normalized = Regex.Replace(
                normalized,
                @"\b(\d+(?:[.,]\d+)?)\s*(trieu|triêu|trieuj|trieeuj|tr|cu|củ|chai)\b",
                "$1 triệu",
                RegexOptions.IgnoreCase);

  
            normalized = Regex.Replace(
                normalized,
                @"\b(\d+(?:[.,]\d+)?)(trieu|triêu|tr|cu|củ|chai)\b",
                "$1 triệu",
                RegexOptions.IgnoreCase);

            normalized = Regex.Replace(
                normalized,
                @"(?<!\d)m(\d{2})(?!\d)",
                "1m$1",
                RegexOptions.IgnoreCase);

            normalized = Regex.Replace(
                normalized,
                @"\b1m(\d)\b",
                "1m$10",
                RegexOptions.IgnoreCase);

            foreach (var kvp in ReplacementMap
                         .Where(x => x.Key.Contains(' '))
                         .OrderByDescending(x => x.Key.Length))
            {
                normalized = ReplaceWholePhrase(normalized, kvp.Key, kvp.Value);
            }

            var originalWords = normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var changedWords = new List<string>();
            var riskyChangedWords = new List<string>();

            var newWords = originalWords.Select(word =>
            {
                if (ReplacementMap.TryGetValue(word, out var replacement))
                {
                    if (!string.Equals(word, replacement, StringComparison.OrdinalIgnoreCase))
                    {
                        changedWords.Add(word);

                        if (HighRiskTokens.Contains(word))
                        {
                            riskyChangedWords.Add(word);
                        }
                    }

                    return replacement;
                }

                return word;
            }).ToArray();

            normalized = string.Join(' ', newWords);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            normalized = Regex.Replace(
    normalized,
    @"\b(tu|tư)\s*v[aăâ]n[sx]?\b",
    "tư vấn",
    RegexOptions.IgnoreCase);

            normalized = Regex.Replace(
                normalized,
                @"\bgoi\s*y\b",
                "gợi ý",
                RegexOptions.IgnoreCase);

            normalized = Regex.Replace(
                normalized,
                @"\bphu\s*h[opơơp]+\b",
                "phù hợp",
                RegexOptions.IgnoreCase);
            var hasChanges = !string.Equals(original, normalized, StringComparison.OrdinalIgnoreCase);
            var riskyChange = riskyChangedWords.Any();
            var needsConfirmation = hasChanges && riskyChange;

            return new NormalizationResult
            {
                OriginalText = original,
                NormalizedText = normalized,
                HasChanges = hasChanges,
                NeedsConfirmation = needsConfirmation,
                Reason = needsConfirmation
                    ? "Phát hiện từ khóa có thể bị gõ sai hoặc viết tắt có độ mơ hồ cao."
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