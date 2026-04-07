using System.Text.Json.Serialization;

namespace Chatbot.API.Models.Telegram
{
    public class TelegramChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}