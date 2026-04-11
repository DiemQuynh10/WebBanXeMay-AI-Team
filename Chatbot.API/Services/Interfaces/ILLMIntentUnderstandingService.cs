using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface ILLMIntentUnderstandingService
    {
        Task<LLMIntentResult?> UnderstandAsync(
            string message,
            CustomerPreferenceProfile? profile);
    }
}