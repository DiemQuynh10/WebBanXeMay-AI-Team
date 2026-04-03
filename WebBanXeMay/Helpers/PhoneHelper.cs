using System.Text;

namespace WebBanXeMay.Helpers
{
    public static class PhoneHelper
    {
        public static string Normalize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            var sb = new StringBuilder();
            foreach (var ch in input)
                if (char.IsDigit(ch)) sb.Append(ch);

            var s = sb.ToString();

            // +84xxxx -> 0xxxx (chuẩn VN, tuỳ bạn)
            if (s.StartsWith("84") && s.Length >= 10)
                s = "0" + s.Substring(2);

            return s;
        }
    }
}