using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required, StringLength(100)]
        public string TenHienThi { get; set; } = string.Empty;

        public string? AnhDaiDien { get; set; }
    }
}