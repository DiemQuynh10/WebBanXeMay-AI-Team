using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chatbot.API.Configurations;
using Chatbot.API.Models.Telegram;
using Chatbot.API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Services
{
    public class TelegramService : ITelegramService
    {
        private readonly HttpClient _httpClient;
        private readonly TelegramSettings _settings;

        public TelegramService(HttpClient httpClient, IOptions<TelegramSettings> settings)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
        }

        public async Task SendMessageAsync(long chatId, string text)
        {
            await SendMessageAsync(chatId, text, null, null);
        }

        public async Task SendMessageAsync(long chatId, string text, object? replyMarkup = null, string? parseMode = null)
        {
            var url = $"{_settings.BaseUrl}/bot{_settings.BotToken}/sendMessage";

            var payload = new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = text,
                ReplyMarkup = replyMarkup,
                ParseMode = NormalizeParseMode(parseMode)
            };

            await PostTelegramAsync(url, payload, "Telegram sendMessage error");
        }
        public async Task SendPhotoAsync(long chatId, string photoUrl, string caption, object? replyMarkup = null, string? parseMode = null)
        {
            var url = $"{_settings.BaseUrl}/bot{_settings.BotToken}/sendPhoto";

            var payload = new TelegramSendPhotoRequest
            {
                ChatId = chatId,
                Photo = photoUrl,
                Caption = caption,
                ReplyMarkup = replyMarkup,
                ParseMode = NormalizeParseMode(parseMode)
            };

            await PostTelegramAsync(url, payload, "Telegram sendPhoto error");
        }
        public async Task SendTypingAsync(long chatId)
        {
            var url = $"{_settings.BaseUrl}/bot{_settings.BotToken}/sendChatAction";

            var payload = new
            {
                chat_id = chatId,
                action = "typing"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Telegram sendChatAction error: {(int)response.StatusCode} - {responseBody}");
            }
        }

        public async Task SetWebhookAsync(string webhookUrl, string secretToken)
        {
            var url = $"{_settings.BaseUrl}/bot{_settings.BotToken}/setWebhook";

            var payload = new
            {
                url = webhookUrl,
                secret_token = secretToken
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Telegram setWebhook error: {(int)response.StatusCode} - {responseBody}");
            }
        }
        private static string? NormalizeParseMode(string? parseMode)
        {
            if (string.IsNullOrWhiteSpace(parseMode))
                return null;

            var normalized = parseMode.Trim();

            return normalized is "Markdown" or "MarkdownV2" or "HTML"
                ? normalized
                : null;
        }
        private async Task PostTelegramAsync(string url, object payload, string errorPrefix)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(payload, jsonOptions);
            Console.WriteLine("TELEGRAM OUTGOING PAYLOAD:");
            Console.WriteLine(json);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"TELEGRAM RESPONSE: {(int)response.StatusCode} - {responseBody}");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"{errorPrefix}: {(int)response.StatusCode} - {responseBody}");
            }
        }
    }
    public class TelegramSendPhotoRequest
    {
        [JsonPropertyName("chat_id")]
        public long ChatId { get; set; }

        [JsonPropertyName("photo")]
        public string Photo { get; set; } = string.Empty;

        [JsonPropertyName("caption")]
        public string? Caption { get; set; }

        [JsonPropertyName("reply_markup")]
        public object? ReplyMarkup { get; set; }

        [JsonPropertyName("parse_mode")]
        public string? ParseMode { get; set; }
    }
}