namespace Chatbot.API.Services.Interfaces
{
    public interface IClarificationStateService
    {
        void SetPending(string conversationId, string normalizedMessage);
        string? GetPending(string conversationId);
        void Clear(string conversationId);
    }
}