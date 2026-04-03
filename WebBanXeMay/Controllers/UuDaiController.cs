using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Models; // <-- Namespace của bạn
using System.Linq;
using System.Threading.Tasks;

namespace WebBanXeMay.Controllers // <-- Namespace của bạn
{
    public class UuDaiController : Controller
    {
        private readonly AppDbContext _context; // <-- Tên DbContext của bạn

        public UuDaiController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /UuDai
        public async Task<IActionResult> Index()
        {
            var ngayHienTai = DateTime.Now;

            // Lấy tất cả các deal còn hạn
            var activeDeals = await _context.UuDais
                .Include(u => u.SanPham)    
                .Include(u => u.Loai)        // Lấy thông tin loại (nếu có)
                .Include(u => u.ThuongHieu)  // Lấy thông tin thương hiệu (nếu có)
                .Where(u => u.NgayBatDau <= ngayHienTai && u.NgayKetThuc >= ngayHienTai)
                .OrderBy(u => u.NgayKetThuc) // Ưu tiên deal sắp hết hạn
                .ToListAsync();

            return View(activeDeals);
        }
    }
}