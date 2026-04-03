using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Areas.Admin.ViewModels
{
    public class ProductCreateViewModel
    {
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [MaxLength(200)]
        public string TenSP { get; set; }= string.Empty;

        [Required(ErrorMessage = "Giá bán là bắt buộc")]
        [Range(1, double.MaxValue, ErrorMessage = "Giá bán phải lớn hơn 0")]
        public decimal Gia { get; set; }

        [Required(ErrorMessage = "Số lượng là bắt buộc")]
        [Range(0, int.MaxValue, ErrorMessage = "Số lượng không được âm")]
        public int SoLuong { get; set; }

        public short? CC { get; set; }

        public string? MoTa { get; set; }
        public bool IsActive { get; set; } = true;

        [Required(ErrorMessage = "Vui lòng chọn loại xe")]
        public int MaLoai { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn hãng xe")]
        public int MaTH { get; set; }
    }
}