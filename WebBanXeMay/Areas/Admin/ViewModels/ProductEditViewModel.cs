using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Areas.Admin.ViewModels
{
    public class ProductEditViewModel
    {
        public int MaSP { get; set; }

        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc.")]
        public string TenSP { get; set; } = string.Empty; // Sửa ở đây

        [Required(ErrorMessage = "Giá bán là bắt buộc.")]
        public decimal Gia { get; set; } // Giữ nguyên là int

        [Required(ErrorMessage = "Số lượng là bắt buộc.")]
        public int SoLuong { get; set; }

        public string? MoTa { get; set; } // Thêm dấu ? cho thuộc tính không bắt buộc
        public short? CC { get; set; }   // Thêm dấu ? cho thuộc tính không bắt buộc
        public string? ImageUrl { get; set; } // Thêm dấu ? cho thuộc tính không bắt buộc

        public IFormFile? NewImageFile { get; set; }
        public bool IsActive { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn loại xe.")]
        public int MaLoai { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn hãng xe.")]
        public int MaTH { get; set; }

        public SelectList? LoaiXeList { get; set; }
        public SelectList? HangXeList { get; set; }
    }
}