using Chatbot.API.Data;
using Chatbot.API.Models.Entities;
using Chatbot.API.Models.Responses;
using Chatbot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.API.Services
{
    public class ConversationHistoryService : IConversationHistoryService
    {
        private readonly ChatbotDbContext _db;
        private readonly ILogger<ConversationHistoryService> _logger;

        public ConversationHistoryService(ChatbotDbContext db, ILogger<ConversationHistoryService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SaveExchangeAsync(
            string conversationId,
            string? channel,
            string? userId,
            string userMessage,
            string botReply)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
                throw new ArgumentException("conversationId is required", nameof(conversationId));

            var normalizedChannel = string.IsNullOrWhiteSpace(channel) ? "web" : channel.Trim();
            var normalizedUserId = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();

            var session = await _db.ConversationSessions
                .Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

            if (session == null)
            {
                session = new ConversationSession
                {
                    ConversationId = conversationId,
                    Channel = normalizedChannel,
                    UserId = normalizedUserId,
                    Title = BuildTitle(userMessage),
                    LastMessagePreview = Truncate(botReply, 300),
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    IsActive = true
                };

                _db.ConversationSessions.Add(session);
                await _db.SaveChangesAsync();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(session.Channel))
                    session.Channel = normalizedChannel;

                if (string.IsNullOrWhiteSpace(session.UserId))
                    session.UserId = normalizedUserId;

                if (string.IsNullOrWhiteSpace(session.Title))
                    session.Title = BuildTitle(userMessage);

                session.LastMessagePreview = Truncate(botReply, 300);
                session.UpdatedAtUtc = DateTime.UtcNow;
            }

            _db.ConversationMessages.Add(new ConversationMessageEntity
            {
                ConversationSessionId = session.Id,
                Role = "user",
                Content = userMessage,
                CreatedAtUtc = DateTime.UtcNow
            });

            _db.ConversationMessages.Add(new ConversationMessageEntity
            {
                ConversationSessionId = session.Id,
                Role = "assistant",
                Content = botReply,
                CreatedAtUtc = DateTime.UtcNow
            });

            session.UpdatedAtUtc = DateTime.UtcNow;
            session.LastMessagePreview = Truncate(botReply, 300);

            await _db.SaveChangesAsync();
        }

        public async Task<List<ConversationSummaryResponse>> GetConversationsAsync(string userId, string channel)
        {
            var normalizedChannel = string.IsNullOrWhiteSpace(channel) ? "web" : channel.Trim();

            return await _db.ConversationSessions
                .Where(x => x.IsActive && x.UserId == userId && x.Channel == normalizedChannel)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => new ConversationSummaryResponse
                {
                    ConversationId = x.ConversationId,
                    Title = x.Title,
                    LastMessagePreview = x.LastMessagePreview,
                    CreatedAtUtc = x.CreatedAtUtc,
                    UpdatedAtUtc = x.UpdatedAtUtc
                })
                .ToListAsync();
        }

        public async Task<List<ConversationMessageResponse>> GetMessagesAsync(string conversationId)
        {
            return await _db.ConversationMessages
                .Where(x => x.ConversationSession != null && x.ConversationSession.ConversationId == conversationId)
                .OrderBy(x => x.CreatedAtUtc)
                .Select(x => new ConversationMessageResponse
                {
                    Role = x.Role,
                    Content = x.Content,
                    CreatedAtUtc = x.CreatedAtUtc
                })
                .ToListAsync();
        }

        public async Task<bool> ExistsAsync(string conversationId)
        {
            return await _db.ConversationSessions.AnyAsync(x => x.ConversationId == conversationId && x.IsActive);
        }

        public async Task DeleteConversationAsync(string conversationId)
        {
            var session = await _db.ConversationSessions
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

            if (session == null)
                return;

            session.IsActive = false;
            session.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private static string BuildTitle(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Đoạn chat mới";

            var cleaned = message.Trim();
            return cleaned.Length <= 60 ? cleaned : cleaned.Substring(0, 60) + "...";
        }

        private static string Truncate(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}