using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "AdminOnly")]
    public class ReviewsController : Controller
    {
        private readonly AppDbContext _context;

        public ReviewsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Admin/Reviews
        // Danh sách đánh giá + lọc + phân trang
        public async Task<IActionResult> Index(
            string? q,             // từ khóa: tên người đánh giá / nội dung
            bool? approved,        // lọc theo đã duyệt / chưa duyệt
            bool? featured,        // lọc theo nổi bật / không nổi bật
            int page = 1,
            int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize <= 0) pageSize = 10;

            var query = _context.DanhGias
                .Include(d => d.SanPham)
                .Include(d => d.DonHang)
                .AsQueryable();

            // --- Lọc theo từ khóa ---
            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(d =>
                    d.TenNguoiDanhGia.Contains(q) ||
                    d.NoiDung.Contains(q)
                );
            }

            // --- Lọc theo trạng thái duyệt ---
            if (approved.HasValue)
            {
                query = query.Where(d => d.IsApproved == approved.Value);
            }

            // --- Lọc theo trạng thái nổi bật ---
            if (featured.HasValue)
            {
                query = query.Where(d => d.IsFeatured == featured.Value);
            }

            // --- Phân trang ---
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            if (totalPages > 0 && page > totalPages) page = totalPages;

            var items = await query
                .OrderByDescending(d => d.NgayDanhGia)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = totalItems;
            ViewBag.TotalPages = totalPages;

            ViewBag.Q = q;
            ViewBag.Approved = approved;
            ViewBag.Featured = featured;

            return View(items);
        }

        // POST: /Admin/Reviews/Approve/5
        // Chờ duyệt -> Đã duyệt (không có "Bỏ duyệt")
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var review = await _context.DanhGias.FindAsync(id);
            if (review == null)
            {
                TempData["Error"] = "Không tìm thấy đánh giá.";
                return RedirectToAction(nameof(Index));
            }

            if (!review.IsApproved)
            {
                review.IsApproved = true;
                _context.DanhGias.Update(review);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã duyệt đánh giá.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Reviews/Feature/5
        // Chỉ cho phép nổi bật khi ĐÃ DUYỆT
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Feature(int id)
        {
            var review = await _context.DanhGias.FindAsync(id);
            if (review == null)
            {
                TempData["Error"] = "Không tìm thấy đánh giá.";
                return RedirectToAction(nameof(Index));
            }

            if (!review.IsApproved)
            {
                TempData["Error"] = "Cần DUYỆT đánh giá trước khi đặt làm NỔI BẬT.";
                return RedirectToAction(nameof(Index));
            }

            if (!review.IsFeatured)
            {
                review.IsFeatured = true;
                _context.DanhGias.Update(review);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã đặt đánh giá làm NỔI BẬT.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Reviews/UnFeature/5
        // Bỏ trạng thái nổi bật, nhưng vẫn giữ IsApproved = true
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnFeature(int id)
        {
            var review = await _context.DanhGias.FindAsync(id);
            if (review == null)
            {
                TempData["Error"] = "Không tìm thấy đánh giá.";
                return RedirectToAction(nameof(Index));
            }

            if (review.IsFeatured)
            {
                review.IsFeatured = false;
                _context.DanhGias.Update(review);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã bỏ trạng thái NỔI BẬT của đánh giá.";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Reviews/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var review = await _context.DanhGias.FindAsync(id);
            if (review == null)
            {
                TempData["Error"] = "Không tìm thấy đánh giá.";
                return RedirectToAction(nameof(Index));
            }

            _context.DanhGias.Remove(review);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Đã xóa đánh giá.";
            return RedirectToAction(nameof(Index));
        }
    }
}
