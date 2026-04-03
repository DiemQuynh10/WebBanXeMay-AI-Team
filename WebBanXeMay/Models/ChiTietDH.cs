using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebBanXeMay.Models
{
    public class ChiTietDH
    {
        [Key]
        public int MaCT { get; set; }

        public int MaDH { get; set; }
        public DonHang? DonHang { get; set; }

        public int MaSP { get; set; }
        public SanPham? SanPham { get; set; }

        public int SoLuong { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Gia { get; set; }  // snapshot giá tại thời điểm đặt
    }
}
