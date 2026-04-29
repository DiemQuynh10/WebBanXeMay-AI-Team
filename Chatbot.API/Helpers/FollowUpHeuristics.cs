namespace Chatbot.API.Helpers
{
    public static class FollowUpHeuristics
    {
        public static bool LooksLikeFollowUp(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            var text = Normalize(message);

            return LooksLikeCompareFollowUp(text)
                   || LooksLikeLookupFollowUp(text)
                   || LooksLikeRecommendationFollowUp(text);
        }

        public static bool LooksLikeCompareFollowUp(string message)
        {
            var text = Normalize(message);

            return
                text.Contains("hon") ||
                text.Contains("cai nao") ||
                text.Contains("xe nao") ||
                text.Contains("mau nao") ||
                text.Contains("cop") ||
                text.Contains("hop nu") ||
                text.Contains("de chong chan") ||
                text.Contains("tiet kiem xang") ||
                text.Contains("gia bao nhieu") ||
                text.Contains("re hon") ||
                text.Contains("dat hon");
        }

        public static bool LooksLikeCompareUseCaseFollowUp(string message)
        {
            var text = Normalize(message);

            return
                text.Contains("di lam hop") ||
                text.Contains("di hoc hop") ||
                text.Contains("di pho hop") ||
                text.Contains("di cho hop");
        }

        public static bool LooksLikeLookupFollowUp(string message)
        {
            var text = Normalize(message);

            return
                text.Contains("gia bao nhieu") ||
                text.Contains("con hang") ||
                text.Contains("con khong") ||
                text.Contains("mau gi") ||
                text.Contains("thong so") ||
                text.Contains("tra gop");
        }

        public static bool LooksLikeRecommendationFollowUp(string message)
        {
            var text = Normalize(message);

            return
                text.Contains("con honda thi sao") ||
                text.Contains("xe ga thoi") ||
                text.Contains("hang khac") ||
                text.Contains("tam gia nay") ||
                text.Contains("loai nay") ||
                text.Contains("goi y them");
        }

        public static bool LooksLikeFreshRecommendationRequest(string message)
        {
            var text = Normalize(message);

            return
                text.Contains("tu van") ||
                text.Contains("goi y") ||
                text.Contains("xe cho nu") ||
                text.Contains("xe cho nam") ||
                text.Contains("khoang") ||
                text.Contains("tam") ||
                text.Contains("quanh") ||
                text.Contains("ngan sach") ||
                text.Contains("duoi") ||
                text.Contains("tren");
        }

        public static string Normalize(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return string.Empty;

            var text = message.Trim().ToLowerInvariant();

            text = text
                .Replace("à", "a").Replace("á", "a").Replace("ạ", "a").Replace("ả", "a").Replace("ã", "a")
                .Replace("ă", "a").Replace("ằ", "a").Replace("ắ", "a").Replace("ặ", "a").Replace("ẳ", "a").Replace("ẵ", "a")
                .Replace("â", "a").Replace("ầ", "a").Replace("ấ", "a").Replace("ậ", "a").Replace("ẩ", "a").Replace("ẫ", "a")
                .Replace("è", "e").Replace("é", "e").Replace("ẹ", "e").Replace("ẻ", "e").Replace("ẽ", "e")
                .Replace("ê", "e").Replace("ề", "e").Replace("ế", "e").Replace("ệ", "e").Replace("ể", "e").Replace("ễ", "e")
                .Replace("ì", "i").Replace("í", "i").Replace("ị", "i").Replace("ỉ", "i").Replace("ĩ", "i")
                .Replace("ò", "o").Replace("ó", "o").Replace("ọ", "o").Replace("ỏ", "o").Replace("õ", "o")
                .Replace("ô", "o").Replace("ồ", "o").Replace("ố", "o").Replace("ộ", "o").Replace("ổ", "o").Replace("ỗ", "o")
                .Replace("ơ", "o").Replace("ờ", "o").Replace("ớ", "o").Replace("ợ", "o").Replace("ở", "o").Replace("ỡ", "o")
                .Replace("ù", "u").Replace("ú", "u").Replace("ụ", "u").Replace("ủ", "u").Replace("ũ", "u")
                .Replace("ư", "u").Replace("ừ", "u").Replace("ứ", "u").Replace("ự", "u").Replace("ử", "u").Replace("ữ", "u")
                .Replace("ỳ", "y").Replace("ý", "y").Replace("ỵ", "y").Replace("ỷ", "y").Replace("ỹ", "y")
                .Replace("đ", "d");

            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            return text;
        }
    }
}