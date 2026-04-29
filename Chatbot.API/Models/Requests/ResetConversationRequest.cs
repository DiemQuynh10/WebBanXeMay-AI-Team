namespace Chatbot.API.Models.Requests
{
    public class ResetConversationRequest
    {
        public string? ConversationId { get; set; }
        public string? UserId { get; set; }
    }
}