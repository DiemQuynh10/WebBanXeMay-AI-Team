using System;

namespace WebBanXeMay.Models
{
    public class OrderLog
    {
        public int Id { get; set; }

        public int MaDH { get; set; }          // Khóa ngoại đến DonHang
        public DonHang? DonHang { get; set; }

        public string NoiDung { get; set; } = "";   // Ví dụ: "Tạo đơn hàng", "Đã cọc", "Hoàn cọc"

        public DateTime ThoiGian { get; set; } = DateTime.Now;
    }
}
