namespace Chatbot.API.Models.Intent
{
    public enum RecommendationContextDecision
    {
        None = 0,

        New = 1,
        Continue = 2,
        Refine = 3,
        Pivot = 4,
        Ambiguous = 5,

        StartFreshRecommendation = New,
        ExpandFromCurrentGoal = Continue,
        NarrowWithinCurrentSet = Refine
    }
}