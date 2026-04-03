using Chatbot.API.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ConversationSession>()
                .HasIndex(x => x.ConversationId)
                .IsUnique();

            modelBuilder.Entity<ConversationSession>()
                .HasMany(x => x.Messages)
                .WithOne(x => x.ConversationSession)
                .HasForeignKey(x => x.ConversationSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}