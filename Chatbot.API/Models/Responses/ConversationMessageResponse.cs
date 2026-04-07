namespace Chatbot.API.Models.Responses
{
    public class ConversationMessageResponse
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }
}