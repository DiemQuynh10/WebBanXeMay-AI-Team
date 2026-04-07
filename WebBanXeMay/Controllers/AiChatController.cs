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

            if (!string.IsNullOrWhiteSpace(request.UserId))
            {
                request.UserId = request.UserId.Trim();
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
                userId = request.UserId,
                channel = string.IsNullOrWhiteSpace(request.Channel) ? "web" : request.Channel.Trim()
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                _logger.LogInformation(
                    "Forwarding chat request to Chatbot.API. ConversationId: {ConversationId}, UserId: {UserId}, Channel: {Channel}",
                    request.ConversationId,
                    request.UserId,
                    request.Channel ?? "web");

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

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations([FromQuery] string userId, [FromQuery] string channel = "web")
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest(new
                {
                    success = false,
                    errorMessage = "userId không được để trống."
                });
            }

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

            try
            {
                var url = $"{chatbotApiBaseUrl}/api/chat/conversations?userId={Uri.EscapeDataString(userId.Trim())}&channel={Uri.EscapeDataString(channel)}";

                _logger.LogInformation(
                    "Forwarding get conversations request to Chatbot.API. UserId: {UserId}, Channel: {Channel}",
                    userId,
                    channel);

                var response = await client.GetAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Chatbot.API conversations returned error. StatusCode: {StatusCode}, Body: {Body}",
                        response.StatusCode,
                        responseBody);

                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        errorMessage = $"Chatbot API conversations lỗi: {responseBody}"
                    });
                }

                return Content(responseBody, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Chatbot.API /api/chat/conversations");

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Lỗi khi tải danh sách hội thoại: " + ex.Message
                });
            }
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetMessages(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return BadRequest(new
                {
                    success = false,
                    errorMessage = "conversationId không được để trống."
                });
            }

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

            try
            {
                _logger.LogInformation(
                    "Forwarding get messages request to Chatbot.API. ConversationId: {ConversationId}",
                    conversationId);

                var response = await client.GetAsync($"{chatbotApiBaseUrl}/api/chat/conversations/{Uri.EscapeDataString(conversationId.Trim())}/messages");
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Chatbot.API messages returned error. StatusCode: {StatusCode}, Body: {Body}",
                        response.StatusCode,
                        responseBody);

                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        errorMessage = $"Chatbot API messages lỗi: {responseBody}"
                    });
                }

                return Content(responseBody, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Chatbot.API /api/chat/conversations/{ConversationId}/messages", conversationId);

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Lỗi khi tải lịch sử hội thoại: " + ex.Message
                });
            }
        }

        [HttpDelete("conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                return BadRequest(new
                {
                    success = false,
                    errorMessage = "conversationId không được để trống."
                });
            }

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

            try
            {
                _logger.LogInformation(
                    "Forwarding delete conversation request to Chatbot.API. ConversationId: {ConversationId}",
                    conversationId);

                var response = await client.DeleteAsync($"{chatbotApiBaseUrl}/api/chat/conversations/{Uri.EscapeDataString(conversationId.Trim())}");
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Chatbot.API delete conversation returned error. StatusCode: {StatusCode}, Body: {Body}",
                        response.StatusCode,
                        responseBody);

                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        errorMessage = $"Chatbot API delete conversation lỗi: {responseBody}"
                    });
                }

                return Content(responseBody, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Chatbot.API delete conversation");

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Lỗi khi xóa hội thoại: " + ex.Message
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