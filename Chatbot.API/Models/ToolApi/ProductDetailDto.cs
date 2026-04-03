namespace Chatbot.API.Models.ToolApi
{
    public class ProductDetailDto: ProductSummaryDto
    {
        public string? MoTa { get; set; }
        public bool IsActive { get; set; }
    }
}
