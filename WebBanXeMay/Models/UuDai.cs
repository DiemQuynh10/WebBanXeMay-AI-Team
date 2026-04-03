using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// Đảm bảo namespace này là đúng với dự án của bạn
namespace WebBanXeMay.Models
{
    // Enum để định nghĩa loại khuyến mãi
    public enum LoaiKhuyenMai
    {
        [Display(Name = "Giảm theo %")]
        PhanTram,
        [Display(Name = "Giảm tiền trực tiếp")]
        SoTienGiam,
        [Display(Name = "Tặng quà")]
        QuaTang
    }

    public class UuDai
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên ưu đãi")]
        [StringLength(100)]
        public string TenUuDai { get; set; } = string.Empty; // Thêm = string.Empty để sửa cảnh báo CS8618

        [Required(ErrorMessage = "Vui lòng nhập mô tả")]
        [StringLength(500)]
        public string MoTa { get; set; } = string.Empty; // Thêm = string.Empty để sửa cảnh báo CS8618

        public string? HinhAnhUrl { get; set; }

        // ==========================================================
        // ĐÂY LÀ THUỘC TÍNH SỐ 1 (Enum) - CHÚNG TA DÙNG TÊN "LoaiKM"
        // ==========================================================
        [Required]
        public LoaiKhuyenMai LoaiKM { get; set; } // Đổi tên từ "Loai" thành "LoaiKM"

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal GiaTri { get; set; }

        [Required]
        public DateTime NgayBatDau { get; set; }
        [Required]
        public DateTime NgayKetThuc { get; set; }

        // --- Các khóa ngoại ---

        public int? SanPhamId { get; set; }
        [ForeignKey("SanPhamId")]
        public virtual SanPham? SanPham { get; set; }

        // ==========================================================
        // ĐÂY LÀ THUỘC TÍNH SỐ 2 (Liên kết bảng) - VẪN GIỮ NGUYÊN TÊN "Loai"
        // ==========================================================
        public int? LoaiId { get; set; }
        [ForeignKey("LoaiId")]
        public virtual Loai? Loai { get; set; } // Lỗi của bạn là do file UuDai.cs bị thiếu dòng này

        public int? ThuongHieuId { get; set; }
        [ForeignKey("ThuongHieuId")]
        public virtual ThuongHieu? ThuongHieu { get; set; }
    }
}