using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IUtteranceGuardService
    {
        UtteranceGuardResult Analyze(string? normalizedMessage, ParsedIntent? parsedIntent);
    }
}