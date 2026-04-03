namespace Chatbot.API.Models.Requests
{
    public class AIRequestContext
    {
        public string ConversationId { get; set; } = string.Empty;
        public string Channel { get; set; } = "web";
        public string? UserId { get; set; }

        public string OriginalUserMessage { get; set; } = string.Empty;

        public string EffectivePrompt { get; set; } = string.Empty;

        public string? RagContext { get; set; }
    }
}