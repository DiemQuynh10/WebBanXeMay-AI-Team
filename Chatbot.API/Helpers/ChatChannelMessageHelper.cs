using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Chatbot.API.Helpers
{
    public static class ChatChannelMessageHelper
    {
        private static readonly Regex MarkdownBoldRegex = new(
            @"(\*\*|__)([^\r\n]+?)\1",
            RegexOptions.Compiled);

        private static readonly Regex CompareRowRegex = new(
            @"^\s*-\s*(?:\*\*|__)?(?<name>.+?)(?:\*\*|__)?\s*:\s*giá\s*(?<price>[\d\.,]+)\s*VNĐ(?:,\s*còn\s*(?<stock>\d+)\s*chiếc)?(?:,\s*hãng\s*(?<brand>[^,\.]+))?(?:,\s*(?:loại|thuộc nhóm)\s*(?<category>[^,\.]+))?(?:,\s*(?<cc>[^,\.]+))?\.?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool TryGetStaticCommandReply(string? rawMessage, out string reply)
        {
            reply = string.Empty;

            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return false;
            }

            var message = rawMessage.Trim();

            if (message.Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                reply = """
Xin chào 👋
Mình là bot hỗ trợ tư vấn xe máy.

Mình có thể giúp bạn:
- Tư vấn chọn xe theo nhu cầu
- Gợi ý xe theo ngân sách
- Tra cứu giá xe
- Kiểm tra mẫu phù hợp

Bạn có thể chọn nhanh bằng menu bên dưới hoặc nhắn tự nhiên như:
- xe ga cho sinh viên
- xe cho nữ dưới 40 triệu
- air blade giá bao nhiêu
""";
                return true;
            }

            if (message.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                reply = """
Bạn có thể hỏi mình theo các cách sau:

- Tư vấn xe cho nữ tầm 35 triệu
- Xe ga nào hợp đi học
- Honda Vision giá bao nhiêu
- Xe nào phù hợp đi làm
- So sánh Vision và Janus

Gõ /menu để hiện lại menu nhanh.
""";
                return true;
            }

            if (message.Equals("/menu", StringComparison.OrdinalIgnoreCase))
            {
                reply = "Đây là menu nhanh, bạn chọn nội dung muốn tra cứu nhé.";
                return true;
            }

            return false;
        }

        public static string NormalizeQuickMenuInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalized = input.Trim();

            return normalized switch
            {
                "Tư vấn xe" => "Tư vấn xe máy phù hợp cho tôi",
                "Xe ga" => "Gợi ý các mẫu xe ga phù hợp",
                "Xe số" => "Gợi ý các mẫu xe số phù hợp",
                "Xe cho nữ" => "Tư vấn xe máy phù hợp cho nữ",
                "Dưới 40 triệu" => "Tư vấn xe máy dưới 40 triệu",
                "Kiểm tra giá xe" => "Cho tôi biết giá các mẫu xe nổi bật",
                _ => normalized
            };
        }

        public static string FormatReply(string? text, string fallbackReply)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallbackReply;
            }

            return text.Replace("\r\n", "\n").Trim();
        }

        public static string FormatTelegramReply(string? text, string fallbackReply)
        {
            var normalized = FormatReply(text, fallbackReply);

            if (TryBuildTelegramCompareTable(normalized, out var compareTableHtml))
            {
                return compareTableHtml;
            }

            return ConvertMarkdownToTelegramHtml(normalized);
        }

        private static bool TryBuildTelegramCompareTable(string text, out string formattedHtml)
        {
            formattedHtml = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Replace("\r\n", "\n").Split('\n');

            if (TryBuildTelegramCompareCardsFromMarkdownTable(lines, out formattedHtml))
            {
                return true;
            }

            var rows = new List<(int Index, string Name, string Brand, string Category, string Cc, string Price, string Stock)>();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var match = CompareRowRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups["name"].Value.Trim();
                var price = match.Groups["price"].Value.Trim();
                var stock = match.Groups["stock"].Success
                    ? match.Groups["stock"].Value.Trim()
                    : "-";
                var brand = match.Groups["brand"].Success
                    ? match.Groups["brand"].Value.Trim()
                    : "-";
                var category = match.Groups["category"].Success
                    ? match.Groups["category"].Value.Trim()
                    : "-";
                var cc = match.Groups["cc"].Success
                    ? match.Groups["cc"].Value.Trim()
                    : "-";

                if (cc.EndsWith("cc", StringComparison.OrdinalIgnoreCase))
                {
                    cc = cc[..^2].Trim();
                }

                rows.Add((i, name, brand, category, cc, price, stock));
            }

            if (rows.Count < 2)
            {
                return false;
            }

            var firstRowIndex = rows.Min(x => x.Index);
            var lastRowIndex = rows.Max(x => x.Index);

            var introLine = lines
                .Take(firstRowIndex)
                .Select(x => x.Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            var tailLines = lines
                .Skip(lastRowIndex + 1)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var tableText = BuildMonospaceCompareTable(rows);
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(introLine))
            {
                sb.AppendLine(ConvertMarkdownToTelegramHtml(introLine));
            }

            sb.Append("<pre>");
            sb.Append(WebUtility.HtmlEncode(tableText));
            sb.AppendLine("</pre>");

            if (tailLines.Count > 0)
            {
                sb.AppendLine();
                sb.Append(ConvertMarkdownToTelegramHtml(string.Join("\n", tailLines)));
            }

            formattedHtml = sb.ToString().Trim();
            return true;
        }

        private static bool TryBuildTelegramCompareCardsFromMarkdownTable(string[] lines, out string formattedHtml)
        {
            formattedHtml = string.Empty;

            var headerIndex = -1;
            for (var i = 0; i < lines.Length - 1; i++)
            {
                var current = lines[i].Trim();
                var next = lines[i + 1].Trim();

                if (IsMarkdownTableRow(current) && IsMarkdownTableSeparator(next))
                {
                    headerIndex = i;
                    break;
                }
            }

            if (headerIndex < 0)
            {
                return false;
            }

            var headers = SplitMarkdownTableRow(lines[headerIndex]);
            var bodyLines = new List<string>();
            var cursor = headerIndex + 2;

            while (cursor < lines.Length)
            {
                var line = lines[cursor].Trim();
                if (!IsMarkdownTableRow(line) || IsMarkdownTableSeparator(line))
                {
                    break;
                }

                bodyLines.Add(line);
                cursor++;
            }

            if (headers.Count == 0 || bodyLines.Count < 2)
            {
                return false;
            }

            var rows = bodyLines
                .Select(row => SplitMarkdownTableRow(row))
                .Where(cells => cells.Count > 0)
                .Select(cells => BuildTelegramCompareRow(headers, cells))
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .ToList();

            if (rows.Count < 2)
            {
                return false;
            }

            var introLines = lines
                .Take(headerIndex)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var tailLines = lines
                .Skip(cursor)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var sb = new StringBuilder();

            if (introLines.Count > 0)
            {
                sb.AppendLine(ConvertMarkdownToTelegramHtml(string.Join("\n", introLines)));
                sb.AppendLine();
            }

            sb.AppendLine("<b>📊 Bảng so sánh dễ nhìn</b>");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                sb.AppendLine();
                sb.AppendLine($"<b>{i + 1}. {WebUtility.HtmlEncode(row.Name)}</b>");
                AppendTelegramField(sb, "🏷 Hãng", row.Brand);
                AppendTelegramField(sb, "🛵 Loại", row.Category);
                AppendTelegramField(sb, "💰 Giá", row.Price);
                AppendTelegramField(sb, "⚙️ Dung tích", row.Cc);
                AppendTelegramField(sb, "📦 Tồn kho", row.Stock);
                AppendTelegramField(sb, "⭐ Nổi bật", row.Highlight);
            }

            if (tailLines.Count > 0)
            {
                sb.AppendLine();
                sb.Append(ConvertMarkdownToTelegramHtml(string.Join("\n", tailLines)));
            }

            formattedHtml = sb.ToString().Trim();
            return true;
        }

        private static bool IsMarkdownTableRow(string line)
        {
            return !string.IsNullOrWhiteSpace(line)
                && line.TrimStart().StartsWith("|", StringComparison.Ordinal)
                && line.TrimEnd().EndsWith("|", StringComparison.Ordinal);
        }

        private static bool IsMarkdownTableSeparator(string line)
        {
            if (!IsMarkdownTableRow(line))
            {
                return false;
            }

            var cells = SplitMarkdownTableRow(line);
            return cells.Count > 0 && cells.All(cell =>
            {
                var value = cell.Trim();
                return Regex.IsMatch(value, @"^:?-{3,}:?$");
            });
        }

        private static List<string> SplitMarkdownTableRow(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            if (trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^1];
            }

            return trimmed
                .Split('|')
                .Select(cell => cell.Trim())
                .ToList();
        }

        private static TelegramCompareRow BuildTelegramCompareRow(IReadOnlyList<string> headers, IReadOnlyList<string> cells)
        {
            string GetCell(params string[] aliases)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var normalizedHeader = NormalizeHeader(headers[i]);
                    if (aliases.Any(alias => normalizedHeader.Contains(alias, StringComparison.OrdinalIgnoreCase)))
                    {
                        return i < cells.Count ? StripMarkdown(cells[i]) : string.Empty;
                    }
                }

                return string.Empty;
            }

            return new TelegramCompareRow(
                Name: GetCell("xe", "mau"),
                Brand: GetCell("hang", "thuong hieu"),
                Category: GetCell("loai"),
                Price: GetCell("gia"),
                Cc: GetCell("dung tich", "cc"),
                Stock: GetCell("ton", "con"),
                Highlight: GetCell("noi bat", "diem"));
        }

        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd');
        }

        private static string StripMarkdown(string value)
        {
            return (value ?? string.Empty)
                .Replace("**", string.Empty)
                .Replace("__", string.Empty)
                .Replace("`", string.Empty)
                .Trim();
        }

        private static void AppendTelegramField(StringBuilder sb, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-")
            {
                return;
            }

            sb.AppendLine($"{label}: {WebUtility.HtmlEncode(value.Trim())}");
        }

        private sealed record TelegramCompareRow(
            string Name,
            string Brand,
            string Category,
            string Price,
            string Cc,
            string Stock,
            string Highlight);

        private static string BuildMonospaceCompareTable(
            List<(int Index, string Name, string Brand, string Category, string Cc, string Price, string Stock)> rows)
        {
            const int maxNameWidth = 28;
            const int maxBrandWidth = 10;
            const int maxCategoryWidth = 12;
            const int maxCcWidth = 6;
            const int maxPriceWidth = 14;
            const int maxStockWidth = 6;

            var nameWidth = Math.Min(
                maxNameWidth,
                Math.Max("Mau xe".Length, rows.Max(x => x.Name.Length)));
            var brandWidth = Math.Min(
                maxBrandWidth,
                Math.Max("Hang".Length, rows.Max(x => x.Brand.Length)));
            var categoryWidth = Math.Min(
                maxCategoryWidth,
                Math.Max("Loai".Length, rows.Max(x => x.Category.Length)));
            var ccWidth = Math.Min(
                maxCcWidth,
                Math.Max("CC".Length, rows.Max(x => x.Cc.Length)));
            var priceWidth = Math.Min(
                maxPriceWidth,
                Math.Max("Gia (VND)".Length, rows.Max(x => x.Price.Length)));
            var stockWidth = Math.Min(
                maxStockWidth,
                Math.Max("Ton".Length, rows.Max(x => x.Stock.Length)));

            var sb = new StringBuilder();
            var header =
                $"{PadCell("Mau xe", nameWidth)} | {PadCell("Hang", brandWidth)} | {PadCell("Loai", categoryWidth)} | {PadCell("CC", ccWidth)} | {PadCell("Gia (VND)", priceWidth)} | {PadCell("Ton", stockWidth)}";

            sb.AppendLine(header);
            sb.AppendLine(new string('-', header.Length));

            foreach (var row in rows)
            {
                sb.AppendLine(
                    $"{PadCell(row.Name, nameWidth)} | {PadCell(row.Brand, brandWidth)} | {PadCell(row.Category, categoryWidth)} | {PadCell(row.Cc, ccWidth)} | {PadCell(row.Price, priceWidth)} | {PadCell(row.Stock, stockWidth)}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string PadCell(string value, int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            var cleaned = (value ?? string.Empty).Trim();
            if (cleaned.Length > width)
            {
                cleaned = width <= 3
                    ? cleaned[..width]
                    : cleaned[..(width - 3)] + "...";
            }

            return cleaned.PadRight(width);
        }

        private static string ConvertMarkdownToTelegramHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            // Encode user/model content first, then selectively map simple markdown bold markers.
            var encoded = WebUtility.HtmlEncode(text);
            return MarkdownBoldRegex.Replace(encoded, "<b>$2</b>");
        }
    }
}
