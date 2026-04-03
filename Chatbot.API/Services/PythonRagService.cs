using System.Text;
using System.Text.Json;
using Chatbot.API.Configurations;
using Chatbot.API.Models.Rag;
using Chatbot.API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Services
{
    public class PythonRagService : IRagService
    {
        private readonly HttpClient _httpClient;
        private readonly RagApiOptions _options;
        private readonly ILogger<PythonRagService> _logger;

        public PythonRagService(
            HttpClient httpClient,
            IOptions<RagApiOptions> options,
            ILogger<PythonRagService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<RagQueryResponse?> QueryAsync(string query, int topK = 4)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new RagQueryResponse
                {
                    Success = false,
                    Context = string.Empty
                };
            }

            var payload = new RagQueryRequest
            {
                Query = query.Trim(),
                TopK = topK
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{_options.BaseUrl}/rag/query", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Python RAG returned error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                    return new RagQueryResponse
                    {
                        Success = false,
                        Context = string.Empty
                    };
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<RagQueryResponse>(responseBody, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while calling Python RAG service.");
                return new RagQueryResponse
                {
                    Success = false,
                    Context = string.Empty
                };
            }
        }
    }
}