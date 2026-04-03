using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Cần cho .CountAsync()
using WebBanXeMay.Data; // Cần cho AppDbContext
using WebBanXeMay.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Route("api/admin/dashboard")]
    [ApiController]
    [Area("Admin")]
    public class DashboardApiController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DashboardApiController(AppDbContext context)
        {
            _context = context;
        }

        // API endpoint: /api/admin/dashboard/revenue-by-month
        [HttpGet("revenue-by-month")]
        public async Task<IActionResult> GetRevenueByMonth()
        {
            var today = DateTime.Today;
            // Lấy 12 tháng gần nhất (bao gồm tháng hiện tại)
            var last12Months = Enumerable.Range(0, 12)
                .Select(i => today.AddMonths(-i))
                .OrderBy(d => d);

            var monthlyRevenue = await _context.DonHangs
                .Where(o => o.TrangThai == TrangThaiDonHang.HoanTat && o.NgayDH >= last12Months.Last())
                .GroupBy(o => new { o.NgayDH.Year, o.NgayDH.Month })
                .Select(g => new {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Total = g.Sum(o => o.TongTien)
                })
                .ToListAsync();

            var result = last12Months.Select(m => new {
                Label = m.ToString("MM/yyyy"),
                Value = monthlyRevenue.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Total ?? 0
            });

            return Ok(result);
        }

        // API endpoint: /api/admin/dashboard/order-status-distribution
        [HttpGet("order-status-distribution")]
        public async Task<IActionResult> GetOrderStatusDistribution()
        {
            var statusCounts = await _context.DonHangs
                .GroupBy(o => o.TrangThai)
                .Select(g => new
                {
                    Status = g.Key.ToString(),
                    Count = g.Count()
                })
                .ToListAsync();

            var allStatuses = Enum.GetNames(typeof(TrangThaiDonHang));

            var result = allStatuses.Select(statusName => new
            {
                Label = statusName,
                Value = statusCounts.FirstOrDefault(sc => sc.Status == statusName)?.Count ?? 0
            });

            return Ok(result);
        }
    }
}