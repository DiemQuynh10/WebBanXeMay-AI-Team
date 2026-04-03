namespace Chatbot.API.Models.Intent
{
    public class RecommendationProfile
    {
        public bool IsStudent { get; set; }
        public bool IsFemale { get; set; }
        public bool IsMale { get; set; }
        public bool ForSchool { get; set; }
        public bool ForWork { get; set; }
        public string? PreferredCategory { get; set; }
    }
}