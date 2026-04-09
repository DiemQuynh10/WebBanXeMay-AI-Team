namespace Chatbot.API.Models.Intent
{
    public static class ChatFlowType
    {
        public const string Greeting = "greeting";
        public const string OrderLookup = "order_lookup";
        public const string ProductLookup = "product_lookup";
        public const string ProductSearch = "product_search";
        public const string Recommendation = "recommendation";
        public const string Compare = "compare";
        public const string RecommendationFollowUp = "recommendation_followup";
        public const string Refinement = "refinement";
        public const string BrandSwitch = "brand_switch";
        public const string OutOfScope = "out_of_scope";
        public const string Unknown = "unknown";
    }
}