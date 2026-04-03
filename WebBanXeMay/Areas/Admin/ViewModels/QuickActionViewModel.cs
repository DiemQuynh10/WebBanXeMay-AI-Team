namespace WebBanXeMay.Models.AdminViewModels
{
    public class QuickActionViewModel
    {
        public int XeTonKho { get; set; }
        public int DonHangCham { get; set; }
        public int XeTonKhoQuaNgay { get; set; }
        public int XeKhongBan { get; set; }
    }

    public class ReportFilterDto
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}