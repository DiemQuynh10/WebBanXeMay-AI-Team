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

            string? validParseMode = null;

            if (!string.IsNullOrWhiteSpace(parseMode))
            {
                var normalized = parseMode.Trim();

                if (normalized == "Markdown" || normalized == "MarkdownV2" || normalized == "HTML")
                {
                    validParseMode = normalized;
                }
            }

            var payload = new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = text,
                ReplyMarkup = replyMarkup,
                ParseMode = validParseMode
            };

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
                throw new Exception($"Telegram sendMessage error: {(int)response.StatusCode} - {responseBody}");
            }
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
    }
}