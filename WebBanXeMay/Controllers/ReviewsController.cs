using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebBanXeMay.Models;
using System.Threading.Tasks;

namespace WebBanXeMay.Controllers
{

    [Authorize]
    public class ReviewsController : Controller
    {
        private readonly AppDbContext _context;
        public ReviewsController(AppDbContext context) => _context = context;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int maDH, int maSP, int rating, string comment)
        {
            var userName = User.Identity?.Name ?? "Khách";
            var review = new DanhGia
            {
                MaDH = maDH,
                MaSP = maSP,
                DiemDanhGia = rating,
                NoiDung = comment,
                TenNguoiDanhGia = userName,
                NgayDanhGia = DateTime.Now
            };
            _context.DanhGias.Add(review);
            await _context.SaveChangesAsync();

            TempData["Success"] = "✅ Cảm ơn bạn đã gửi đánh giá!";
            return RedirectToAction("Details", "Orders", new { area = "User", id = maDH });
        }

    }

}