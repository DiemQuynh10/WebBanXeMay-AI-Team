using System;
using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Models
{
    public class Voucher
    {
        public int Id { get; set; }

        [Required, StringLength(50)]
        [Display(Name = "Mã giảm giá")]
        public string MaVoucher { get; set; } = null!;

        [StringLength(200)]
        [Display(Name = "Tên voucher")]
        public string? TenVoucher { get; set; }

        [StringLength(500)]
        [Display(Name = "Mô tả")]
        public string? MoTa { get; set; }

        // true = giảm theo %, false = giảm theo số tiền
        [Display(Name = "Giảm theo phần trăm?")]
        public bool IsPercent { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Giá trị giảm phải > 0")]
        [Display(Name = "Giá trị giảm")]
        public decimal GiaTri { get; set; }

        // Dùng cho trường hợp giảm theo %
        [Display(Name = "Giá trị giảm tối đa")]
        public decimal? GiaTriGiamToiDa { get; set; }

        [Display(Name = "Đơn hàng tối thiểu")]
        public decimal? DonHangToiThieu { get; set; }

        [Required]
        [Display(Name = "Ngày bắt đầu")]
        [DataType(DataType.Date)]
        public DateTime NgayBatDau { get; set; }

        [Required]
        [Display(Name = "Ngày kết thúc")]
        [DataType(DataType.Date)]
        public DateTime NgayKetThuc { get; set; }

        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải > 0")]
        [Display(Name = "Số lượng phát hành")]
        public int SoLuong { get; set; }

        [Display(Name = "Số lượng đã dùng")]
        public int SoLuongDaDung { get; set; }

        [Display(Name = "Số lần dùng tối đa / khách")]
        public int? SoLanDungToiDaMoiKhach { get; set; }

        [Display(Name = "Đang hoạt động")]
        public bool IsActive { get; set; }

        [Display(Name = "Đã xóa")]
        public bool IsDeleted { get; set; }
    }
}
