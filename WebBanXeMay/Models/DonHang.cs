using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebBanXeMay.Models
{
    public enum TrangThaiDonHang
    {
        ChoXacNhan = 0,
        DangXuLy = 1,
        DangGiao = 2,
        HoanTat = 3,
        DaHuy = 4
    }
    public enum TrangThaiCoc
    {
        ChuaCoc = 0,
        ChoXacNhanCoc = 1,
        DaCoc = 2,
        HoanCoc = 3,
        MatCoc = 4
    }
    public class DonHang
    {
        [Key]
        public int MaDH { get; set; }

        // FK -> IdentityUser
        [Required]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        [Column(TypeName = "datetime")]
        public DateTime NgayDH { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18,2)")]
        public decimal TongTien { get; set; }

        public TrangThaiDonHang TrangThai { get; set; } = TrangThaiDonHang.ChoXacNhan;

        [StringLength(100)] public string? NguoiNhan { get; set; }
        [StringLength(20)] public string? Phone { get; set; }
        [StringLength(300)] public string? DiaChi { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? SoTienCoc { get; set; }          // Mức tiền phải đặt cọc

        public TrangThaiCoc TrangThaiCoc { get; set; } = TrangThaiCoc.ChuaCoc;

        public DateTime? HanCoc { get; set; }            // Ví dụ: +3 ngày kể từ khi tạo đơn

        [StringLength(50)]
        public string? PhuongThucCoc { get; set; }       // "MoMo", "VNPay", "ChuyenKhoan"

        [StringLength(100)]
        public string? MaGiaoDichCoc { get; set; }       // mã giao dịch trả về từ cổng thanh toán

        public DateTime? NgayCoc { get; set; }           // thời điểm đã cọc
                                                         // --- MỚI THÊM ---
        public DateTime? ThoiGianYeuCauCoc { get; set; }     // lúc user bấm "đã chuyển khoản"
        public DateTime? ThoiGianXacNhanCoc { get; set; }    // lúc admin xác nhận

        [StringLength(300)]
        public string? GhiChuCoc { get; set; }               // user ghi: chuyển từ tk nào, nội dung gì

        [Column(TypeName = "decimal(18,2)")]
        public decimal? SoTienGiam { get; set; }      // số tiền giảm từ voucher

        [StringLength(50)]
        public string? MaVoucher { get; set; }        // mã voucher đã dùng

        public int? VoucherId { get; set; }           // FK sang bảng Voucher (nếu có)
        public Voucher? Voucher { get; set; }

        public ICollection<ChiTietDH> ChiTietDHs { get; set; } = new List<ChiTietDH>();
    }
}
