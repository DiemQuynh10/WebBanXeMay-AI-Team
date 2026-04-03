using Microsoft.AspNetCore.Mvc;

namespace WebBanXeMay.Infrastructure.Security
{
    public static class ToolApiKeyValidator
    {
        public const string HeaderName = "X-Tool-Api-Key";

        public static bool IsValid(HttpRequest request, IConfiguration config)
        {
            var expected = config["ToolApi:ApiKey"];
            if (string.IsNullOrWhiteSpace(expected)) return false;

            if (!request.Headers.TryGetValue(HeaderName, out var provided))
                return false;

            return string.Equals(provided.ToString(), expected, StringComparison.Ordinal);
        }

        public static IActionResult UnauthorizedResult()
            => new UnauthorizedObjectResult(new { error = "Missing/invalid tool api key" });
    }
}