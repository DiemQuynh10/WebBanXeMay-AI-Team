namespace WebBanXeMay.Dtos.ToolApi
{
    public class ProductDetailDto : ProductSummaryDto
    {
        public string? MoTa { get; set; }
        public bool IsActive { get; set; }
    }
}