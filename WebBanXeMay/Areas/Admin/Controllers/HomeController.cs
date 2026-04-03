using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "AdminOnly")]
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        // range: "month", "3m", "12m", "custom"
        public async Task<IActionResult> Index(string range = "month", DateTime? from = null, DateTime? to = null)
        {
            var now = DateTime.Now;
            DateTime start, end;

            // =========================
            // XỬ LÝ KHOẢNG THỜI GIAN
            // =========================
            switch (range)
            {
                case "3m":
                    // 3 tháng gần nhất (gồm tháng hiện tại)
                    start = new DateTime(now.Year, now.Month, 1).AddMonths(-2);
                    end = start.AddMonths(3);
                    ViewBag.RangeLabel = "3 tháng gần nhất";
                    break;

                case "12m":
                    // 12 tháng gần nhất
                    start = new DateTime(now.Year, now.Month, 1).AddMonths(-11);
                    end = start.AddMonths(12);
                    ViewBag.RangeLabel = "12 tháng gần nhất";
                    break;

                case "custom":
                    if (from.HasValue && to.HasValue)
                    {
                        start = from.Value.Date;
                        // end là ngày +1 để so sánh < end
                        end = to.Value.Date.AddDays(1);
                        ViewBag.RangeLabel = $"{start:dd/MM/yyyy} - {to.Value:dd/MM/yyyy}";
                    }
                    else
                    {
                        // Nếu custom nhưng không truyền đủ thì fallback về tháng này
                        start = new DateTime(now.Year, now.Month, 1);
                        end = start.AddMonths(1);
                        ViewBag.RangeLabel = "Tháng này";
                        range = "month";
                    }
                    break;

                default: // "month"
                    start = new DateTime(now.Year, now.Month, 1);
                    end = start.AddMonths(1);
                    ViewBag.RangeLabel = "Tháng này";
                    break;
            }

            ViewBag.Range = range;
            ViewBag.FromDate = range == "custom" && from.HasValue
                ? from.Value.ToString("yyyy-MM-dd")
                : null;
            ViewBag.ToDate = range == "custom" && to.HasValue
                ? to.Value.ToString("yyyy-MM-dd")
                : null;

            // =========================
            // 1. THỐNG KÊ NHANH
            // =========================
            ViewBag.TongXe = await _context.SanPhams.CountAsync();
            ViewBag.KhachHang = await _context.Users.CountAsync();
            ViewBag.DonHangCho = await _context.DonHangs.CountAsync(d => d.TrangThai == TrangThaiDonHang.ChoXacNhan);
            ViewBag.DoanhThu = await _context.DonHangs
                .Where(d => d.TrangThai == TrangThaiDonHang.HoanTat)
                .SumAsync(d => (decimal?)d.TongTien) ?? 0;

            // CẢNH BÁO KHO & ĐƠN QUÁ HẠN
            ViewBag.LowStockProducts = await _context.SanPhams
                .Where(p => p.IsActive && p.SoLuong > 0 && p.SoLuong <= 5)
                .CountAsync();

            ViewBag.OutOfStockButActive = await _context.SanPhams
                .Where(p => p.IsActive && p.SoLuong <= 0)
                .CountAsync();

            var choXacNhanTimeout = now.AddHours(-24);   // chờ xác nhận > 24h
            var dangXuLyTimeout = now.AddDays(-3);     // đang xử lý > 3 ngày

            ViewBag.OverdueConfirmOrders = await _context.DonHangs
                .Where(d => d.TrangThai == TrangThaiDonHang.ChoXacNhan
                            && d.NgayDH <= choXacNhanTimeout)
                .CountAsync();

            ViewBag.OverdueProcessingOrders = await _context.DonHangs
                .Where(d => d.TrangThai == TrangThaiDonHang.DangXuLy
                            && d.NgayDH <= dangXuLyTimeout)
                .CountAsync();

            // =========================
            // 2. BIỂU ĐỒ DOANH THU THEO THÁNG
            //     (theo khoảng thời gian đã chọn)
            // =========================
            var revenueQuery = await _context.DonHangs
                .Where(d => d.TrangThai == TrangThaiDonHang.HoanTat
                            && d.NgayDH >= start && d.NgayDH < end)
                .GroupBy(d => new { d.NgayDH.Year, d.NgayDH.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Tong = g.Sum(x => x.TongTien)
                })
                .OrderBy(g => g.Year)
                .ThenBy(g => g.Month)
                .ToListAsync();

            // Labels & Values cho Chart.js
            ViewBag.Labels = string.Join(",",
                revenueQuery.Select(x => $"'Tháng {x.Month}/{x.Year}'"));
            ViewBag.Values = string.Join(",",
                revenueQuery.Select(x => x.Tong));

            // =========================
            // 3. TOP 5 XE BÁN CHẠY (TOÀN BỘ)
            // =========================
            var topXe = await _context.ChiTietDHs
                .Include(c => c.SanPham)
                .Where(c => c.DonHang.TrangThai == TrangThaiDonHang.HoanTat)
                .GroupBy(c => c.SanPham!.TenSP)
                .Select(g => new
                {
                    TenSP = g.Key,
                    TongSoLuong = g.Sum(x => x.SoLuong)
                })
                .OrderByDescending(x => x.TongSoLuong)
                .Take(5)
                .ToListAsync();
            ViewBag.TopXe = topXe;

            // =========================
            // 4. 5 ĐƠN HÀNG MỚI NHẤT
            // =========================
            var donMoi = await _context.DonHangs
                .Include(d => d.User)
                .OrderByDescending(d => d.NgayDH)
                .Take(5)
                .ToListAsync();
            ViewBag.DonMoi = donMoi;

            // =========================
            // 5. BIỂU ĐỒ TRÒN TRẠNG THÁI ĐƠN HÀNG
            // =========================
            var trangThaiThongKe = await _context.DonHangs
                .GroupBy(d => d.TrangThai)
                .Select(g => new
                {
                    Ten = g.Key.ToString(),
                    SoLuong = g.Count()
                })
                .ToListAsync();

            ViewBag.TrangThaiLabel = string.Join(",",
                trangThaiThongKe.Select(x => $"'{x.Ten}'"));
            ViewBag.TrangThaiValue = string.Join(",",
                trangThaiThongKe.Select(x => x.SoLuong));

            return View();
        }
    }
}
