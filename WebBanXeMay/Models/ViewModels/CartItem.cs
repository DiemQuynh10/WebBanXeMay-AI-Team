namespace WebBanXeMay.Models.ViewModels
{
    public class CartItem
    {
        public int MaSP { get; set; }
        public string TenSP { get; set; } = "";
        public string? ImageUrl { get; set; }
        public decimal Gia { get; set; }
        public int SoLuong { get; set; }
        public string? Slug { get; set; }
        public decimal ThanhTien => Gia * SoLuong;
    }
}
