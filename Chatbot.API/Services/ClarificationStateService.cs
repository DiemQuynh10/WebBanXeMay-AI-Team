using System.Collections.Concurrent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class ClarificationStateService : IClarificationStateService
    {
        private readonly ConcurrentDictionary<string, string> _pending = new();

        public void SetPending(string conversationId, string normalizedMessage)
        {
            if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(normalizedMessage))
                return;

            _pending[conversationId] = normalizedMessage;
        }

        public string? GetPending(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                return null;

            return _pending.TryGetValue(conversationId, out var value) ? value : null;
        }

        public void Clear(string conversationId)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                return;

            _pending.TryRemove(conversationId, out _);
        }
    }
}