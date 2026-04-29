using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using WebBanXeMay.Models.ViewModels;
using WebBanXeMay.Services;

namespace WebBanXeMay.Controllers
{
    [Route("ai-chat")]
    public class AiChatController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiChatController> _logger;
        private readonly IInputTextSanitizer _inputTextSanitizer;
        public AiChatController(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<AiChatController> logger,
    IInputTextSanitizer inputTextSanitizer)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _inputTextSanitizer = inputTextSanitizer;
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

            request.Message = _inputTextSanitizer.Sanitize(request.Message);
            _logger.LogInformation("Sanitized user message before forwarding: {Message}", request.Message);

            if (!string.IsNullOrWhiteSpace(request.ConversationId))
            {
                request.ConversationId = request.ConversationId.Trim();
            }

            var isAuthenticated = User?.Identity?.IsAuthenticated == true;
            var realUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (isAuthenticated && !string.IsNullOrWhiteSpace(realUserId))
            {
                request.UserId = realUserId.Trim();
                request.IsAuthenticated = true;
            }
            else
            {
                request.UserId = string.IsNullOrWhiteSpace(request.UserId)
                    ? null
                    : request.UserId.Trim();

                request.IsAuthenticated = false;
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
                channel = string.IsNullOrWhiteSpace(request.Channel) ? "web" : request.Channel.Trim(),
                isAuthenticated = request.IsAuthenticated
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
        public async Task<IActionResult> GetConversations([FromQuery] string channel = "web")
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated == true;
            var realUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!isAuthenticated || string.IsNullOrWhiteSpace(realUserId))
            {
                return Unauthorized(new
                {
                    success = false,
                    errorMessage = "Bạn cần đăng nhập để xem lịch sử chat."
                });
            }

            var chatbotApiBaseUrl = _configuration["ChatbotApi:BaseUrl"];
            var client = _httpClientFactory.CreateClient();

            var url = $"{chatbotApiBaseUrl}/api/chat/conversations?userId={Uri.EscapeDataString(realUserId)}&channel={Uri.EscapeDataString(channel)}";

            var response = await client.GetAsync(url);
            var responseBody = await response.Content.ReadAsStringAsync();

            return Content(responseBody, "application/json");
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetMessages(string conversationId)
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated == true;
            var realUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!isAuthenticated || string.IsNullOrWhiteSpace(realUserId))
            {
                return Unauthorized(new
                {
                    success = false,
                    errorMessage = "Bạn cần đăng nhập để xem nội dung chat."
                });
            }

            var chatbotApiBaseUrl = _configuration["ChatbotApi:BaseUrl"];
            var client = _httpClientFactory.CreateClient();

            var url = $"{chatbotApiBaseUrl}/api/chat/conversations/{Uri.EscapeDataString(conversationId.Trim())}/messages?userId={Uri.EscapeDataString(realUserId)}";

            var response = await client.GetAsync(url);
            var responseBody = await response.Content.ReadAsStringAsync();

            return Content(responseBody, "application/json");
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

            var isAuthenticated = User?.Identity?.IsAuthenticated == true;
            var realUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!isAuthenticated || string.IsNullOrWhiteSpace(realUserId))
            {
                return Unauthorized(new
                {
                    success = false,
                    errorMessage = "Bạn cần đăng nhập để xóa hội thoại."
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
                var url =
                    $"{chatbotApiBaseUrl}/api/chat/conversations/{Uri.EscapeDataString(conversationId.Trim())}" +
                    $"?userId={Uri.EscapeDataString(realUserId)}";

                var response = await client.DeleteAsync(url);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
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

            var isAuthenticated = User?.Identity?.IsAuthenticated == true;
            var realUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!isAuthenticated || string.IsNullOrWhiteSpace(realUserId))
            {
                return Unauthorized(new
                {
                    success = false,
                    errorMessage = "Bạn cần đăng nhập để reset hội thoại."
                });
            }

            var payload = new
            {
                conversationId = request.ConversationId,
                userId = realUserId
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