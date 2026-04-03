namespace Chatbot.API.Models.ToolApi
{
    public class ProductSearchResponseDto
    {
        public int Count {  get; set; }
        public List<ProductSummaryDto> Items { get; set; } = new();
    }
}
