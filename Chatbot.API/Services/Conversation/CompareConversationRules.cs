using System.Text.RegularExpressions;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Services.Conversation
{
    public static class CompareConversationRules
    {
        public static bool LooksLikeDirectCompareRequest(
            string message,
            ParsedIntent parsedIntent)
        {
            if (string.IsNullOrWhiteSpace(message) || parsedIntent == null)
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasTwoMentionedProducts =
                parsedIntent.MentionedProducts != null &&
                parsedIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() >= 2;

            bool hasCompareConnector =
    text.Contains(" với ") ||
    text.Contains(" va ") ||
    text.Contains(" và ") ||
    text.Contains("so sánh") ||
    text.Contains("so sanh") ||
    text.Contains(" hay ");

            bool hasCompareQuestionTone =
     text.Contains("nào hơn") ||
     text.Contains("nao hon") ||
     text.Contains("hơn") ||
     text.Contains("hon") ||

     text.Contains("hợp nữ") ||
     text.Contains("hop nu") ||
     text.Contains("hợp nữ hơn") ||
     text.Contains("hop nu hon") ||
     text.Contains("phù hợp cho nữ") ||
     text.Contains("phu hop cho nu") ||
     text.Contains("nữ tính hơn") ||
     text.Contains("nu tinh hon") ||

     text.Contains("cốp rộng") ||
     text.Contains("cop rong") ||
     text.Contains("dễ chống chân") ||
     text.Contains("de chong chan") ||
     text.Contains("tiết kiệm xăng") ||
     text.Contains("tiet kiem xang") ||

     text.Contains("đẹp hơn") ||
     text.Contains("dep hon") ||
     text.Contains("thanh lịch hơn") ||
     text.Contains("thanh lich hon") ||
     text.Contains("êm hơn") ||
     text.Contains("em hon") ||

     text.Contains("giá bao nhiêu") ||
     text.Contains("gia bao nhieu") ||
     text.Contains("mức giá");

            return hasTwoMentionedProducts && (hasCompareConnector || hasCompareQuestionTone);
        }

        public static bool LooksLikeOrphanCompareFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasCompareSubject =
                text.Contains("con nào") ||
                text.Contains("xe nào") ||
                text.Contains("mẫu nào");

            bool hasCompareFeature =
    text.Contains("cốp rộng hơn") ||
    text.Contains("cop rong hon") ||
    text.Contains("dễ chống chân hơn") ||
    text.Contains("de chong chan hon") ||
    text.Contains("hợp nữ hơn") ||
    text.Contains("hop nu hon") ||
    text.Contains("hợp nữ") ||
    text.Contains("hop nu") ||
    text.Contains("phù hợp cho nữ") ||
    text.Contains("phu hop cho nu") ||
    text.Contains("nữ tính hơn") ||
    text.Contains("nu tinh hon") ||
    text.Contains("tiết kiệm xăng hơn") ||
    text.Contains("tiet kiem xang hon") ||
    text.Contains("đẹp hơn") ||
    text.Contains("dep hon") ||
    text.Contains("thanh lịch hơn") ||
    text.Contains("thanh lich hon");

            return hasCompareSubject && hasCompareFeature;
        }

        public static bool IsCompareFeatureFollowUpQuestion(
    ParsedIntent parsedIntent,
    CustomerPreferenceProfile? profile,
    string message)
        {
            if (parsedIntent == null || profile == null || string.IsNullOrWhiteSpace(message))
                return false;

            if (!profile.HasActiveCompareContext || profile.LastComparedProducts.Count < 2)
                return false;
            if (LooksLikeAlternativeRecommendationRequest(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool hasCompareFeatureOrComparePrice =
     !string.IsNullOrWhiteSpace(parsedIntent.ComparisonFeature) ||

     text.Contains("cốp rộng") ||
     text.Contains("cop rong") ||
     text.Contains("dễ chống chân") ||
     text.Contains("de chong chan") ||
     text.Contains("tiết kiệm xăng") ||
     text.Contains("tiet kiem xang") ||

     text.Contains("hợp nữ") ||
     text.Contains("hop nu") ||
     text.Contains("hợp nữ hơn") ||
     text.Contains("hop nu hon") ||
     text.Contains("phù hợp cho nữ") ||
     text.Contains("phu hop cho nu") ||
     text.Contains("nữ tính hơn") ||
     text.Contains("nu tinh hon") ||

     text.Contains("đẹp hơn") ||
     text.Contains("dep hon") ||
     text.Contains("thanh lịch hơn") ||
     text.Contains("thanh lich hon") ||
     text.Contains("êm hơn") ||
     text.Contains("em hon") ||

     text.Contains("giá") ||
     text.Contains("gia") ||
     text.Contains("bao nhiêu") ||
     text.Contains("bao nhieu") ||
     text.Contains("mức giá") ||
     text.Contains("bao nhiêu tiền") ||
     text.Contains("bao nhieu tien");

            bool doesNotNameTwoNewProducts =
                parsedIntent.MentionedProducts == null ||
                parsedIntent.MentionedProducts
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() < 2;

            return hasCompareFeatureOrComparePrice && doesNotNameTwoNewProducts;
        }
        private static bool LooksLikeAlternativeRecommendationRequest(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = message.Trim().ToLowerInvariant();

            bool asksOtherOption =
                text.Contains("còn xe nào") ||
                text.Contains("con xe nao") ||
                text.Contains("xe nào") ||
                text.Contains("xe nao") ||
                text.Contains("mẫu nào") ||
                text.Contains("mau nao") ||
                text.Contains("con nào") ||
                text.Contains("con nao") ||
                text.Contains("xe khác") ||
                text.Contains("xe khac") ||
                text.Contains("mẫu khác") ||
                text.Contains("mau khac");

            bool asksCheaper =
                text.Contains("rẻ hơn") ||
                text.Contains("re hon") ||
                text.Contains("mềm hơn") ||
                text.Contains("mem hon") ||
                text.Contains("thấp hơn") ||
                text.Contains("thap hon") ||
                text.Contains("ít tiền hơn") ||
                text.Contains("it tien hon");

            return asksOtherOption && asksCheaper;
        }
    }
}