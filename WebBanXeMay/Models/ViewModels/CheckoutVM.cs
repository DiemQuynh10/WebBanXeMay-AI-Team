namespace WebBanXeMay.Models.ViewModels;

using System.ComponentModel.DataAnnotations;

public class CheckoutVM
{
    [Required, StringLength(100)]
    public string NguoiNhan { get; set; } = "";

    [Required, StringLength(20)]
    [RegularExpression(@"^(0|\+84)\d{8,10}$", ErrorMessage = "Số điện thoại không hợp lệ")]
    public string Phone { get; set; } = "";

    [Required, StringLength(300)]
    public string DiaChi { get; set; } = "";

    public string PaymentMethod { get; set; } = "COD";
    public string? PhuongThucCoc { get; set; }   // MoMo / VNPay / ChuyenKhoan
    public bool AgreeDeposit { get; set; }       // tích đồng ý đặt cọc
}
