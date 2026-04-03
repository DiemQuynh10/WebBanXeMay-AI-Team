using System.Text.Json.Serialization;

namespace Chatbot.API.Models.Rag
{
    public class RagQueryRequest
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("top_k")]
        public int TopK { get; set; } = 4;
    }
}