using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Chatbot.API.Models.Entities
{
    public class ConversationStateEntity
    {
        public int Id { get; set; }

        [Required]
        public int ConversationSessionId { get; set; }

        [ForeignKey(nameof(ConversationSessionId))]
        public ConversationSession? ConversationSession { get; set; }

        [MaxLength(50)]
        public string? CurrentDomain { get; set; } // xe_may, don_hang, out_of_scope

        [MaxLength(50)]
        public string? CurrentGoalType { get; set; } // recommendation, search, lookup, compare, order_lookup

        [MaxLength(50)]
        public string? CurrentGoalStatus { get; set; } // active, pending_clarification, completed, switched

        [MaxLength(100)]
        public string? LastIntentType { get; set; }

        [MaxLength(100)]
        public string? LastQuestionType { get; set; } // ask_price, ask_stock, refine_brand, refine_budget...

        [MaxLength(100)]
        public string? LastBotQuestionType { get; set; } // clarify_budget, clarify_brand, clarify_use_case...

        [MaxLength(500)]
        public string? LastResolvedReference { get; set; } // "Honda Air Blade", "xe đầu tiên", ...

        public string? ConstraintsJson { get; set; } // brand/category/price/target/use_case...

        public string? CandidateProductIdsJson { get; set; } // tập xe hiện tại đang xét

        public string? MentionedProductIdsJson { get; set; } // các xe vừa nhắc đến

        public string? MentionedProductNamesJson { get; set; }

        public string? TurnSummary { get; set; } // tóm tắt ngắn 1 dòng cho turn gần nhất

        public decimal? CarryForwardConfidence { get; set; }

        public bool IsAwaitingClarification { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}