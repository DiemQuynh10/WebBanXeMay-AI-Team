namespace WebBanXeMay.Models.ViewModels
{
    public class AiChatResponse
    {
        public bool Success {  get; set; }
        public string Reply { get; set; } = string.Empty;
        public string? UsedTool {  get; set; }
        public string? ErrorMessage {  get; set; }
        public string? ConversationId {  get; set; }
    }
}
