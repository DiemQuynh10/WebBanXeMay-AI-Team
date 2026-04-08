using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebBanXeMay.Models
{
    public class SanPham
    {
        [Key]
        public int MaSP { get; set; }

        [Required, StringLength(150)]
        public string TenSP { get; set; } = string.Empty;

        [Required, StringLength(150)]
        public string Slug { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Gia { get; set; }

        public int SoLuong { get; set; }
        public short? CC { get; set; }

        [StringLength(300)]
        public string? ImageUrl { get; set; }

        public string? MoTa { get; set; }

        public bool IsActive { get; set; } = true;

        // FK
        public int MaLoai { get; set; }
        public Loai? Loai { get; set; }

        public int MaTH { get; set; }
        public ThuongHieu? ThuongHieu { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }
        public string? Tags { get; set; }

        // ĐÃ XÓA KHỐI OPERATOR GÂY LỖI TẠI ĐÂY
    }
}