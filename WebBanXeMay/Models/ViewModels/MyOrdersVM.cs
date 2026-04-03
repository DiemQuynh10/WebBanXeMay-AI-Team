using WebBanXeMay.Models; // thêm dòng này ở đầu file nếu chưa có

namespace WebBanXeMay.Models.ViewModels
{
    public class MyOrderListItem
    {
        public int MaDH { get; set; }
        public DateTime NgayDH { get; set; }
        public TrangThaiDonHang TrangThai { get; set; }
        public decimal TongTien { get; set; }
        public int SoMatHang { get; set; }
        public decimal? SoTienCoc { get; set; }          // số tiền khách đã/ phải cọc
        public TrangThaiCoc TrangThaiCoc { get; set; }   // trạng thái cọc
        public List<MyOrderPreviewItem> PreviewItems { get; set; } = new();
    }

    public class MyOrderPreviewItem
    {
        public string TenSP { get; set; } = "";
        public int SoLuong { get; set; }
        public string ImageUrl { get; set; } = "";
    }

    // ================= CHI TIẾT ĐƠN HÀNG (CHO VIEW DETAIL) =================

    public class MyOrderDetailsVM
    {
        // dùng đúng tên theo view Razor của m
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } = string.Empty;   // "ChoXacNhan", "HoanTat"...

        public decimal TotalAmount { get; set; }

        // Thông tin giao hàng
        public string? ShippingRecipient { get; set; }
        public string? ShippingPhone { get; set; }
        public string? ShippingAddress { get; set; }

        // Đặt cọc
        public decimal? SoTienCoc { get; set; }
        public TrangThaiCoc TrangThaiCoc { get; set; } = TrangThaiCoc.ChuaCoc;
        public DateTime? HanCoc { get; set; }
        public DateTime? NgayCoc { get; set; }
        public string? PhuongThucCoc { get; set; }
        public string? MaGiaoDichCoc { get; set; }
        public bool IsDepositOverdue { get; set; }


        // Chi tiết sản phẩm + đánh giá
        public List<MyOrderItemVM> Items { get; set; } = new();
        public List<MyOrderReviewVM> Reviews { get; set; } = new();
        public List<OrderLog> Logs { get; set; } = new();
    }

    public class MyOrderItemVM
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;

        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice => UnitPrice * Quantity;

        public bool HasBeenReviewed { get; set; }
    }

    public class MyOrderReviewVM
    {
        public int ReviewId { get; set; }
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? ImageUrl { get; set; }
        public string? VideoUrl { get; set; }

        public bool IsAnonymous { get; set; }
    }
}
