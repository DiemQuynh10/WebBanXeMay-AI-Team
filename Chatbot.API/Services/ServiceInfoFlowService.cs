using System.Text;
using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ServiceInfoFlowService : IServiceInfoFlowService
    {
        private readonly IOpenAIService _openAIService;
        private readonly IRagService _ragService;
        private readonly ILogger<ServiceInfoFlowService> _logger;

        public ServiceInfoFlowService(
            IOpenAIService openAIService,
            IRagService ragService,
            ILogger<ServiceInfoFlowService> logger)
        {
            _openAIService = openAIService;
            _ragService = ragService;
            _logger = logger;
        }

        public async Task<ChatResponse> HandleAsync(
            ChatRequest request,
            string conversationId,
            string normalizedMessage,
            string semanticQuery,
            string originalMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            string? ragContext = null;
            var ragQuery = string.IsNullOrWhiteSpace(semanticQuery)
                ? normalizedMessage
                : semanticQuery;

            try
            {
                var ragResult = await _ragService.QueryAsync(ragQuery, topK: 8);
                if (ragResult?.Success == true && RagContextPostProcessor.HasUsableContext(ragResult.Context))
                {
                    ragContext = RagContextPostProcessor.Filter(
                        ragResult.Context,
                        normalizedMessage,
                        ragQuery,
                        intent,
                        profile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "RAG query failed for service/policy flow. ConversationId={ConversationId}",
                    conversationId);
            }

            var aiContext = new AIRequestContext
            {
                ConversationId = conversationId,
                Channel = request.Channel ?? "web",
                UserId = request.UserId,
                OriginalUserMessage = originalMessage,
                EffectivePrompt = BuildServicePrompt(normalizedMessage, ragQuery, intent, profile),
                RagContext = ragContext
            };

            var aiResult = await _openAIService.AskAsync(aiContext);
            if ((!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Reply) || RagContextPostProcessor.LooksLikeNoDataReply(aiResult.Reply)) &&
                RagContextPostProcessor.HasUsableContext(ragContext))
            {
                aiResult.Success = true;
                aiResult.UsedAI = false;
                aiResult.Reply = BuildRagOnlyReply(ragContext!);
            }

            if (string.IsNullOrWhiteSpace(aiResult.Reply))
            {
                aiResult.Success = true;
                aiResult.UsedAI = true;
                aiResult.Reply = "M\u00ecnh ch\u01b0a c\u00f3 \u0111\u1ee7 d\u1eef li\u1ec7u ch\u00ednh x\u00e1c v\u1ec1 ph\u1ea7n n\u00e0y. B\u1ea1n n\u00f3i r\u00f5 h\u01a1n m\u1ed9t ch\u00fat nh\u01b0 tr\u1ea3 g\u00f3p, b\u1ea3o d\u01b0\u1ee1ng, b\u1ea3o h\u00e0nh ho\u1eb7c ch\u00ednh s\u00e1ch c\u1ee5 th\u1ec3 \u0111\u1ec3 m\u00ecnh tr\u1ea3 l\u1eddi s\u00e1t h\u01a1n nh\u00e9.";
            }

            aiResult.ConversationId ??= conversationId;
            return aiResult;
        }

        private static string BuildServicePrompt(
            string normalizedMessage,
            string semanticQuery,
            ParsedIntent intent,
            CustomerPreferenceProfile profile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("User is asking about motorcycle store services or policies.");
            sb.AppendLine($"Original question: {normalizedMessage}");
            sb.AppendLine($"RAG semantic query: {semanticQuery}");
            sb.AppendLine($"Intent group: {intent.IntentType}");
            if (!string.IsNullOrWhiteSpace(intent.PolicySlot))
            {
                sb.AppendLine($"Requested policy slot: {intent.PolicySlot}");
            }

            if (!string.IsNullOrWhiteSpace(profile.LastSemanticMeaning))
            {
                sb.AppendLine($"Nearest conversation semantic context: {profile.LastSemanticMeaning}");
            }

            sb.AppendLine("Answering rules:");
            sb.AppendLine("- Answer only the exact point the user asked; do not expand unless asked.");
            sb.AppendLine("- Always use the nearest RAG context when it is related or even approximately related.");
            sb.AppendLine("- If RAG contains concrete numbers, terms, fees, interest rates, warranty periods, kilometers, or conditions, you must include them accurately.");
            sb.AppendLine("- Do not say there is no data when RAG context is present and related.");
            sb.AppendLine("- Only fallback to missing-data wording when RAG context is empty or completely unrelated.");
            sb.AppendLine("- Reply in Vietnamese, about 3 concise sentences, warm and natural like a real sales consultant.");
            sb.AppendLine("- Do not invent interest rates, documents, warranty, maintenance, or store policy details.");
            return sb.ToString().Trim();
        }

        private static string BuildRagOnlyReply(string ragContext)
        {
            return RagContextPostProcessor.BuildGroundedReply(ragContext);
        }
    }
}
