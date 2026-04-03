using Microsoft.AspNetCore.Mvc;
using WebBanXeMay.Infrastructure.Security;

namespace WebBanXeMay.Controllers.Api.Tools
{
    [ApiController]
    [Route("api/tools/health")]
    public class ToolHealthController : ControllerBase
    {
        private readonly IConfiguration _config;
        public ToolHealthController(IConfiguration config) => _config = config;

        [HttpGet]
        public IActionResult Get()
        {
            // Có thể yêu cầu API key hoặc không, tuỳ bạn. Khuyên: có.
            if (!ToolApiKeyValidator.IsValid(Request, _config))
                return ToolApiKeyValidator.UnauthorizedResult();

            return Ok(new
            {
                status = "ok",
                serverTime = DateTime.UtcNow
            });
        }
    }
}