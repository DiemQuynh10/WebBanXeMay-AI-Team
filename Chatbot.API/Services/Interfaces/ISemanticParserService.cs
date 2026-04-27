using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface ISemanticParserService
    {
        Task<SemanticResult> ParseAsync(
            string rawMessage,
            CustomerPreferenceProfile? profile = null,
            CancellationToken cancellationToken = default);
    }
}
