namespace WebBanXeMay.Areas.Admin.ViewModels
{
    public class UserChatViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Avatar { get; set; } = "/images/avatar-default.png";

        // 🔴 NEW: số tin nhắn khách gửi mà admin chưa đọc
        public int UnreadCount { get; set; }

        // 🔴 NEW: nội dung tin nhắn cuối cùng (để hiện ở sidebar)
        public string? LastMessage { get; set; }

        // 🔴 NEW: thời gian tin nhắn cuối cùng (để sort + hiển thị)
        public DateTime? LastMessageTime { get; set; }
    }
}
