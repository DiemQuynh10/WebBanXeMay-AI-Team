using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Chatbot.API.Models.Entities
{
    public class ConversationMessageEntity
    {
        public int Id { get; set; }

        [Required]
        public int ConversationSessionId { get; set; }

        [ForeignKey(nameof(ConversationSessionId))]
        public ConversationSession? ConversationSession { get; set; }

        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}