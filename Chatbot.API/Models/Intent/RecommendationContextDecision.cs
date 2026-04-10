namespace Chatbot.API.Models.Intent
{
    public enum RecommendationContextDecision
    {
        None = 0,
        NarrowWithinCurrentSet = 1,
        ExpandFromCurrentGoal = 2,
        StartFreshRecommendation = 3
    }
}