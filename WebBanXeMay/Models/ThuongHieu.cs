using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Models
{
    public class ThuongHieu
    {
        [Key]
        public int MaTH { get; set; }

        [Required, StringLength(100)]
        public string TenTH { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Slug { get; set; } = string.Empty;

        public ICollection<SanPham>? SanPhams { get; set; }
    }
}
