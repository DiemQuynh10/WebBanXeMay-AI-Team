namespace Chatbot.API.Models.Responses
{
    public class ChatProductCard
    {
        public int Id { get; set; }
        public string Ten { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
        public string? CC { get; set; }
        public string? ImageUrl { get; set; }
        public string? ThuongHieu { get; set; }
        public string? Loai { get; set; }
    }

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

        public List<ChatProductCard>? Products { get; set; }
    }
}