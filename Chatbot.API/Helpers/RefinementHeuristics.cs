using Chatbot.API.Models.Intent;
using Chatbot.API.Models.Responses;

namespace Chatbot.API.Helpers
{
    public static class RefinementHeuristics
    {
        public static bool LooksLikeRefinementFollowUp(
    string message,
    ParsedIntent intent,
    CustomerPreferenceProfile? profile)
        {
            var text = (message ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (profile?.HasActiveRecommendationContext == true &&
                (text.Contains("còn") || text.Contains("rẻ hơn") || text.Contains("xe ga thôi")))
            {
                return true;
            }

            return false;
        }
    }
}