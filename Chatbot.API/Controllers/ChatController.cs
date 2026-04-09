using Chatbot.API.Models.Requests;
using Chatbot.API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.API.Controllers
{
    [ApiController]
    [Route("api/chat")]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;
        private readonly IConversationMemoryService _memoryService;
        private readonly IClarificationStateService _clarificationStateService;
        private readonly IConversationHistoryService _historyService;
        private readonly ILogger<ChatController> _logger;
        private readonly IConversationPreferenceService _conversationPreferenceService;

        public ChatController(
    IChatService chatService,
    IConversationMemoryService memoryService,
    IClarificationStateService clarificationStateService,
    IConversationHistoryService historyService,
    IConversationPreferenceService conversationPreferenceService,
    ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _memoryService = memoryService;
            _clarificationStateService = clarificationStateService;
            _historyService = historyService;
            _conversationPreferenceService = conversationPreferenceService;
            _logger = logger;
        }
        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new
                    {
                        success = false,
                        errorMessage = "Request không hợp lệ."
                    });
                }

                var result = await _chatService.ProcessMessageAsync(request);

                if (result.Success
                    && !string.IsNullOrWhiteSpace(request.Message)
                    && !string.IsNullOrWhiteSpace(result.Reply)
                    && !string.IsNullOrWhiteSpace(result.ConversationId))
                {
                    await _historyService.SaveExchangeAsync(
                        result.ConversationId!,
                        request.Channel,
                        request.UserId,
                        request.Message.Trim(),
                        result.Reply.Trim());
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while processing chat request.");

                return StatusCode(500, new
                {
                    success = false,
                    reply = "Xin lỗi, chatbot đang gặp lỗi tạm thời.",
                    errorMessage = "Internal server error",
                    conversationId = request?.ConversationId
                });
            }
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations([FromQuery] string userId, [FromQuery] string channel = "web")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        errorMessage = "userId không được để trống."
                    });
                }

                var normalizedChannel = string.IsNullOrWhiteSpace(channel) ? "web" : channel.Trim();
                var result = await _historyService.GetConversationsAsync(userId.Trim(), normalizedChannel);
                return Ok(new
                {
                    success = true,
                    items = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while getting conversations. UserId: {UserId}, Channel: {Channel}", userId, channel);

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Không thể tải danh sách hội thoại."
                });
            }
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> GetMessages(string conversationId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        errorMessage = "conversationId không được để trống."
                    });
                }

                var items = await _historyService.GetMessagesAsync(conversationId.Trim());

                return Ok(new
                {
                    success = true,
                    items = items
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while getting messages. ConversationId: {ConversationId}", conversationId);

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Không thể tải lịch sử hội thoại."
                });
            }
        }

        [HttpDelete("conversations/{conversationId}")]
        public async Task<IActionResult> DeleteConversation(string conversationId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(conversationId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        errorMessage = "conversationId không được để trống."
                    });
                }
                var normalizedId = conversationId.Trim();
                await _historyService.DeleteConversationAsync(conversationId.Trim());
                _clarificationStateService.Clear(conversationId.Trim());
                await _conversationPreferenceService.ClearAsync(normalizedId);
                return Ok(new
                {
                    success = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting conversation. ConversationId: {ConversationId}", conversationId);

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Không thể xóa hội thoại."
                });
            }
        }

        [HttpPost("reset")]
        public async Task<IActionResult> ResetConversation([FromBody] ResetConversationRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request?.ConversationId))
                {
                    return BadRequest(new
                    {
                        success = false,
                        errorMessage = "ConversationId không được để trống."
                    });
                }

                var conversationId = request.ConversationId.Trim();

                await _memoryService.ClearAsync(conversationId);
                _clarificationStateService.Clear(conversationId);

                await _conversationPreferenceService.ClearAsync(conversationId);
                _logger.LogInformation("Conversation reset. ConversationId: {ConversationId}", conversationId);

                return Ok(new
                {
                    success = true,
                    conversationId = conversationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resetting conversation. ConversationId: {ConversationId}", request?.ConversationId);

                return StatusCode(500, new
                {
                    success = false,
                    errorMessage = "Không thể reset hội thoại.",
                    conversationId = request?.ConversationId
                });
            }
        }
    }
}