using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IContextualIntentClassifierService
    {
        Task<ContextualIntentDecision?> ClassifyAsync(
            string normalizedMessage,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile profile,
            CancellationToken cancellationToken = default);
    }
}