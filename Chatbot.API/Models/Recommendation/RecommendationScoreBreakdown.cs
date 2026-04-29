using Chatbot.API.Models.ToolApi;

namespace Chatbot.API.Models.Recommendation
{
    public class RecommendationCriterionScore
    {
        public string Criterion { get; set; } = string.Empty;
        public double Weight { get; set; }
        public double RawScore { get; set; }
        public double WeightedScore { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class RecommendationScoreBreakdown
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public double TotalScore { get; set; }
        public List<RecommendationCriterionScore> Criteria { get; set; } = new();
    }

    public class ScoredRecommendationItem
    {
        public ProductSummaryDto Product { get; set; } = default!;
        public RecommendationScoreBreakdown Breakdown { get; set; } = new();
    }
}