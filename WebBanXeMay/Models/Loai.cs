using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Models
{
    public class Loai
    {
        [Key]
        public int MaLoai { get; set; }

        [Required, StringLength(100)]
        public string TenLoai { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Slug { get; set; } = string.Empty; 

        public ICollection<SanPham>? SanPhams { get; set; }
    }
}
