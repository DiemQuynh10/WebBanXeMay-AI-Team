using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Helpers
{
    public class ProductScoreResult
    {
        public ProductSummaryDto Product { get; set; } = default!;
        public int Score { get; set; }
        public List<string> Reasons { get; set; } = new();
    }
}