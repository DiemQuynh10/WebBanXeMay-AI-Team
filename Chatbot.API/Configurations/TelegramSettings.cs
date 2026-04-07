namespace Chatbot.API.Configurations
{
    public class TelegramSettings
    {
        public string BotToken { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.telegram.org";
        public string SecretToken { get; set; } = string.Empty;
    }
}