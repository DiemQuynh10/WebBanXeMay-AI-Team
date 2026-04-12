using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ChatService : IChatService
    {
        private readonly IChatFlowOrchestrator _orchestrator;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            IChatFlowOrchestrator orchestrator,
            ILogger<ChatService> logger)
        {
            _orchestrator = orchestrator;
            _logger = logger;
        }

        public Task<ChatResponse> ProcessMessageAsync(ChatRequest request)
        {
            _logger.LogWarning("=== CHAT SERVICE WRAPPER HIT ===");
            return _orchestrator.HandleAsync(request);
        }
    }
}