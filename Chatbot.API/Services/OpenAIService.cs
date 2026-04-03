using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chatbot.API.Configurations;
using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Requests;
using Chatbot.API.Models.Responses;
using Chatbot.API.Prompts;
using Chatbot.API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Services
{
    public class OpenAIService : IOpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly OpenAISettings _settings;
        private readonly IConversationMemoryService _memoryService;
        private readonly IToolDefinitionProvider _toolDefinitionProvider;
        private readonly IToolDispatcher _toolDispatcher;
        private readonly ILogger<OpenAIService> _logger;

        public OpenAIService(
            HttpClient httpClient,
            IOptions<OpenAISettings> settings,
            IConversationMemoryService memoryService,
            IToolDefinitionProvider toolDefinitionProvider,
            IToolDispatcher toolDispatcher,
            ILogger<OpenAIService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _memoryService = memoryService;
            _toolDefinitionProvider = toolDefinitionProvider;
            _toolDispatcher = toolDispatcher;
            _logger = logger;
        }

        public async Task<ChatResponse> AskAsync(AIRequestContext context)
        {
            var conversationId = string.IsNullOrWhiteSpace(context.ConversationId)
                ? Guid.NewGuid().ToString()
                : context.ConversationId.Trim();

            var channel = string.IsNullOrWhiteSpace(context.Channel)
                ? "web"
                : context.Channel.Trim();

            var userId = string.IsNullOrWhiteSpace(context.UserId)
                ? null
                : context.UserId.Trim();

            var originalUserMessage = context.OriginalUserMessage?.Trim() ?? string.Empty;
            var effectivePrompt = string.IsNullOrWhiteSpace(context.EffectivePrompt)
                ? originalUserMessage
                : context.EffectivePrompt.Trim();

            var ragContext = context.RagContext;

            _logger.LogInformation(
                "OpenAI AskAsync - ragContext length: {Length}",
                string.IsNullOrWhiteSpace(ragContext) ? 0 : ragContext.Length);

            try
            {
                var history = await _memoryService.GetMessagesAsync(conversationId);

                var messages = BuildInitialMessages(history, effectivePrompt, ragContext);

                var requestBody = new JsonObject
                {
                    ["model"] = _settings.Model,
                    ["messages"] = messages,
                    ["tools"] = _toolDefinitionProvider.GetTools(),
                    ["tool_choice"] = "auto"
                };

                var rawJson = await SendChatCompletionAsync(requestBody, "OpenAI first response");

                using var doc = JsonDocument.Parse(rawJson);
                var message = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message");

                string? usedTool = null;
                string finalReply;

                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.GetArrayLength() > 0)
                {
                    var toolCall = toolCalls[0];
                    var toolCallId = toolCall.GetProperty("id").GetString() ?? string.Empty;
                    var functionName = toolCall.GetProperty("function").GetProperty("name").GetString() ?? string.Empty;
                    var argumentsJson = toolCall.GetProperty("function").GetProperty("arguments").GetString() ?? "{}";

                    usedTool = functionName;

                    _logger.LogInformation(
                        "Tool called: {ToolName}, ConversationId: {ConversationId}, Arguments: {Arguments}",
                        usedTool,
                        conversationId,
                        argumentsJson);

                    var toolResult = await _toolDispatcher.ExecuteAsync(functionName, argumentsJson);

                    var secondMessages = BuildSecondMessages(
                        history,
                        effectivePrompt,
                        toolCallId,
                        functionName,
                        argumentsJson,
                        toolResult,
                        ragContext);

                    var secondBody = new JsonObject
                    {
                        ["model"] = _settings.Model,
                        ["messages"] = secondMessages
                    };

                    var secondRawJson = await SendChatCompletionAsync(secondBody, "OpenAI second response");

                    using var secondDoc = JsonDocument.Parse(secondRawJson);
                    var secondMessage = secondDoc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message");

                    finalReply = secondMessage.TryGetProperty("content", out var secondContentNode)
                        ? secondContentNode.GetString() ?? "Mình chưa có câu trả lời phù hợp."
                        : "Mình chưa có câu trả lời phù hợp.";
                }
                else
                {
                    finalReply = message.TryGetProperty("content", out var contentNode)
                        ? contentNode.GetString() ?? "Mình chưa có câu trả lời phù hợp."
                        : "Mình chưa có câu trả lời phù hợp.";
                }

                await _memoryService.AddMessageAsync(
                    conversationId,
                    new ChatMessage
                    {
                        Role = "user",
                        Content = originalUserMessage
                    },
                    channel,
                    userId);

                await _memoryService.AddMessageAsync(
                    conversationId,
                    new ChatMessage
                    {
                        Role = "assistant",
                        Content = finalReply
                    },
                    channel,
                    userId);

                return new ChatResponse
                {
                    Success = true,
                    Reply = finalReply,
                    UsedTool = usedTool,
                    UsedAI = true,
                    ErrorMessage = null,
                    ConversationId = conversationId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AskAsync. ConversationId: {ConversationId}", conversationId);

                return new ChatResponse
                {
                    Success = false,
                    Reply = "Xin lỗi, chatbot đang gặp lỗi tạm thời. Bạn vui lòng thử lại sau.",
                    UsedAI = true,
                    ErrorMessage = ex.Message,
                    ConversationId = conversationId
                };
            }
        }
        private JsonArray BuildInitialMessages(List<ChatMessage> history, string userMessage, string? ragContext = null)
        {
            var messages = new JsonArray
    {
        new JsonObject
        {
            ["role"] = "system",
            ["content"] = SystemPromptProvider.GetSystemPrompt()
        }
    };

            if (!string.IsNullOrWhiteSpace(ragContext))
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "system",
                    ["content"] =
                        "Dưới đây là ngữ cảnh tham khảo được truy xuất từ kho tri thức nội bộ. " +
                        "Chỉ sử dụng khi phù hợp và không được mâu thuẫn với dữ liệu realtime từ tool API.\n\n" +
                        ragContext
                });
            }

            foreach (var msg in history)
            {
                messages.Add(new JsonObject
                {
                    ["role"] = msg.Role,
                    ["content"] = msg.Content
                });
            }

            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = userMessage
            });

            return messages;
        }

        private JsonArray BuildSecondMessages(
    List<ChatMessage> history,
    string userMessage,
    string toolCallId,
    string functionName,
    string argumentsJson,
    string toolResult,
    string? ragContext = null)
        {
            var secondMessages = new JsonArray
{
    new JsonObject
    {
        ["role"] = "system",
        ["content"] = SystemPromptProvider.GetToolResultPrompt()
    }
};

            if (!string.IsNullOrWhiteSpace(ragContext))
            {
                secondMessages.Add(new JsonObject
                {
                    ["role"] = "system",
                    ["content"] =
                        "Ngữ cảnh bổ sung từ kho tri thức nội bộ. Chỉ dùng để hỗ trợ diễn giải, " +
                        "không được mâu thuẫn với dữ liệu tool realtime.\n\n" + ragContext
                });
            }

            foreach (var msg in history)
            {
                if (msg.Role == "user" || msg.Role == "assistant")
                {
                    secondMessages.Add(new JsonObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content
                    });
                }
            }

            secondMessages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = userMessage
            });

            secondMessages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = toolCallId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = functionName,
                            ["arguments"] = argumentsJson
                        }
                    }
                }
            });

            secondMessages.Add(new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = toolCallId,
                ["content"] = toolResult
            });

            return secondMessages;
        }

        private async Task<string> SendChatCompletionAsync(JsonObject requestBody, string logLabel)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            httpRequest.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            var rawJson = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("{LogLabel}: {RawJson}", logLabel, rawJson);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"OpenAI API error. StatusCode={(int)response.StatusCode}, Body={rawJson}");
            }

            return rawJson;
        }
    }
}