using System.ComponentModel.DataAnnotations;

namespace Chatbot.API.Configurations
{
    public class RagApiOptions
    {
        [Required]
        public string BaseUrl { get; set; } = string.Empty;

        public int TimeoutSeconds { get; set; } = 30;
    }
}