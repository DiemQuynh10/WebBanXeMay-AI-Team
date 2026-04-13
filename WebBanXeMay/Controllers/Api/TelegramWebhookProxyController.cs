using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace WebBanXeMay.Controllers.Api
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramWebhookProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelegramWebhookProxyController> _logger;

        public TelegramWebhookProxyController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<TelegramWebhookProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> ProxyWebhook()
        {
            var chatbotApiBaseUrl = _configuration["ChatbotApi:BaseUrl"];
            if (string.IsNullOrWhiteSpace(chatbotApiBaseUrl))
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = "Missing ChatbotApi:BaseUrl configuration"
                });
            }

            var targetUrl = $"{chatbotApiBaseUrl.TrimEnd('/')}/api/telegram/webhook";

            Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }
            Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                body = "{}";
            }

            var forwardRequest = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var secretHeader)
                && !string.IsNullOrWhiteSpace(secretHeader))
            {
                forwardRequest.Headers.TryAddWithoutValidation(
                    "X-Telegram-Bot-Api-Secret-Token",
                    secretHeader.ToString());
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.SendAsync(forwardRequest);
                var responseBody = await response.Content.ReadAsStringAsync();

                var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

                return new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    ContentType = contentType,
                    Content = responseBody
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to forward Telegram webhook to Chatbot.API");

                return StatusCode(502, new
                {
                    success = false,
                    error = "Webhook forwarding failed"
                });
            }
        }
    }
}
