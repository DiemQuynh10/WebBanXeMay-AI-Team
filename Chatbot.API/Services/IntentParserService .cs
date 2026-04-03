using Chatbot.API.Models.Intent;
using Chatbot.API.Services.Interfaces;

namespace Chatbot.API.Services
{
    public class IntentParserService : IIntentParserService
    {
        public Task<ParsedIntent> ParseAsync(string message)
        {
            var result = new ParsedIntent
            {
                RawMessage = message ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(message))
                return Task.FromResult(result);

            var text = message.ToLowerInvariant();

            if (text.Contains("xe ga") || text.Contains("tay ga") || text.Contains("scooter"))
                result.Category = "xe ga";
            else if (text.Contains("xe số"))
                result.Category = "xe số";
            else if (text.Contains("côn tay") || text.Contains("xe côn"))
                result.Category = "côn tay";

            if (text.Contains("honda"))
                result.Brand = "Honda";
            else if (text.Contains("yamaha"))
                result.Brand = "Yamaha";
            else if (text.Contains("suzuki"))
                result.Brand = "Suzuki";
            else if (text.Contains("sym"))
                result.Brand = "SYM";
            else if (text.Contains("piaggio"))
                result.Brand = "Piaggio";

            var targets = new List<string>();

            if (text.Contains("sinh viên"))
                targets.Add("sinh viên");

            if (text.Contains("nữ"))
                targets.Add("nữ");

            if (text.Contains("nam"))
                targets.Add("nam");

            if (targets.Any())
                result.Target = string.Join(" ", targets);

            return Task.FromResult(result);
        }
    }
}