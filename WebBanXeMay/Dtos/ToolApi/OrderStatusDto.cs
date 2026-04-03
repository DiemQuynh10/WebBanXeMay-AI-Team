namespace WebBanXeMay.Dtos.ToolApi
{
    public class OrderStatusDto
    {
        public int MaDH { get; set; }
        public DateTime NgayDH { get; set; }
        public decimal TongTien { get; set; }
        public string TrangThai { get; set; } = "";

        public string? NguoiNhan { get; set; }
        public string? Phone { get; set; }
        public string? DiaChi { get; set; }

        public decimal? SoTienCoc { get; set; }
        public string TrangThaiCoc { get; set; } = "";
        public DateTime? HanCoc { get; set; }
        public string? PhuongThucCoc { get; set; }
        public string? MaGiaoDichCoc { get; set; }
        public DateTime? NgayCoc { get; set; }
        public DateTime? ThoiGianYeuCauCoc { get; set; }
        public DateTime? ThoiGianXacNhanCoc { get; set; }
        public string? GhiChuCoc { get; set; }

        public decimal? SoTienGiam { get; set; }
        public string? MaVoucher { get; set; }
        public int? VoucherId { get; set; }

        public List<OrderItemDto> Items { get; set; } = new();
    }

    public class OrderItemDto
    {
        public int MaSP { get; set; }
        public string TenSP { get; set; } = "";
        public int SoLuong { get; set; }
        public decimal DonGia { get; set; }
        public decimal ThanhTien => DonGia * SoLuong;
    }
}