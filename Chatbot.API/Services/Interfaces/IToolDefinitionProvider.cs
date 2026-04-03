using System.Text.Json.Nodes;

namespace Chatbot.API.Services.Interfaces
{
    public interface IToolDefinitionProvider
    {
        JsonArray GetTools();
    }
}
