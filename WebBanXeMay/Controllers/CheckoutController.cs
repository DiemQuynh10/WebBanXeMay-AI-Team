using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using WebBanXeMay.Models.ViewModels;

namespace WebBanXeMay.Controllers
{
    [Route("checkout")]
    [Authorize] // tất cả action trong Checkout đều yêu cầu đăng nhập
    public class CheckoutController : Controller
    {
        private readonly AppDbContext _db;
        public CheckoutController(AppDbContext db) { _db = db; }

        // ===== Helpers =====
        private string? UserId =>
            User?.Claims.FirstOrDefault(c => c.Type.EndsWith("/nameidentifier"))?.Value;

        private static List<int> ParseIds(string? ids) =>
            string.IsNullOrWhiteSpace(ids)
                ? new List<int>()
                : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var i) ? i : -1)
                    .Where(i => i > 0)
                    .Distinct()
                    .ToList();

        private static decimal CalcShipping(decimal sub) => sub >= 20_000_000m ? 0m : 50_000m;

        // Đặt cọc = 10% tổng đơn, tối thiểu 2.000.000đ, không vượt quá tổng
        private decimal CalcDeposit(decimal total)
        {
            var percent = total * 0.10m;
            var deposit = percent < 2_000_000m ? 2_000_000m : percent;
            if (deposit > total) deposit = total;
            return decimal.Round(deposit, 0);
        }

        private static CartItem ToVM(CartLine x) => new CartItem
        {
            MaSP = x.MaSP,
            TenSP = x.TenSP,
            ImageUrl = x.ImageUrl,
            Gia = x.Gia,
            SoLuong = x.SoLuong
        };

        // ===== GET /checkout?ids=... =====
        [HttpGet("")]
        public async Task<IActionResult> Index([FromQuery] string? ids, CancellationToken ct)
        {
            var uid = UserId!;
            var idList = ParseIds(ids); // danh sách sản phẩm user chọn

            var cart = await _db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == uid, ct);

            if (cart == null || cart.Items.Count == 0)
                return RedirectToAction("Index", "Cart");

            // intersect ids với giỏ
            var cartIds = cart.Items.Select(i => i.MaSP).ToHashSet();
            var selected = (idList.Count > 0)
                ? idList.Where(x => cartIds.Contains(x)).Distinct().ToList()
                : cart.Items.Select(i => i.MaSP).ToList();

            if (selected.Count == 0)
                selected = cart.Items.Select(i => i.MaSP).ToList();

            var lines = cart.Items.Where(i => selected.Contains(i.MaSP)).ToList();
            var items = lines.Select(ToVM).ToList();

            var sub = items.Sum(i => i.Gia * i.SoLuong);
            var ship = CalcShipping(sub);
            var total = sub + ship;
            var deposit = CalcDeposit(total);

            ViewBag.Items = items;
            ViewBag.SubTotal = sub;
            ViewBag.Shipping = ship;
            ViewBag.Total = total;
            ViewBag.Ids = string.Join(',', selected);
            ViewBag.Deposit = deposit;
            ViewBag.DepositNote = "Đặt cọc 10% (tối thiểu 2.000.000đ). Số tiền cọc sẽ được trừ vào hóa đơn khi nhận xe.";

            var now = DateTime.Now;

            var availableVouchers = await _db.Vouchers
                .Where(v => !v.IsDeleted
                         && v.IsActive
                         && v.NgayBatDau <= now
                         && v.NgayKetThuc >= now
                         && v.SoLuongDaDung < v.SoLuong)
                .OrderBy(v => v.NgayKetThuc)
                .ToListAsync(ct);

            ViewBag.AvailableVouchers = availableVouchers;

            return View(new CheckoutVM());
        }

        // ===== POST /checkout/place-order =====
        [ValidateAntiForgeryToken]
        [HttpPost("place-order", Name = "CheckoutPlaceOrder")]
        public async Task<IActionResult> PlaceOrder(
            CheckoutVM model,
            [FromForm] string ids,
            [FromForm] int? voucherId,
            [FromForm] string? voucherCode,
            [FromForm] decimal? voucherDiscount, // chỉ tham khảo, sẽ tính lại trên server
            CancellationToken ct)
        {
            var uid = UserId!;
            var idList = ParseIds(ids);

            // 1) Lấy giỏ
            var cart = await _db.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == uid, ct);

            if (cart == null || cart.Items.Count == 0)
                return RedirectToAction(nameof(Index), new { ids });

            // 2) Lấy các dòng được chọn
            var lines = (idList.Count == 0)
                ? cart.Items.ToList()
                : cart.Items.Where(i => idList.Contains(i.MaSP)).ToList();

            if (lines.Count == 0)
                return RedirectToAction(nameof(Index), new { ids });

            // Lấy lại danh sách voucher để khi lỗi quay lại vẫn có dữ liệu
            var now = DateTime.Now;
            var availableVouchers = await _db.Vouchers
                .Where(v => !v.IsDeleted
                         && v.IsActive
                         && v.NgayBatDau <= now
                         && v.NgayKetThuc >= now
                         && v.SoLuongDaDung < v.SoLuong)
                .OrderBy(v => v.NgayKetThuc)
                .ToListAsync(ct);

            // Helper nạp lại ViewBag khi lỗi
            IActionResult BackToIndex()
            {
                var items0 = lines.Select(ToVM).ToList();
                var sub0 = items0.Sum(i => i.Gia * i.SoLuong);
                var ship0 = CalcShipping(sub0);
                var total0 = sub0 + ship0;
                var deposit0 = CalcDeposit(total0);

                ViewBag.Items = items0;
                ViewBag.SubTotal = sub0;
                ViewBag.Shipping = ship0;
                ViewBag.Total = total0;
                ViewBag.Deposit = deposit0;
                ViewBag.DepositNote = "Đặt cọc 10% (tối thiểu 2.000.000đ). Số tiền cọc sẽ được trừ vào hóa đơn khi nhận xe.";
                ViewBag.Ids = ids;
                ViewBag.AvailableVouchers = availableVouchers;

                return View("Index", model);
            }

            // 3) Validate form + đồng ý cọc
            if (!ModelState.IsValid)
                return BackToIndex();

            if (!model.AgreeDeposit)
            {
                ModelState.AddModelError("", "Vui lòng đồng ý điều khoản đặt cọc.");
                return BackToIndex();
            }

            // 4) Đọc lại giá & kiểm kho
            var maList = lines.Select(l => l.MaSP).Distinct().ToList();
            var products = await _db.SanPhams
                                    .Where(sp => maList.Contains(sp.MaSP))
                                    .ToDictionaryAsync(sp => sp.MaSP, ct);

            foreach (var line in lines)
            {
                if (!products.TryGetValue(line.MaSP, out var sp) || !sp.IsActive)
                    ModelState.AddModelError("", $"Sản phẩm {line.TenSP} không còn bán.");
                else if (sp.SoLuong < line.SoLuong)
                    ModelState.AddModelError("", $"Sản phẩm {sp.TenSP} chỉ còn {sp.SoLuong} chiếc.");
            }
            if (!ModelState.IsValid)
                return BackToIndex();

            // 5) Tính lại tổng
            decimal sub = 0m;
            foreach (var line in lines)
                sub += products[line.MaSP].Gia * line.SoLuong;
            var shipping = CalcShipping(sub);
            var total = sub + shipping;

            // ===== 5b) ÁP DỤNG VOUCHER (tính lại trên server) =====
            decimal discount = 0m;
            Voucher? voucher = null;

            if (voucherId.HasValue && !string.IsNullOrWhiteSpace(voucherCode))
            {
                voucher = await _db.Vouchers
                    .FirstOrDefaultAsync(v =>
                        v.Id == voucherId.Value &&
                        v.MaVoucher == voucherCode &&
                        !v.IsDeleted, ct);

                if (voucher != null)
                {
                    bool valid =
                        voucher.IsActive &&
                        voucher.NgayBatDau <= now &&
                        voucher.NgayKetThuc >= now &&
                        voucher.SoLuongDaDung < voucher.SoLuong &&
                        (!voucher.DonHangToiThieu.HasValue || total >= voucher.DonHangToiThieu.Value);

                    if (valid)
                    {
                        if (voucher.IsPercent)
                        {
                            discount = total * voucher.GiaTri / 100m;
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

                        if (discount > total)
                            discount = total;
                    }
                    else
                    {
                        // voucher không hợp lệ nữa -> bỏ qua
                        discount = 0m;
                        voucher = null;
                    }
                }
            }

            var finalTotal = total - discount;
            if (finalTotal < 0) finalTotal = 0;

            // 6) Tạo đơn trong transaction
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var productsTx = await _db.SanPhams
                                          .Where(sp => maList.Contains(sp.MaSP))
                                          .ToDictionaryAsync(sp => sp.MaSP, ct);

                foreach (var line in lines)
                {
                    var sp = productsTx[line.MaSP];
                    if (!sp.IsActive)
                        throw new InvalidOperationException($"Sản phẩm {sp.TenSP} tạm ngừng bán.");
                    if (line.SoLuong > sp.SoLuong)
                        throw new InvalidOperationException($"Sản phẩm {sp.TenSP} chỉ còn {sp.SoLuong} chiếc.");
                }

                // Đặt cọc tính theo TỔNG SAU GIẢM
                var deposit = CalcDeposit(finalTotal);

                var order = new DonHang
                {
                    UserId = uid,
                    NgayDH = DateTime.Now,
                    TrangThai = TrangThaiDonHang.ChoXacNhan,
                    TongTien = finalTotal,                 // tổng sau giảm

                    NguoiNhan = model.NguoiNhan,
                    Phone = model.Phone,
                    DiaChi = model.DiaChi,

                    // Voucher
                    SoTienGiam = discount > 0 ? discount : null,
                    MaVoucher = voucher?.MaVoucher,
                    VoucherId = voucher?.Id,

                    // Thông tin cọc
                    SoTienCoc = deposit,
                    TrangThaiCoc = TrangThaiCoc.ChuaCoc,
                    HanCoc = DateTime.Now.AddDays(3),
                    PhuongThucCoc = string.IsNullOrWhiteSpace(model.PhuongThucCoc)
                        ? "MoMo"
                        : model.PhuongThucCoc,

                    ChiTietDHs = new List<ChiTietDH>()
                };

                // Trừ kho + thêm chi tiết
                foreach (var line in lines)
                {
                    var sp = productsTx[line.MaSP];
                    sp.SoLuong -= line.SoLuong;
                    _db.SanPhams.Update(sp);

                    order.ChiTietDHs.Add(new ChiTietDH
                    {
                        MaSP = sp.MaSP,
                        SoLuong = line.SoLuong,
                        Gia = sp.Gia
                    });
                }

                _db.DonHangs.Add(order);

                // Cập nhật số lần dùng voucher
                if (voucher != null)
                {
                    voucher.SoLuongDaDung += 1;
                    _db.Vouchers.Update(voucher);
                }

                // Xoá các dòng đã mua khỏi giỏ
                _db.CartLines.RemoveRange(lines);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                // Chuyển sang flow đặt cọc phía user
                return RedirectToAction("Deposit", "Orders", new { id = order.MaDH });
            }
            catch (DbUpdateConcurrencyException)
            {
                await tx.RollbackAsync(ct);
                ModelState.AddModelError("", "Kho vừa thay đổi, vui lòng thử lại.");
            }
            catch (InvalidOperationException ex)
            {
                await tx.RollbackAsync(ct);
                ModelState.AddModelError("", ex.Message);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                ModelState.AddModelError("", "Có lỗi khi đặt hàng. Vui lòng thử lại.");
            }

            // 7) Nếu lỗi → quay lại
            return BackToIndex();
        }

        // ===== GET /checkout/success/{id} =====
        [HttpGet("success/{id:int}")]
        public async Task<IActionResult> Success(int id, CancellationToken ct)
        {
            var uid = UserId!;
            var order = await _db.DonHangs
                .Include(o => o.ChiTietDHs).ThenInclude(ctdh => ctdh.SanPham)
                .FirstOrDefaultAsync(o => o.MaDH == id && o.UserId == uid, ct);

            if (order == null) return NotFound();
            return View(order);
        }

        // Đã bỏ các action cũ liên quan đến cọc ở Checkout
    }
}
