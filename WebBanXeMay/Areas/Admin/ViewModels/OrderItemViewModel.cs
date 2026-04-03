namespace WebBanXeMay.Areas.Admin.ViewModels
{
    // Lớp này chứa thông tin của MỘT dòng sản phẩm trong chi tiết đơn hàng
    public class OrderItemViewModel
    {
        public string ProductName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice => Quantity * UnitPrice; // Thuộc tính tự tính toán
    }
}