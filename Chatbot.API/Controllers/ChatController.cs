using Chatbot.API.Helpers;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services;
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
        private readonly IInputTextSanitizer _inputTextSanitizer;
        private readonly IConversationStateService _conversationStateService;
        public ChatController(
    IChatService chatService,
    IConversationMemoryService memoryService,
    IClarificationStateService clarificationStateService,
    IConversationHistoryService historyService,
    IConversationPreferenceService conversationPreferenceService,
    IInputTextSanitizer inputTextSanitizer,
    IConversationStateService conversationStateService,
    ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _memoryService = memoryService;
            _clarificationStateService = clarificationStateService;
            _historyService = historyService;
            _conversationPreferenceService = conversationPreferenceService;
            _inputTextSanitizer = inputTextSanitizer;
            _conversationStateService = conversationStateService;
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

                request.Message = _inputTextSanitizer.Sanitize(request.Message);

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new
                    {
                        success = false,
                        errorMessage = "Tin nhắn không được để trống."
                    });
                }

                if (ChatChannelMessageHelper.TryGetStaticCommandReply(request.Message, out var staticReply))
                {
                    var commandResult = BuildStaticCommandResponse(request, staticReply);
                    await SaveExchangeIfNeededAsync(request, commandResult);
                    return Ok(commandResult);
                }

                request.Message = ChatChannelMessageHelper.NormalizeQuickMenuInput(request.Message);

                var result = await _chatService.ProcessMessageAsync(request);

                result.Reply = ChatChannelMessageHelper.FormatReply(
                    result.Reply,
                    "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé.");

                await SaveExchangeIfNeededAsync(request, result);

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

        private static ChatResponse BuildStaticCommandResponse(ChatRequest request, string reply)
        {
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString()
                : request.ConversationId.Trim();

            request.ConversationId = conversationId;

            return new ChatResponse
            {
                Success = true,
                Reply = ChatChannelMessageHelper.FormatReply(
                    reply,
                    "Mình chưa có câu trả lời phù hợp. Bạn thử nói rõ hơn nhu cầu như ngân sách, giới tính hoặc loại xe nhé."),
                ConversationId = conversationId,
                UsedAI = false,
                ElapsedMs = 0
            };
        }

        private async Task SaveExchangeIfNeededAsync(ChatRequest request, ChatResponse result)
        {
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
        public async Task<IActionResult> GetMessages(string conversationId, [FromQuery] string userId)
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
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        errorMessage = "userId không được để trống."
                    });
                }

                var isOwner = await _historyService.IsConversationOwnerAsync(conversationId.Trim(), userId.Trim());

                if (!isOwner)
                {
                    return Forbid();
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
        public async Task<IActionResult> DeleteConversation(
     string conversationId,
     [FromQuery] string userId)
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

                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        errorMessage = "userId không được để trống."
                    });
                }

                var normalizedId = conversationId.Trim();
                var normalizedUserId = userId.Trim();

                var isOwner = await _historyService.IsConversationOwnerAsync(normalizedId, normalizedUserId);

                if (!isOwner)
                {
                    return Forbid();
                }

                await _historyService.DeleteConversationAsync(normalizedId);
                _clarificationStateService.Clear(normalizedId);
                await _conversationPreferenceService.ClearAsync(normalizedId);
                await _conversationStateService.ClearAsync(normalizedId);

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
                if (string.IsNullOrWhiteSpace(request.UserId))
                {
                    return Unauthorized(new
                    {
                        success = false,
                        errorMessage = "userId không được để trống."
                    });
                }

                var userId = request.UserId.Trim();

                var isOwner = await _historyService.IsConversationOwnerAsync(conversationId, userId);

                if (!isOwner)
                {
                    return Forbid();
                }
                await _memoryService.ClearAsync(conversationId);
                _clarificationStateService.Clear(conversationId);

                await _conversationPreferenceService.ClearAsync(conversationId);
                _logger.LogInformation("Conversation reset. ConversationId: {ConversationId}", conversationId);
                await _conversationStateService.ClearAsync(conversationId);
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