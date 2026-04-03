namespace Chatbot.API.Models.Responses
{
    public class ChatResponse
    {
        public bool Success { get; set; }

        public string Reply { get; set; } = string.Empty;

        public string? UsedTool { get; set; }

        public bool UsedAI { get; set; }

        public long? ElapsedMs { get; set; }

        public string? ErrorMessage { get; set; }

        public string? ConversationId { get; set; }

        public object? DebugInfo { get; set; }
    }
}