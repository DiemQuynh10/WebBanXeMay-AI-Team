using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using WebBanXeMay.Models.ViewModels;

namespace WebBanXeMay.Controllers
{
    public class CartController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        // Constructor: Inject DbContext và UserManager
        public CartController(AppDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // ===== Helpers =====
        private string? UserId =>
            User?.Claims.FirstOrDefault(c => c.Type.EndsWith("/nameidentifier"))?.Value;

        private async Task<Cart> GetOrCreateCartAsync(string userId, CancellationToken ct = default)
        {
            var cart = await _db.Carts
                                .Include(c => c.Items)
                                .FirstOrDefaultAsync(c => c.UserId == userId, ct);

            if (cart == null)
            {
                cart = new Cart { UserId = userId };
                _db.Carts.Add(cart);
                await _db.SaveChangesAsync(ct);
                await _db.Entry(cart).Collection(c => c.Items).LoadAsync(ct);
            }

            return cart;
        }

        private static CartItem ToVM(CartLine x) => new CartItem
        {
            MaSP = x.MaSP,
            TenSP = x.TenSP,
            ImageUrl = x.ImageUrl,
            Gia = x.Gia,
            SoLuong = x.SoLuong,
            Slug = x.Slug
        };

        // ===== DTOs cho JSON body =====
        public class AddCartRequest
        {
            public int maSP { get; set; }
            public int quantity { get; set; } = 1;
        }

        public class UpdateCartRequest
        {
            public int maSP { get; set; }
            public int quantity { get; set; }
        }

        public class RemoveCartRequest
        {
            public int maSP { get; set; }
        }

        // ===== Trang /Cart (xem giỏ): yêu cầu đăng nhập =====
        [Authorize]
        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var cart = await GetOrCreateCartAsync(UserId!, ct);
            var items = cart.Items.Select(ToVM).ToList();
            ViewBag.Total = items.Sum(i => i.ThanhTien);
            return View(items);
        }

        // ===== API Count (cho badge header) – không bắt buộc login =====
        [HttpGet]
        public async Task<IActionResult> Count(CancellationToken ct)
        {
            if (!(User?.Identity?.IsAuthenticated ?? false))
                return Json(new { count = 0, total = 0m });

            var cart = await _db.Carts
                                .Include(c => c.Items)
                                .FirstOrDefaultAsync(c => c.UserId == UserId, ct);

            var count = cart?.Items.Sum(x => x.SoLuong) ?? 0;
            var total = cart?.Items.Sum(x => x.Gia * x.SoLuong) ?? 0m;

            return Json(new { count, total });
        }

        // ===== API Mini (mini-cart) – không bắt buộc login =====
        [HttpGet]
        public async Task<IActionResult> Mini(CancellationToken ct)
        {
            if (!(User?.Identity?.IsAuthenticated ?? false))
                return Json(new { items = Array.Empty<CartItem>(), total = 0m });

            var cart = await _db.Carts
                                .Include(c => c.Items)
                                .FirstOrDefaultAsync(c => c.UserId == UserId, ct);

            var items = cart?.Items.Select(ToVM).ToList() ?? new List<CartItem>();
            var total = items.Sum(i => i.ThanhTien);

            return Json(new { items, total });
        }

        // [GET] /Cart/GetCartItemCount (API Mới thêm để fix lỗi badge)
        [HttpGet]
        public async Task<IActionResult> GetCartItemCount()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Json(new { count = 0 });

            // Đếm số lượng từ CartLines
            var cart = await _db.Carts.Include(c => c.Items)
                                      .FirstOrDefaultAsync(c => c.UserId == user.Id);

            var count = cart?.Items.Sum(x => x.SoLuong) ?? 0;

            return Json(new { count });
        }

        // ===== API JSON: Thêm vào giỏ =====
        [Authorize]
        [HttpPost]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> AddJson([FromBody] AddCartRequest input, CancellationToken ct)
        {
            var qty = input.quantity < 1 ? 1 : input.quantity;

            var sp = await _db.SanPhams.AsNoTracking()
                                       .FirstOrDefaultAsync(x => x.MaSP == input.maSP, ct);
            if (sp == null) return NotFound();

            if (sp.SoLuong <= 0)
                return BadRequest(new { ok = false, message = "Sản phẩm đã hết hàng." });

            var cart = await GetOrCreateCartAsync(UserId!, ct);
            var line = cart.Items.FirstOrDefault(i => i.MaSP == input.maSP);
            var dangTrongGio = line?.SoLuong ?? 0;

            if (dangTrongGio + qty > sp.SoLuong)
            {
                var conLai = Math.Max(0, sp.SoLuong - dangTrongGio);
                return BadRequest(new { ok = false, message = $"Chỉ còn {conLai} chiếc trong kho." });
            }

            if (line == null)
            {
                cart.Items.Add(new CartLine
                {
                    MaSP = sp.MaSP,
                    SoLuong = qty,
                    Gia = sp.Gia,
                    TenSP = sp.TenSP,
                    ImageUrl = string.IsNullOrWhiteSpace(sp.ImageUrl)
                        ? "~/images/hero-showroom.jpg"
                        : sp.ImageUrl,
                    Slug = sp.Slug
                });
            }
            else
            {
                line.SoLuong += qty;
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var count = cart.Items.Sum(x => x.SoLuong);
            var total = cart.Items.Sum(x => x.Gia * x.SoLuong);

            return Json(new { ok = true, count, total });
        }

        // ===== API JSON: Cập nhật số lượng =====
        [Authorize]
        [HttpPost]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> UpdateJson([FromBody] UpdateCartRequest input, CancellationToken ct)
        {
            var cart = await GetOrCreateCartAsync(UserId!, ct);
            var line = cart.Items.FirstOrDefault(i => i.MaSP == input.maSP);
            if (line == null)
                return NotFound(new { ok = false, message = "Không tìm thấy dòng giỏ hàng." });

            var sp = await _db.SanPhams.AsNoTracking()
                                       .FirstOrDefaultAsync(x => x.MaSP == input.maSP, ct);
            if (sp == null) return NotFound();

            var newQty = input.quantity <= 0 ? 0 : input.quantity;

            if (newQty == 0)
            {
                _db.CartLines.Remove(line);
            }
            else
            {
                if (newQty > sp.SoLuong)
                    return BadRequest(new { ok = false, message = $"Chỉ còn {sp.SoLuong} chiếc trong kho." });

                line.SoLuong = newQty;
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var count = cart.Items.Sum(x => x.SoLuong);
            var total = cart.Items.Sum(x => x.Gia * x.SoLuong);

            return Json(new { ok = true, count, total });
        }

        // ===== API JSON: Xoá sản phẩm khỏi giỏ =====
        [Authorize]
        [HttpPost]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> RemoveJson([FromBody] RemoveCartRequest input, CancellationToken ct)
        {
            var cart = await GetOrCreateCartAsync(UserId!, ct);
            var lines = cart.Items.Where(i => i.MaSP == input.maSP).ToList();

            if (lines.Count > 0)
            {
                _db.CartLines.RemoveRange(lines);
                cart.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            var count = cart.Items.Sum(x => x.SoLuong);
            var total = cart.Items.Sum(x => x.Gia * x.SoLuong);

            return Json(new { ok = true, count, total });
        }

        // ===== Action Submit Form truyền thống =====
        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int maSP, int quantity = 1, CancellationToken ct = default)
        {
            await AddJson(new AddCartRequest { maSP = maSP, quantity = quantity }, ct);
            return RedirectToAction(nameof(Index));
        }

        // ===== Mua ngay → chuyển sang Checkout với 1 dòng giỏ =====
        [Authorize, HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> BuyNow(int maSP, int quantity = 1, CancellationToken ct = default)
        {
            var qty = quantity < 1 ? 1 : quantity;

            // 1. Lấy sản phẩm
            var sp = await _db.SanPhams.FirstOrDefaultAsync(x => x.MaSP == maSP, ct);
            if (sp == null) return NotFound();

            if (!sp.IsActive || sp.SoLuong <= 0)
            {
                TempData["CartError"] = "Sản phẩm đã hết hàng.";
                return RedirectToAction("Details", "SanPham", new { slug = sp.Slug });
            }

            // 2. Kiểm tra tồn kho cho số lượng muốn mua
            if (qty > sp.SoLuong)
            {
                TempData["CartError"] = $"Chỉ còn {sp.SoLuong} chiếc trong kho.";
                return RedirectToAction("Details", "SanPham", new { slug = sp.Slug });
            }

            // 3. Lấy / tạo giỏ
            var cart = await GetOrCreateCartAsync(UserId!, ct);
            var line = cart.Items.FirstOrDefault(i => i.MaSP == maSP);

            // 4. Mua ngay: set lại số lượng đúng = qty, không cộng dồn
            if (line == null)
            {
                line = new CartLine
                {
                    MaSP = sp.MaSP,
                    SoLuong = qty,
                    Gia = sp.Gia,
                    TenSP = sp.TenSP,
                    ImageUrl = string.IsNullOrWhiteSpace(sp.ImageUrl)
                        ? "~/images/hero-showroom.jpg"
                        : sp.ImageUrl,
                    Slug = sp.Slug
                };
                cart.Items.Add(line);
            }
            else
            {
                line.SoLuong = qty;
                line.Slug = sp.Slug;
            }

            cart.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // dùng MaSP làm ids cho Checkout
            var ids = sp.MaSP.ToString();

            return RedirectToAction("Index", "Checkout", new { ids });
        }


        // ===== Xoá + Clear giỏ bằng submit form =====
        [Authorize, HttpPost]
        public async Task<IActionResult> Remove(int maSP, CancellationToken ct = default)
        {
            await RemoveJson(new RemoveCartRequest { maSP = maSP }, ct);
            return RedirectToAction(nameof(Index));
        }

        [Authorize, HttpPost]
        public async Task<IActionResult> Clear(CancellationToken ct = default)
        {
            var cart = await GetOrCreateCartAsync(UserId!, ct);
            _db.CartLines.RemoveRange(cart.Items);
            cart.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return RedirectToAction(nameof(Index));
        }
    }
}
