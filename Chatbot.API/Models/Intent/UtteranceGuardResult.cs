namespace Chatbot.API.Models.Intent
{
    public enum UtteranceKind
    {
        Domain,
        OutOfScope,
        Noise,
        Ack,
        Greeting,
        Unknown
    }

    public class UtteranceGuardResult
    {
        public UtteranceKind Kind { get; set; } = UtteranceKind.Unknown;
        public bool ShouldAskClarification { get; set; }
        public string? ClarificationMessage { get; set; }
    }
}