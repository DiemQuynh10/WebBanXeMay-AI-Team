using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;

namespace WebBanXeMay.Controllers
{
    // Controller dành cho KHÁCH HÀNG (không Area)
    public class VoucherController : Controller
    {
        private readonly AppDbContext _context;

        public VoucherController(AppDbContext context)
        {
            _context = context;
        }

        // ==========================
        // 1. Trang kho voucher cho user: GET /Voucher
        // ==========================
        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now;

            // Chỉ lấy các voucher còn dùng được
            var vouchers = await _context.Vouchers
                .Where(v =>
                    !v.IsDeleted &&
                    v.IsActive &&
                    v.NgayBatDau <= now &&
                    v.NgayKetThuc >= now &&
                    v.SoLuongDaDung < v.SoLuong
                )
                .OrderBy(v => v.NgayKetThuc)
                .ToListAsync();

            return View(vouchers);   // View: Views/Voucher/Index.cshtml (kho ưu đãi)
        }

        // ==========================
        // 2. API áp dụng voucher khi thanh toán (AJAX): POST /Voucher/Apply
        // ==========================
        [HttpPost]
        public async Task<IActionResult> Apply(string code, decimal orderTotal)
        {
            code = code?.Trim();

            if (string.IsNullOrEmpty(code))
            {
                return Json(new
                {
                    success = false,
                    message = "Vui lòng nhập mã giảm giá."
                });
            }

            var now = DateTime.Now;

            // Tìm voucher theo mã (không tính cái đã xóa)
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v =>
                    v.MaVoucher == code &&
                    !v.IsDeleted);

            if (voucher == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá không tồn tại."
                });
            }

            if (!voucher.IsActive)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá này đã bị tắt."
                });
            }

            if (voucher.NgayBatDau > now || voucher.NgayKetThuc < now)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá đã hết hạn hoặc chưa đến thời gian sử dụng."
                });
            }

            if (voucher.SoLuongDaDung >= voucher.SoLuong)
            {
                return Json(new
                {
                    success = false,
                    message = "Mã giảm giá đã được sử dụng hết."
                });
            }

            if (voucher.DonHangToiThieu.HasValue &&
                orderTotal < voucher.DonHangToiThieu.Value)
            {
                return Json(new
                {
                    success = false,
                    message = $"Đơn tối thiểu để dùng mã là {voucher.DonHangToiThieu.Value:N0} đ."
                });
            }

            // TODO: Nếu bạn có logic giới hạn số lần dùng / khách,
            // ở đây sẽ kiểm tra theo UserId (để sau mình có thể thêm).

            // ===== TÍNH SỐ TIỀN GIẢM =====
            decimal discount;

            if (voucher.IsPercent)
            {
                discount = orderTotal * voucher.GiaTri / 100m;

                if (voucher.GiaTriGiamToiDa.HasValue &&
                    discount > voucher.GiaTriGiamToiDa.Value)
                {
                    discount = voucher.GiaTriGiamToiDa.Value;
                }
            }
            else
            {
                discount = voucher.GiaTri;
            }

            if (discount > orderTotal)
                discount = orderTotal;

            var finalTotal = orderTotal - discount;

            return Json(new
            {
                success = true,
                message = "Áp dụng voucher thành công.",
                discount = discount,
                final = finalTotal,
                code = voucher.MaVoucher,
                voucherId = voucher.Id
            });
        }
    }
}
