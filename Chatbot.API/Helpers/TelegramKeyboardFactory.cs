namespace Chatbot.API.Helpers
{
    public static class TelegramKeyboardFactory
    {
        public static object MainMenu()
        {
            return new
            {
                keyboard = new[]
                {
                    new[] { new { text = "Tư vấn xe" }, new { text = "Xe ga" } },
                    new[] { new { text = "Xe số" }, new { text = "Xe cho nữ" } },
                    new[] { new { text = "Dưới 40 triệu" }, new { text = "Kiểm tra giá xe" } }
                },
                resize_keyboard = true,
                one_time_keyboard = false
            };
        }
    }
}