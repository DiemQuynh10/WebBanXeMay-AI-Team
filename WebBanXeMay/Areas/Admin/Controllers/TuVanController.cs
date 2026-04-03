using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Models;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")] // ⚡ Thêm dòng này
    public class TuVanController : Controller
    {
        private readonly AppDbContext _context;

        public TuVanController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var list = await _context.TuVans
                .OrderByDescending(t => t.NgayGui)
                .ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> Details(int id)
        {
            var tv = await _context.TuVans.FindAsync(id);
            if (tv == null) return NotFound();
            return View(tv);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var tv = await _context.TuVans.FindAsync(id);
            if (tv == null) return NotFound();

            _context.TuVans.Remove(tv);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Đã xoá yêu cầu tư vấn.";
            return RedirectToAction(nameof(Index));
        }
    }
}
