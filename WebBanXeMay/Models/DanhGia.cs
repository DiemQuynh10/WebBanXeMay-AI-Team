using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebBanXeMay.Models
{
    public class DanhGia
    {
        [Key]
        public int MaDanhGia { get; set; }

        [ForeignKey("DonHang")]
        public int MaDH { get; set; }

        [ForeignKey("SanPham")]
        public int MaSP { get; set; }

        [Required, StringLength(100)]
        public string TenNguoiDanhGia { get; set; } = string.Empty;

        [StringLength(1000)]
        public string NoiDung { get; set; } = string.Empty;

        [Range(1, 5)]
        public int DiemDanhGia { get; set; }

        public DateTime NgayDanhGia { get; set; } = DateTime.Now;
        [StringLength(500)]
        public string? ImageUrl { get; set; }

        [StringLength(500)]
        public string? VideoUrl { get; set; }

        // Navigation
        public DonHang DonHang { get; set; }
        public SanPham SanPham { get; set; }
        public bool IsAnonymous { get; set; } = false;

        // === THÊM 2 DÒNG NÀY VÀO ===

        // Mặc định là 'false' (chưa duyệt). Admin sẽ đổi thành 'true' 
        // để cho phép hiển thị trên trang chi tiết sản phẩm.
        public bool IsApproved { get; set; } = false;

        // Mặc định là 'false'. Admin sẽ đổi thành 'true' 
        // để hiển thị ở mục "Khách hàng nói gì" ngoài trang chủ.
        public bool IsFeatured { get; set; } = false;
    }
}