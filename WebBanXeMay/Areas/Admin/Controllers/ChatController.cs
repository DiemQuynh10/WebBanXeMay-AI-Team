using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebBanXeMay.Areas.Admin.ViewModels;
using WebBanXeMay.Data;
using WebBanXeMay.Models;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "AdminOnly")]
    public class ChatController : Controller
    {
        private readonly AppDbContext _context;

        public ChatController(AppDbContext context)
        {
            _context = context;
        }

        // Màn quản lý chat: danh sách khách + badge chưa đọc
        public async Task<IActionResult> Manage()
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Lấy tất cả user ĐÃ TỪNG CHAT, trừ chính admin
            var users = await _context.Users
                .Where(u =>
                    u.Id != adminId &&                                      // 🔴 LOẠI ADMIN
                    _context.Messages.Any(m => m.SenderId == u.Id ||
                                              m.ReceiverId == u.Id))
                .Select(u => new UserChatViewModel
                {
                    UserId = u.Id,
                    UserName = u.UserName ?? "Khách hàng",
                    Avatar = "/images/avatar-default.png",

                    // Số tin KHÁCH gửi cho shop mà chưa đọc
                    UnreadCount = _context.Messages.Count(m =>
                        m.SenderId == u.Id &&
                        !m.IsReadByAdmin),

                    // Nội dung tin cuối
                    LastMessage = _context.Messages
                        .Where(m => m.SenderId == u.Id || m.ReceiverId == u.Id)
                        .OrderByDescending(m => m.Timestamp)
                        .Select(m => m.Content)
                        .FirstOrDefault(),

                    // Thời gian tin cuối
                    LastMessageTime = _context.Messages
                        .Where(m => m.SenderId == u.Id || m.ReceiverId == u.Id)
                        .OrderByDescending(m => m.Timestamp)
                        .Select(m => (DateTime?)m.Timestamp)
                        .FirstOrDefault()
                })
                .OrderByDescending(x => x.LastMessageTime)
                .ToListAsync();

            return View(users);
        }


        // API lấy lịch sử chat với 1 khách + đánh dấu đã đọc
        [HttpGet]
        public async Task<IActionResult> GetHistory(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest();

            // Lấy toàn bộ tin giữa user này và shop (bất kể receiver là ai)
            var messages = await _context.Messages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            // Những tin KHÁCH gửi mà chưa đọc -> đánh dấu đã đọc
            var unread = messages
                .Where(m => m.SenderId == userId && !m.IsReadByAdmin)
                .ToList();

            if (unread.Any())
            {
                foreach (var msg in unread)
                {
                    msg.IsReadByAdmin = true;
                }
                await _context.SaveChangesAsync();
            }

            return Json(messages);
        }
    }
}
