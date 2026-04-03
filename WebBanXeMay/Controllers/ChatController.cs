using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebBanXeMay.Data;
using WebBanXeMay.Models;

namespace WebBanXeMay.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly AppDbContext _context;

        public ChatController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // TODO: CHỖ NÀY M PHẢI ĐIỀU CHỈNH ĐÚNG VỚI TÀI KHOẢN ADMIN CỦA M
            // Ví dụ nếu admin login bằng UserName = "admin"
            var adminId = await _context.Users
                .Where(u => u.UserName == "admin")      // <-- ĐỔI CHO ĐÚNG
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

            ViewBag.ShopId = adminId;   // truyền xuống cho view

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetMyHistory()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return BadRequest();

            var messages = await _context.Messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            return Json(messages);
        }
    }
}
