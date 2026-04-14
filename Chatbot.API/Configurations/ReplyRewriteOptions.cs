namespace Chatbot.API.Configurations
{
    public class ReplyRewriteOptions
    {
        public bool EnableRecommendationRewrite { get; set; } = false;
        public bool EnableRefinementRewrite { get; set; } = true;
    }
}