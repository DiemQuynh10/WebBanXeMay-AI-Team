using System.Text.Json.Serialization;

namespace Chatbot.API.Models.Telegram
{
    public class TelegramMessage
    {
        [JsonPropertyName("message_id")]
        public long MessageId { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("chat")]
        public TelegramChat? Chat { get; set; }

        [JsonPropertyName("date")]
        public long Date { get; set; }

        [JsonPropertyName("from")]
        public TelegramUser? From { get; set; }

        [JsonPropertyName("photo")]
        public object[]? Photo { get; set; }

        [JsonPropertyName("document")]
        public object? Document { get; set; }

        [JsonPropertyName("sticker")]
        public object? Sticker { get; set; }
    }
}