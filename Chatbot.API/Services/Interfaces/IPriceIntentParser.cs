using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IPriceIntentParser
    {
        PriceIntent Parse(string message);
    }
}