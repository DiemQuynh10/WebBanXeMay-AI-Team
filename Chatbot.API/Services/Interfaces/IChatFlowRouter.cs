using Chatbot.API.Models.Intent;

namespace Chatbot.API.Services.Interfaces
{
    public interface IChatFlowRouter
    {
        FlowRoutingResult Route(
            string normalizedMessage,
            ParsedIntent intent,
            CustomerPreferenceProfile? profile);
    }
}