using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Models
{
    public class TuVan
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập họ tên")]
        public string HoTen { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập email")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
        public string SoDienThoai { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập nội dung tư vấn")]
        public string NoiDung { get; set; }

        public DateTime NgayGui { get; set; } = DateTime.Now;
    }
}
