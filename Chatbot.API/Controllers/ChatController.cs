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
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatService chatService, IConversationMemoryService memoryService, IClarificationStateService clarificationStateService, ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _memoryService = memoryService;
            _clarificationStateService = clarificationStateService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            try
            {
                var result = await _chatService.ProcessMessageAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while processing chat request.");

                return StatusCode(500, new
                {
                    success = false,
                    reply = "Xin lỗi, chatbot đang gặp lỗi tạm thời.",
                    errorMessage = ex.Message,
                    conversationId = request?.ConversationId
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