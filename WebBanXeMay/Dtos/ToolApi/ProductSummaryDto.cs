namespace WebBanXeMay.Dtos.ToolApi
{
    public class ProductSummaryDto
    {
        public int Id { get; set; }
        public string Ten { get; set; } = "";
        public string Slug { get; set; } = "";
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
        public short? CC { get; set; }

        public string? ImageUrl { get; set; }
        public string ThuongHieu { get; set; } = "";
        public string Loai { get; set; } = "";
        public string? Tags { get; set; }
    }
}