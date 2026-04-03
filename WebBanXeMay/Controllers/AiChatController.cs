using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebBanXeMay.Models.ViewModels;

namespace WebBanXeMay.Controllers
{
    [Route("ai-chat")]
    public class AiChatController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiChatController> _logger;

        public AiChatController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AiChatController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] AiChatRequest request)
        {
            if (request == null)
            {
                return BadRequest(new AiChatResponse
                {
                    Success = false,
                    ErrorMessage = "Request không hợp lệ."
                });
            }

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new AiChatResponse
                {
                    Success = false,
                    ErrorMessage = "Tin nhắn không được để trống."
                });
            }

            request.Message = request.Message.Trim();

            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                request.ConversationId = request.ConversationId.Trim();
            }

            var chatbotApiBaseUrl = _configuration["ChatbotApi:BaseUrl"];
            if (string.IsNullOrWhiteSpace(chatbotApiBaseUrl))
            {
                return StatusCode(500, new AiChatResponse
                {
                    Success = false,
                    ErrorMessage = "Chưa cấu hình ChatbotApi:BaseUrl."
                });
            }

            var client = _httpClientFactory.CreateClient();

            var payload = new
            {
                message = request.Message,
                conversationId = request.ConversationId,
                channel = "web"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                _logger.LogInformation(
                    "Forwarding chat request to Chatbot.API. ConversationId: {ConversationId}",
                    request.ConversationId);

                var response = await client.PostAsync($"{chatbotApiBaseUrl}/api/chat", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Chatbot.API returned error. StatusCode: {StatusCode}, Body: {Body}",
                        response.StatusCode,
                        responseBody);

                    return StatusCode((int)response.StatusCode, new AiChatResponse
                    {
                        Success = false,
                        ErrorMessage = $"Chatbot API lỗi: {responseBody}"
                    });
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<AiChatResponse>(responseBody, options);

                if (result == null)
                {
                    return StatusCode(500, new AiChatResponse
                    {
                        Success = false,
                        ErrorMessage = "Không đọc được dữ liệu trả về từ chatbot."
                    });
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Chatbot.API /api/chat");

                return StatusCode(500, new AiChatResponse
                {
                    Success = false,
                    ErrorMessage = "Lỗi khi gọi Chatbot API: " + ex.Message
                });
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset([FromBody] AiChatResetRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ConversationId))
            {
                return BadRequest(new
                {
                    success = false,
                    errorMessage = "ConversationId không được để trống."
                });
            }

            request.ConversationId = request.ConversationId.Trim();

            var chatbotApiBaseUrl = _configuration["ChatbotApi:BaseUrl"];
            if (string.IsNullOrWhiteSpace(chatbotApiBaseUrl))
            {
                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Chưa cấu hình ChatbotApi:BaseUrl."
                });
            }

            var client = _httpClientFactory.CreateClient();

            var payload = new
            {
                conversationId = request.ConversationId
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                _logger.LogInformation(
                    "Forwarding reset request to Chatbot.API. ConversationId: {ConversationId}",
                    request.ConversationId);

                var response = await client.PostAsync($"{chatbotApiBaseUrl}/api/chat/reset", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Chatbot.API reset returned error. StatusCode: {StatusCode}, Body: {Body}",
                        response.StatusCode,
                        responseBody);

                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        errorMessage = $"Chatbot API reset lỗi: {responseBody}"
                    });
                }

                return Content(responseBody, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Chatbot.API /api/chat/reset");

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Lỗi khi reset Chatbot API: " + ex.Message
                });
            }
        }
    }

    public class AiChatResetRequest
    {
        public string? ConversationId { get; set; }
    }
}