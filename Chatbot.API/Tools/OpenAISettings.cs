using System.ComponentModel.DataAnnotations;

namespace Chatbot.API.Tools
{
    public class OpenAISettings
    {
        [Required]
        public string ApiKey { get; set; } = string.Empty;
        [Required]
        public string Model { get; set; } = "gpt-4o-mini";
    }
}
