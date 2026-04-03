namespace Chatbot.API.Services.Interfaces
{
    public interface IToolDispatcher
    {
        Task<string> ExecuteAsync(string functionName, string argumentsJson);
    }
}
