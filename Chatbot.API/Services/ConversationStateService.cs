using System.Text.Json;
using Chatbot.API.Data;
using Chatbot.API.Models.Chat;
using Chatbot.API.Models.Entities;
using Chatbot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.API.Services
{
    public class ConversationStateService : IConversationStateService
    {
        private readonly ChatbotDbContext _dbContext;

        public ConversationStateService(ChatbotDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<ConversationState> GetAsync(string conversationId, string? channel = null, string? userId = null)
        {
            var session = await GetOrCreateSessionAsync(conversationId, channel, userId);

            var entity = await _dbContext.ConversationStates
                .FirstOrDefaultAsync(x => x.ConversationSessionId == session.Id);

            if (entity == null)
            {
                entity = new ConversationStateEntity
                {
                    ConversationSessionId = session.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                _dbContext.ConversationStates.Add(entity);
                await _dbContext.SaveChangesAsync();
            }

            return MapToModel(conversationId, entity);
        }

        public async Task ApplyPatchAsync(string conversationId, ConversationStatePatch patch, string? channel = null, string? userId = null)
        {
            var session = await GetOrCreateSessionAsync(conversationId, channel, userId);

            var entity = await _dbContext.ConversationStates
                .FirstOrDefaultAsync(x => x.ConversationSessionId == session.Id);

            if (entity == null)
            {
                entity = new ConversationStateEntity
                {
                    ConversationSessionId = session.Id,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _dbContext.ConversationStates.Add(entity);
            }

            if (patch.CurrentDomain != null) entity.CurrentDomain = patch.CurrentDomain;
            if (patch.CurrentGoalType != null) entity.CurrentGoalType = patch.CurrentGoalType;
            if (patch.CurrentGoalStatus != null) entity.CurrentGoalStatus = patch.CurrentGoalStatus;

            if (patch.LastIntentType != null) entity.LastIntentType = patch.LastIntentType;
            if (patch.LastQuestionType != null) entity.LastQuestionType = patch.LastQuestionType;
            if (patch.LastBotQuestionType != null) entity.LastBotQuestionType = patch.LastBotQuestionType;

            if (patch.LastResolvedReference != null) entity.LastResolvedReference = patch.LastResolvedReference;

            if (patch.ClearConstraints)
            {
                entity.ConstraintsJson = Serialize(new Dictionary<string, string?>());
            }
            else if (patch.Constraints != null)
            {
                entity.ConstraintsJson = Serialize(patch.Constraints);
            }

            if (patch.ClearCandidateProducts)
            {
                entity.CandidateProductIdsJson = Serialize(new List<int>());
            }
            else if (patch.CandidateProductIds != null)
            {
                entity.CandidateProductIdsJson = Serialize(patch.CandidateProductIds);
            }

            if (patch.ClearMentionedProducts)
            {
                entity.MentionedProductIdsJson = Serialize(new List<int>());
                entity.MentionedProductNamesJson = Serialize(new List<string>());
            }
            else
            {
                if (patch.MentionedProductIds != null)
                    entity.MentionedProductIdsJson = Serialize(patch.MentionedProductIds);

                if (patch.MentionedProductNames != null)
                    entity.MentionedProductNamesJson = Serialize(patch.MentionedProductNames);
            }

            if (patch.TurnSummary != null) entity.TurnSummary = patch.TurnSummary;
            if (patch.CarryForwardConfidence.HasValue) entity.CarryForwardConfidence = patch.CarryForwardConfidence.Value;
            if (patch.IsAwaitingClarification.HasValue) entity.IsAwaitingClarification = patch.IsAwaitingClarification.Value;

            entity.UpdatedAtUtc = DateTime.UtcNow;
            session.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
        }

        public async Task ClearAsync(string conversationId)
        {
            var session = await _dbContext.ConversationSessions
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

            if (session == null)
                return;

            var state = await _dbContext.ConversationStates
                .FirstOrDefaultAsync(x => x.ConversationSessionId == session.Id);

            if (state == null)
                return;

            _dbContext.ConversationStates.Remove(state);
            await _dbContext.SaveChangesAsync();
        }

        private async Task<ConversationSession> GetOrCreateSessionAsync(string conversationId, string? channel, string? userId)
        {
            var session = await _dbContext.ConversationSessions
                .FirstOrDefaultAsync(x => x.ConversationId == conversationId);

            if (session != null)
                return session;

            session = new ConversationSession
            {
                ConversationId = conversationId,
                Channel = string.IsNullOrWhiteSpace(channel) ? "web" : channel.Trim(),
                UserId = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim(),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                IsActive = true
            };

            _dbContext.ConversationSessions.Add(session);
            await _dbContext.SaveChangesAsync();
            return session;
        }

        private static ConversationState MapToModel(string conversationId, ConversationStateEntity entity)
        {
            return new ConversationState
            {
                ConversationId = conversationId,
                CurrentDomain = entity.CurrentDomain,
                CurrentGoalType = entity.CurrentGoalType,
                CurrentGoalStatus = entity.CurrentGoalStatus,
                LastIntentType = entity.LastIntentType,
                LastQuestionType = entity.LastQuestionType,
                LastBotQuestionType = entity.LastBotQuestionType,
                LastResolvedReference = entity.LastResolvedReference,
                Constraints = DeserializeDictionary(entity.ConstraintsJson),
                CandidateProductIds = DeserializeListInt(entity.CandidateProductIdsJson),
                MentionedProductIds = DeserializeListInt(entity.MentionedProductIdsJson),
                MentionedProductNames = DeserializeListString(entity.MentionedProductNamesJson),
                TurnSummary = entity.TurnSummary,
                CarryForwardConfidence = entity.CarryForwardConfidence,
                IsAwaitingClarification = entity.IsAwaitingClarification,
                UpdatedAtUtc = entity.UpdatedAtUtc
            };
        }

        private static string Serialize<T>(T data)
        {
            return JsonSerializer.Serialize(data);
        }

        private static Dictionary<string, string?> DeserializeDictionary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                   ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<int> DeserializeListInt(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<int>();

            return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
        }

        private static List<string> DeserializeListString(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
    }
}