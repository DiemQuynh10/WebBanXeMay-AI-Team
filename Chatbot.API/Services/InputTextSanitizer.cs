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

            // Chuẩn hóa Unicode để tránh ký tự tổ hợp lạ
            text = text.Normalize(NormalizationForm.FormC);

            // Thay non-breaking space và vài khoảng trắng đặc biệt
            text = text
                .Replace('\u00A0', ' ')
                .Replace('\u2007', ' ')
                .Replace('\u202F', ' ');

            // Gom khoảng trắng
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }
    }
}