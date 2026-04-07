using System.Text.Json.Serialization;

namespace Chatbot.API.Models.Telegram
{
    public class TelegramUpdate
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }

        [JsonPropertyName("message")]
        public TelegramMessage? Message { get; set; }
    }
}