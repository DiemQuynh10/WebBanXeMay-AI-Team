using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IIntentParserService
    {
        Task<ParsedIntent> ParseAsync(string message);
    }
}
