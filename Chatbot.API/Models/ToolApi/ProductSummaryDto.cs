namespace Chatbot.API.Models.ToolApi
{
    public class ProductSummaryDto
    {
        public int Id { get; set; }
        public string Ten { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public decimal Gia { get; set; }
        public int SoLuong {  get; set; }
        public short? CC { get; set; }
        public string? ImageUrl { get; set; }
        public string ThuongHieu { get; set; } = string.Empty;
        public string Loai { get; set; }=string.Empty;
        public string? Tags { get; set; }
    }
}
