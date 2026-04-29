namespace WebBanXeMay.Models.ViewModels
{
    public class AiChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId { get; set; }
        public string? UserId { get; set; }
        public string? Channel { get; set; }

        public bool IsAuthenticated { get; set; }
    }
}