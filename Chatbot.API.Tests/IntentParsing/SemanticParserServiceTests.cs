using System.Text.Json;
using Chatbot.API.Configurations;
using Chatbot.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chatbot.API.Tests.IntentParsing;

public class SemanticParserServiceTests
{
    [Fact]
    public async Task ParseAsync_BatchInputs_ShouldPrintSemanticResultsToConsole()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("OPENAI_API_KEY is not set. Skipping SemanticParserService integration test.");
            return;
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini";
        }

        var service = CreateService(apiKey, model);

        var inputs = new[]
        {
            "xe binh dan thoi",
            "gia mem la duoc",
            "tra gop nhe thoi",
            "toi muon tra gop lai suat thap",
            "khong can xe dat dau",
            "dung goi y xe qua cao cap",
            "muon xe cho sinh vien gia vua phai",
            "toi khong muon tra gop",
            "toi can xe gon nhe di pho",
            "xe nao vua re vua tiet kiem xang"
        };

        Console.WriteLine("=== SemanticParserService Batch Test ===");
        Console.WriteLine($"Model: {model}");
        Console.WriteLine($"Total inputs: {inputs.Length}");
        Console.WriteLine();

        foreach (var input in inputs)
        {
            var result = await service.ParseAsync(input);

            Console.WriteLine($"Input: {input}");
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            Console.WriteLine(new string('-', 80));
        }
    }

    private static SemanticParserService CreateService(string apiKey, string model)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var options = Options.Create(new OpenAISettings
        {
            ApiKey = apiKey,
            Model = model
        });

        return new SemanticParserService(httpClient, options, NullLogger<SemanticParserService>.Instance);
    }
}
