using System.Text.Json.Serialization;

namespace Chatbot.API.Models.Telegram
{
    public class TelegramSendMessageRequest
    {
        [JsonPropertyName("chat_id")]
        public long ChatId { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("parse_mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ParseMode { get; set; }

        [JsonPropertyName("reply_markup")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? ReplyMarkup { get; set; }
    }
}