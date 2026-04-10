using System.Text;
using System.Text.RegularExpressions;

namespace Chatbot.API.Services
{
    public interface IInputTextSanitizer
    {
        string Sanitize(string? input);
    }

    public class InputTextSanitizer : IInputTextSanitizer
    {
        public string Sanitize(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var text = input.Trim();

            // Chuẩn hóa Unicode trước
            text = text.Normalize(NormalizationForm.FormC);

            // Sửa các mẫu vỡ dấu thường gặp theo dạng "khung từ"
            // Không phụ thuộc ký tự lỗi cụ thể là gì
            text = RepairBrokenVietnamese(text);

            // Gom khoảng trắng
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        private static string RepairBrokenVietnamese(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Các từ xuất hiện thật trong log runtime của bạn
            text = Regex.Replace(text, @"\bt\S?\b", "từ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bd\S?n\b", "đến", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\btri\S?u\b", "triệu", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bkho\S?ng\b", "khoảng", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bt\S?m\b", "tầm", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bd\S?\S?i\b", "dưới", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\btr\S?n\b", "trên", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bv\S?n\b", "vấn", RegexOptions.IgnoreCase);

            return text;
        }
    }
}