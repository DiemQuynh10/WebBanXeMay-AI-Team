namespace WebBanXeMay.Models
{
    public class CartLine
    {
        public int Id { get; set; }

        // FK -> Cart
        public int CartId { get; set; }
        public Cart? Cart { get; set; }

        public int MaSP { get; set; }
        public int SoLuong { get; set; }
        public decimal Gia { get; set; }  
        public string TenSP { get; set; } = "";
        public string? ImageUrl { get; set; }

        // 👇 THÊM MỚI: lưu slug để link sang trang chi tiết
        public string? Slug { get; set; }
    }
}