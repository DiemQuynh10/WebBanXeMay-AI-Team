namespace Chatbot.API.Models.Normalization
{
    public class NormalizationResult
    {
        public string OriginalText { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;

        public bool HasChanges { get; set; }

        public bool NeedsConfirmation { get; set; }

        public string? Reason { get; set; }
    }
}