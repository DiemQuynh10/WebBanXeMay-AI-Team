using System.ComponentModel.DataAnnotations;

namespace Chatbot.API.Models.Entities
{
    public class ConversationSession
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string ConversationId { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Channel { get; set; }

        [MaxLength(100)]
        public string? UserId { get; set; }

        [MaxLength(200)]
        public string? Title { get; set; }

        [MaxLength(500)]
        public string? LastMessagePreview { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        public ICollection<ConversationMessageEntity> Messages { get; set; } = new List<ConversationMessageEntity>();
    }
}