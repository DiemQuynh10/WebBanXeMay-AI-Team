using WebBanXeMay.Models;
using System.Collections.Generic;

namespace WebBanXeMay.Areas.Admin.ViewModels
{
    public class DonHangViewModel
    {
        public DonHang DonHang { get; set; } = new DonHang();
        public List<ChiTietDH> ChiTietDHs { get; set; } = new List<ChiTietDH>();
        public List<SanPham> DanhSachSanPham { get; set; } = new List<SanPham>();
    }
}