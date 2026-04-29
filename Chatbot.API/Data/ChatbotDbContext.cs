using Chatbot.API.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Chatbot.API.Data
{
    public class ChatbotDbContext : DbContext
    {
        public ChatbotDbContext(DbContextOptions<ChatbotDbContext> options)
            : base(options)
        {
        }

        public DbSet<ConversationSession> ConversationSessions { get; set; } = null!;
        public DbSet<ConversationMessageEntity> ConversationMessages { get; set; } = null!;
        public DbSet<ConversationStateEntity> ConversationStates { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ConversationSession>()
                .HasIndex(x => x.ConversationId)
                .IsUnique();

            modelBuilder.Entity<ConversationSession>()
                .HasIndex(x => new { x.Channel, x.UserId, x.UpdatedAtUtc });

            modelBuilder.Entity<ConversationSession>()
                .Property(x => x.Title)
                .HasMaxLength(200);

            modelBuilder.Entity<ConversationSession>()
                .Property(x => x.LastMessagePreview)
                .HasMaxLength(500);

            modelBuilder.Entity<ConversationSession>()
                .HasMany(x => x.Messages)
                .WithOne(x => x.ConversationSession)
                .HasForeignKey(x => x.ConversationSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ConversationSession>()
    .HasOne(x => x.State)
    .WithOne(x => x.ConversationSession)
    .HasForeignKey<ConversationStateEntity>(x => x.ConversationSessionId)
    .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ConversationStateEntity>()
                .HasIndex(x => x.ConversationSessionId)
                .IsUnique();

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.CurrentDomain)
                .HasMaxLength(50);

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.CurrentGoalType)
                .HasMaxLength(50);

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.CurrentGoalStatus)
                .HasMaxLength(50);

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.LastIntentType)
                .HasMaxLength(100);

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.LastQuestionType)
                .HasMaxLength(100);

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.LastBotQuestionType)
                .HasMaxLength(100);

            modelBuilder.Entity<ConversationStateEntity>()
                .Property(x => x.LastResolvedReference)
                .HasMaxLength(500);

            modelBuilder.Entity<ConversationMessageEntity>()
                .HasIndex(x => new { x.ConversationSessionId, x.CreatedAtUtc });
        }
    }
}