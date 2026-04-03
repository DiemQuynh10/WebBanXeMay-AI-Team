namespace Chatbot.API.Models.Rag
{
    public class RagQueryResponse
    {
        public bool Success { get; set; }
        public string Context { get; set; } = string.Empty;
        public List<string> Chunks { get; set; } = new();
    }
}