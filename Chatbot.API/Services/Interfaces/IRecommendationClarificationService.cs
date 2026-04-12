using Chatbot.API.Models.Intent;
using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Services.Interfaces
{
    public interface IRecommendationClarificationService
    {
        bool HasEnoughSignalsForDirectRecommendation(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null);

        bool NeedsClarificationForConsultation(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null);

        string BuildClarificationQuestion(
            string message,
            ParsedIntent parsedIntent,
            CustomerPreferenceProfile? profile = null);
        bool IsConsultationIntent(string message);
    }
}