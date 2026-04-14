namespace Chatbot.API.Models.Intent
{
    public class LLMRecommendationResult
    {
        public double Confidence { get; set; }
        public List<LLMRecommendedItem> Recommendations { get; set; } = new();
    }

    public class LLMRecommendedItem
    {
        public int ProductId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}