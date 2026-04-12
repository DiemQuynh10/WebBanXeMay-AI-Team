using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Conversation
{
    public static class LookupConversationRules
    {
        public static bool LooksLikeDirectProductLookup(string message, ParsedIntent parsedIntent)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasLookupKeyword =
                text.Contains("giá") ||
                text.Contains("gia") ||
                text.Contains("bao nhiêu") ||
                text.Contains("bao nhieu") ||
                text.Contains("còn hàng") ||
                text.Contains("con hang") ||
                text.Contains("tồn kho") ||
                text.Contains("ton kho") ||
                text.Contains("có sẵn") ||
                text.Contains("co san") ||
                text.Contains("còn mấy chiếc") ||
                text.Contains("con may chiec") ||
                text.Contains("bao nhiêu chiếc") ||
                text.Contains("bao nhieu chiec");

            bool hasMentionedProduct =
                parsedIntent.MentionedProducts != null &&
                parsedIntent.MentionedProducts.Count > 0;

            bool looksLikeSpecificModelPhrase =
                Regex.IsMatch(
                    text,
                    @"\b(vision|air blade|freego|latte|grande|vario|future|wave|sirius|impulse|burgman|zip|attila)\b",
                    RegexOptions.IgnoreCase);

            return hasLookupKeyword && (hasMentionedProduct || looksLikeSpecificModelPhrase);
        }

        public static bool LooksLikeLookupFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            return
                text.Contains("còn hàng") ||
                text.Contains("con hang") ||
                text.Contains("còn không") ||
                text.Contains("con khong") ||
                text.Contains("còn không vậy") ||
                text.Contains("con khong vay") ||
                text.Contains("còn ko") ||
                text.Contains("con ko") ||
                text.Contains("hết hàng") ||
                text.Contains("het hang") ||
                text.Contains("tồn kho") ||
                text.Contains("ton kho") ||
                text.Contains("còn mấy chiếc") ||
                text.Contains("con may chiec") ||
                text.Contains("bao nhiêu chiếc") ||
                text.Contains("bao nhieu chiec") ||
                text.Contains("số lượng còn") ||
                text.Contains("so luong con");
        }
    }
}