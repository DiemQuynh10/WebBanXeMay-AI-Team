using Chatbot.API.Models.Intent;

namespace Chatbot.API.Models.Chat
{
    public class TurnContextBuildResult
    {
        public ParsedIntent EffectiveIntent { get; set; } = new();
        public CustomerPreferenceProfile EffectiveProfile { get; set; } = new();

        public bool IsFollowUp { get; set; }
        public bool IsGoalSwitch { get; set; }
        public bool IsShortFollowUp { get; set; }

        public string? GoalContinuity { get; set; } // continue, refine, pivot, new_goal
        public string? Reason { get; set; }

        public string? ResolvedReference { get; set; }
        public List<string> CarryForwardFields { get; set; } = new();
    }
}