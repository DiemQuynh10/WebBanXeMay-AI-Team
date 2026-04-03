using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")]
    // [Authorize(Roles = "Admin")] // nếu bạn dùng phân quyền thì bật lên
    public class VoucherController : Controller
    {
        private readonly AppDbContext _context;

        public VoucherController(AppDbContext context)
        {
            _context = context;
        }

        // =======================
        // 1. Danh sách voucher
        // =======================
        public async Task<IActionResult> Index(string? search, string? status)
        {
            var query = _context.Vouchers
                                .Where(v => !v.IsDeleted)
                                .OrderByDescending(v => v.NgayBatDau)
                                .AsQueryable();

            // Tìm kiếm theo mã / tên
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(v => v.MaVoucher.Contains(search)
                                      || (v.TenVoucher ?? "").Contains(search));
            }

            // Lọc theo trạng thái (tuỳ cách bạn đặt param trên view)
            // status: "active", "expired", "inactive"
            var now = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(status))
            {
                switch (status.ToLower())
                {
                    case "active":
                        query = query.Where(v => v.IsActive
                                              && v.NgayBatDau <= now
                                              && v.NgayKetThuc >= now);
                        break;

                    case "expired":
                        query = query.Where(v => v.NgayKetThuc < now);
                        break;

                    case "inactive":
                        query = query.Where(v => !v.IsActive);
                        break;
                }
            }

            var vouchers = await query.ToListAsync();
            return View(vouchers);
        }

        // =======================
        // 2. Chi tiết (nếu cần)
        // =======================
        public async Task<IActionResult> Details(int id)
        {
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (voucher == null) return NotFound();
            return View(voucher);
        }

        // =======================
        // 3. Tạo mới - GET
        // =======================
        public IActionResult Create()
        {
            // mặc định ngày
            var model = new Voucher
            {
                NgayBatDau = DateTime.Today,
                NgayKetThuc = DateTime.Today.AddDays(7),
                IsActive = true,
                SoLuong = 1
            };
            return View(model);
        }

        // =======================
        // 3. Tạo mới - POST
        // =======================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Voucher voucher)
        {
            // 1. Kiểm tra trùng mã (không tính cái đã xóa)
            if (await _context.Vouchers
                .AnyAsync(v => v.MaVoucher == voucher.MaVoucher && !v.IsDeleted))
            {
                ModelState.AddModelError("MaVoucher", "Mã giảm giá này đã tồn tại.");
            }

            // 2. Check ngày
            if (voucher.NgayKetThuc < voucher.NgayBatDau)
            {
                ModelState.AddModelError("NgayKetThuc", "Ngày kết thúc phải >= ngày bắt đầu.");
            }

            // 3. Check giá trị theo nghiệp vụ
            if (voucher.IsPercent)
            {
                if (voucher.GiaTri <= 0 || voucher.GiaTri > 100)
                {
                    ModelState.AddModelError("GiaTri", "Giảm theo % phải trong khoảng 1-100.");
                }
                if (voucher.GiaTriGiamToiDa.HasValue && voucher.GiaTriGiamToiDa <= 0)
                {
                    ModelState.AddModelError("GiaTriGiamToiDa", "Giá trị giảm tối đa phải > 0.");
                }
            }
            else
            {
                if (voucher.GiaTri <= 0)
                {
                    ModelState.AddModelError("GiaTri", "Số tiền giảm phải > 0.");
                }
            }

            if (voucher.SoLuong <= 0)
            {
                ModelState.AddModelError("SoLuong", "Số lượng phát hành phải > 0.");
            }

            if (!ModelState.IsValid)
            {
                return View(voucher);
            }

            // Giá trị khởi tạo
            voucher.SoLuongDaDung = 0;
            voucher.IsDeleted = false;

            _context.Vouchers.Add(voucher);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // =======================
        // 4. Sửa voucher - GET
        // =======================
        public async Task<IActionResult> Edit(int id)
        {
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (voucher == null) return NotFound();

            return View(voucher);
        }

        // =======================
        // 4. Sửa voucher - POST
        // =======================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Voucher input)
        {
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (voucher == null) return NotFound();

            // Kiểm tra trùng mã với thằng khác
            if (await _context.Vouchers
                .AnyAsync(v => v.Id != id
                            && v.MaVoucher == input.MaVoucher
                            && !v.IsDeleted))
            {
                ModelState.AddModelError("MaVoucher", "Mã giảm giá này đã tồn tại.");
            }

            if (input.NgayKetThuc < input.NgayBatDau)
            {
                ModelState.AddModelError("NgayKetThuc", "Ngày kết thúc phải >= ngày bắt đầu.");
            }

            if (input.IsPercent)
            {
                if (input.GiaTri <= 0 || input.GiaTri > 100)
                {
                    ModelState.AddModelError("GiaTri", "Giảm theo % phải trong khoảng 1-100.");
                }
                if (input.GiaTriGiamToiDa.HasValue && input.GiaTriGiamToiDa <= 0)
                {
                    ModelState.AddModelError("GiaTriGiamToiDa", "Giá trị giảm tối đa phải > 0.");
                }
            }
            else
            {
                if (input.GiaTri <= 0)
                {
                    ModelState.AddModelError("GiaTri", "Số tiền giảm phải > 0.");
                }
            }

            if (input.SoLuong <= 0)
            {
                ModelState.AddModelError("SoLuong", "Số lượng phát hành phải > 0.");
            }

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            // Cập nhật từng field để không bị ghi đè lung tung
            voucher.MaVoucher = input.MaVoucher;
            voucher.TenVoucher = input.TenVoucher;
            voucher.MoTa = input.MoTa;
            voucher.IsPercent = input.IsPercent;
            voucher.GiaTri = input.GiaTri;
            voucher.GiaTriGiamToiDa = input.GiaTriGiamToiDa;
            voucher.DonHangToiThieu = input.DonHangToiThieu;
            voucher.NgayBatDau = input.NgayBatDau;
            voucher.NgayKetThuc = input.NgayKetThuc;
            voucher.SoLuong = input.SoLuong;
            voucher.IsActive = input.IsActive;
            voucher.SoLanDungToiDaMoiKhach = input.SoLanDungToiDaMoiKhach;
            // SoLuongDaDung giữ nguyên (do tính theo đơn hàng)

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // =======================
        // 5. Xoá (soft delete)
        // =======================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null) return NotFound();

            // Soft delete: chỉ ẩn, không xoá khỏi DB
            voucher.IsDeleted = true;
            voucher.IsActive = false;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // =======================
        // 6. Bật / tắt nhanh voucher
        // =======================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

            if (voucher == null) return NotFound();

            voucher.IsActive = !voucher.IsActive;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
