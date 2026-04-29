namespace Chatbot.API.Models.Intent
{
    public class ContextualIntentDecision
    {
        public string Flow { get; set; } = "unknown";
        public string Action { get; set; } = "unknown";
        public double Confidence { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool? ShouldKeepPreviousFeature { get; set; }
    }
}