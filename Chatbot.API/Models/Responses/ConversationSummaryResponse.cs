namespace Chatbot.API.Models.Responses
{
    public class ConversationSummaryResponse
    {
        public string ConversationId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? LastMessagePreview { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}