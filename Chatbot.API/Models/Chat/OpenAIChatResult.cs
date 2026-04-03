namespace Chatbot.API.Models.Chat
{
    public class OpenAIChatResult
    {
        public string Reply { get; set; } = "";
        public string? UsedTool { get; set; }

    }
}
