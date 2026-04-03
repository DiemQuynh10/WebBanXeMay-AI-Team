namespace Chatbot.API.Models.Requests
{
    public class ChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? ConversationId {  get; set; }
        public string? UserId { get; set; }
        public string? Channel { get; set; } = "web";
        public string? Phone { get; set; }
    }
}