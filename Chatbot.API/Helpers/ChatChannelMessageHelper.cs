namespace Chatbot.API.Helpers
{
    public static class ChatChannelMessageHelper
    {
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
    }
}