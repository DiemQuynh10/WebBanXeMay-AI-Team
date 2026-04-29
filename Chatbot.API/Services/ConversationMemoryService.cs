using Chatbot.API.Data;
using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Entities;
using Chatbot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.API.Services
{
    public class ConversationMemoryService : IConversationMemoryService
    {
        private const int MaxMessages = 20;
        private readonly ChatbotDbContext _dbContext;

        public ConversationMemoryService(ChatbotDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ChatMessage>> GetMessagesAsync(string conversationId)
        {
            var session = await _dbContext.ConversationSessions
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.IsActive);

            if (session == null)
                return new List<ChatMessage>();

            var messages = await _dbContext.ConversationMessages
                .Where(x => x.ConversationSessionId == session.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(MaxMessages)
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new ChatMessage
                {
                    Role = NormalizeRole(x.Role),
                    Content = x.Content
                })
                .ToListAsync();

            return messages;
        }
        // Deprecated: không dùng để persist hội thoại nữa.
        // Lưu exchange chuẩn hóa được thực hiện tại ConversationHistoryService.SaveExchangeAsync từ ChatController.
        public async Task AddMessageAsync(string conversationId, ChatMessage message, string? channel = null, string? userId = null)
        {
            var session = await _dbContext.ConversationSessions
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

            if (session == null)
            {
                session = new ConversationSession
                {
                    ConversationId = conversationId,
                    Channel = channel,
                    UserId = userId
                };

                _dbContext.ConversationSessions.Add(session);
                await _dbContext.SaveChangesAsync();
            }

            var entity = new ConversationMessageEntity
            {
                ConversationSessionId = session.Id,
                Role = message.Role,
                Content = message.Content
            };

            _dbContext.ConversationMessages.Add(entity);
            await _dbContext.SaveChangesAsync();
        }

        public async Task ClearAsync(string conversationId)
        {
            var session = await _dbContext.ConversationSessions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

            if (session == null) return;

            _dbContext.ConversationMessages.RemoveRange(session.Messages);
            _dbContext.ConversationSessions.Remove(session);

            await _dbContext.SaveChangesAsync();
        }

        public async Task<bool> ExistsAsync(string conversationId)
        {
            return await _dbContext.ConversationSessions
                .AnyAsync(x => x.ConversationId == conversationId && x.IsActive);
        }

        private static string NormalizeRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return "user";
            }

            return role.Equals("bot", StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : role;
        }
    }
}