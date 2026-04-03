// Vị trí file: Areas/Admin/ViewModels/OrderDetailViewModel.cs

using System;
using System.Collections.Generic;

namespace WebBanXeMay.Areas.Admin.ViewModels
{
    // Lớp cha
    public class OrderDetailViewModel
    {
        public int OrderId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShippingRecipient { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string ShippingPhone { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }

        // ===== THUỘC TÍNH ĐẶT CỌC =====
        public decimal? DepositAmount { get; set; }      // SoTienCoc
        public string? DepositStatus { get; set; }       // TrangThaiCoc (Chuỗi: ChoXacNhanCoc, DaCoc...)
        public DateTime? DepositDueDate { get; set; }    // HanCoc
        public string? DepositMethod { get; set; }       // PhuongThucCoc (MoMo, Tiền mặt...)
        public decimal? RemainingAmount { get; set; }    // TongTien - SoTienCoc
        public bool IsDepositOverdue { get; set; }

        // Danh sách sản phẩm trong đơn
        public List<OrderItemViewModel> Items { get; set; } = new();

        // Danh sách đánh giá gắn với đơn
        public List<ReviewViewModel> Reviews { get; set; } = new();

        // ===== LỊCH SỬ ĐƠN HÀNG (LOGS) =====
        public List<LogItemViewModel> Logs { get; set; } = new();

        // Lớp con cho Sản phẩm
        public class OrderItemViewModel
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public string ImageUrl { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal TotalPrice => Quantity * UnitPrice;

            // === Trường để biết SP đã được đánh giá chưa ===
            public bool HasBeenReviewed { get; set; } = false;
        }

        // Lớp con cho Đánh giá
        public class ReviewViewModel
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

        // Lớp con cho Lịch sử đơn hàng (log)
        public class LogItemViewModel
        {
            public DateTime ThoiGian { get; set; }
            public string NoiDung { get; set; } = string.Empty;
        }
    }
}
