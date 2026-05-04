using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IIntentRecoveryService
    {
        void Recover(
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile);
    }
}