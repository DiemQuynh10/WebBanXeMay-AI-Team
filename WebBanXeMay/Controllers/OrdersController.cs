using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using WebBanXeMay.Models.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using System.IO;

namespace WebBanXeMay.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public OrdersController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // ========== DANH SÁCH ĐƠN CỦA USER ==========
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var orders = await _context.DonHangs
                .Include(o => o.ChiTietDHs)
                    .ThenInclude(ct => ct.SanPham)
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.NgayDH)
                .ToListAsync();

            return View(orders);
        }

        // ========== CHI TIẾT ĐƠN ==========
        public async Task<IActionResult> Details(int id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
                return RedirectToAction("Login", "Account");

            var order = await _context.DonHangs
                .Include(d => d.ChiTietDHs)
                    .ThenInclude(ct => ct.SanPham)
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.MaDH == id && d.UserId == currentUserId);

            if (order == null)
            {
                TempData["Error"] = "❌ Không tìm thấy đơn hàng hoặc bạn không có quyền xem.";
                return RedirectToAction(nameof(Index));
            }

            var reviews = await _context.DanhGias
                .Where(r => r.MaDH == id)
                .Include(r => r.SanPham)
                .ToListAsync();

            var logs = await _context.OrderLogs
                .Where(x => x.MaDH == id)
                .OrderByDescending(x => x.ThoiGian)
                .ToListAsync();

            var reviewedProductIds = reviews
                .Select(r => r.MaSP)
                .ToHashSet();

            // ====== TÍNH CỜ QUÁ HẠN CỌC ======
            bool isDepositOverdue =
                order.SoTienCoc.HasValue && order.SoTienCoc.Value > 0 &&
                order.HanCoc.HasValue &&
                order.HanCoc.Value < DateTime.Now &&
                (
                    order.TrangThaiCoc == TrangThaiCoc.ChuaCoc ||
                    order.TrangThaiCoc == TrangThaiCoc.ChoXacNhanCoc ||
                    order.TrangThaiCoc == TrangThaiCoc.DaCoc
                );

            var model = new MyOrderDetailsVM
            {
                OrderId = order.MaDH,
                OrderDate = order.NgayDH,
                Status = order.TrangThai.ToString(),
                TotalAmount = order.TongTien,

                ShippingRecipient = order.NguoiNhan
                                     ?? order.User?.TenHienThi
                                     ?? order.User?.UserName
                                     ?? "Không rõ",
                ShippingPhone = order.Phone,
                ShippingAddress = order.DiaChi,

                // Thông tin cọc
                SoTienCoc = order.SoTienCoc,
                TrangThaiCoc = order.TrangThaiCoc,
                HanCoc = order.HanCoc,
                NgayCoc = order.NgayCoc,
                PhuongThucCoc = order.PhuongThucCoc,
                MaGiaoDichCoc = order.MaGiaoDichCoc,
                IsDepositOverdue = isDepositOverdue,   // ⭐ dùng cho view cảnh báo

                // Sản phẩm trong đơn
                Items = order.ChiTietDHs.Select(ct => new MyOrderItemVM
                {
                    ProductId = ct.MaSP,
                    ProductName = ct.SanPham?.TenSP ?? "Không rõ",
                    ImageUrl = string.IsNullOrWhiteSpace(ct.SanPham?.ImageUrl)
                                    ? "/images/no-image.png"
                                    : ct.SanPham.ImageUrl,
                    Quantity = ct.SoLuong,
                    UnitPrice = ct.Gia,
                    HasBeenReviewed = reviewedProductIds.Contains(ct.MaSP)
                }).ToList(),

                // Các đánh giá
                Reviews = reviews.Select(rv => new MyOrderReviewVM
                {
                    ReviewId = rv.MaDanhGia,
                    ProductId = rv.MaSP,
                    ProductName = rv.SanPham?.TenSP ?? "Không rõ",
                    ReviewerName = rv.TenNguoiDanhGia,
                    Comment = rv.NoiDung,
                    Rating = rv.DiemDanhGia,
                    CreatedAt = rv.NgayDanhGia,
                    ImageUrl = rv.ImageUrl,
                    VideoUrl = rv.VideoUrl,
                    IsAnonymous = rv.IsAnonymous
                }).ToList(),

                // Lịch sử xử lý đơn
                Logs = logs.Select(l => new OrderLog
                {
                    Id = l.Id,
                    NoiDung = l.NoiDung,
                    ThoiGian = l.ThoiGian
                }).ToList()
            };

            return View(model);
        }

        // ========== USER HỦY ĐƠN ==========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var order = await _context.DonHangs
                .FirstOrDefaultAsync(d => d.MaDH == id && d.UserId == userId);

            if (order == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hoặc bạn không có quyền.";
                return RedirectToAction(nameof(Index));
            }

            // Chỉ cho hủy khi đơn đang CHO XÁC NHẬN
            if (order.TrangThai != TrangThaiDonHang.ChoXacNhan)
            {
                TempData["Warning"] = "Đơn hiện không thể hủy.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Nếu đã có đặt cọc (và trạng thái cọc khác 'ChuaCoc') thì không cho user tự hủy
            bool hasDeposit = order.SoTienCoc.HasValue && order.SoTienCoc.Value > 0;
            bool isDepositStarted = hasDeposit && order.TrangThaiCoc != TrangThaiCoc.ChuaCoc;

            if (isDepositStarted)
            {
                TempData["Warning"] = "Đơn đã liên quan đến đặt cọc, vui lòng liên hệ hỗ trợ để hủy.";
                return RedirectToAction(nameof(Details), new { id });
            }

            order.TrangThai = TrangThaiDonHang.DaHuy;

            // Ghi log
            _context.OrderLogs.Add(new OrderLog
            {
                MaDH = order.MaDH,
                NoiDung = "Khách tự hủy đơn hàng từ giao diện người dùng.",
                ThoiGian = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Đã hủy đơn #{id}.";
            return RedirectToAction(nameof(Index));
        }

        // ========== THÊM ĐÁNH GIÁ ==========
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddReview(
            [FromForm] int MaDH,
            [FromForm] int MaSP,
            [FromForm] int DiemDanhGia,
            [FromForm] string NoiDung,
            [FromForm] bool IsAnonymous,
            [FromForm] IFormFile? Images,
            [FromForm] IFormFile? Videos)
        {
            var order = await _context.DonHangs.FindAsync(MaDH);
            if (order == null || order.TrangThai != TrangThaiDonHang.HoanTat)
            {
                TempData["Error"] = "Chỉ có thể đánh giá khi đơn hàng đã hoàn tất!";
                return RedirectToAction("Details", "Orders", new { id = MaDH });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FindAsync(userId);
            string reviewerName = user?.TenHienThi ?? user?.UserName ?? "Người dùng ẩn danh";

            var existingReview = await _context.DanhGias
                .FirstOrDefaultAsync(r =>
                    r.MaDH == MaDH &&
                    r.MaSP == MaSP &&
                    r.TenNguoiDanhGia == reviewerName
                );

            if (existingReview != null)
            {
                TempData["Warning"] = "⚠️ Bạn đã đánh giá sản phẩm này trong đơn hàng này rồi.";
                return RedirectToAction("Details", "Orders", new { id = MaDH });
            }

            var model = new DanhGia
            {
                MaDH = MaDH,
                MaSP = MaSP,
                DiemDanhGia = DiemDanhGia,
                NoiDung = NoiDung,
                NgayDanhGia = DateTime.Now,
                IsAnonymous = IsAnonymous,
                TenNguoiDanhGia = IsAnonymous ? "Người dùng ẩn danh" : reviewerName
            };

            // Upload ảnh
            if (Images != null && Images.Length > 0)
            {
                string uploadDir = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "reviews");
                Directory.CreateDirectory(uploadDir);
                string uniqueFileName = Guid.NewGuid() + "_" + Images.FileName;
                string filePath = Path.Combine(uploadDir, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await Images.CopyToAsync(fileStream);
                }
                model.ImageUrl = "/uploads/reviews/" + uniqueFileName;
            }

            // Upload video
            if (Videos != null && Videos.Length > 0)
            {
                string uploadDir = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "reviews");
                Directory.CreateDirectory(uploadDir);
                string uniqueFileName = Guid.NewGuid() + "_" + Videos.FileName;
                string filePath = Path.Combine(uploadDir, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await Videos.CopyToAsync(fileStream);
                }
                model.VideoUrl = "/uploads/reviews/" + uniqueFileName;
            }

            _context.DanhGias.Add(model);
            await _context.SaveChangesAsync();

            TempData["Success"] = "✅ Cảm ơn bạn đã đánh giá sản phẩm!";
            return RedirectToAction("Details", "Orders", new { id = MaDH });
        }

        // ========== ĐẶT CỌC - PHÍA USER ==========

        // GET: /Orders/Deposit/18
        [HttpGet]
        public async Task<IActionResult> Deposit(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var order = await _context.DonHangs
                .FirstOrDefaultAsync(d => d.MaDH == id && d.UserId == userId);

            if (order == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng hoặc bạn không có quyền.";
                return RedirectToAction(nameof(Index));
            }

            if (!order.SoTienCoc.HasValue || order.SoTienCoc <= 0)
            {
                TempData["Warning"] = "Đơn hàng này không yêu cầu đặt cọc.";
                return RedirectToAction(nameof(Details), new { id });
            }

            bool changed = false;

            // Hạn cọc mặc định: +3 ngày kể từ ngày đặt nếu chưa có
            if (!order.HanCoc.HasValue)
            {
                order.HanCoc = order.NgayDH.AddDays(3);
                changed = true;
            }

            // Vá dữ liệu cũ: nếu đang ChoXacNhanCoc nhưng chưa có thời gian yêu cầu thì coi như ChưaCoc
            if (order.TrangThaiCoc == TrangThaiCoc.ChoXacNhanCoc
                && !order.ThoiGianYeuCauCoc.HasValue)
            {
                order.TrangThaiCoc = TrangThaiCoc.ChuaCoc;
                changed = true;
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }

            return View(order); // View: Views/Orders/Deposit.cshtml
        }

        // POST: user bấm "Tôi đã chuyển khoản, chờ shop xác nhận"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> YeuCauXacNhanCoc(int orderId, string? note)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account");

            var order = await _context.DonHangs
                .FirstOrDefaultAsync(d => d.MaDH == orderId && d.UserId == userId);

            if (order == null)
            {
                TempData["Error"] = "Không tìm thấy đơn hàng hoặc bạn không có quyền.";
                return RedirectToAction(nameof(Index));
            }

            if (!order.SoTienCoc.HasValue || order.SoTienCoc <= 0)
            {
                TempData["Warning"] = "Đơn hàng này không yêu cầu đặt cọc.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            if (order.TrangThaiCoc == TrangThaiCoc.DaCoc)
            {
                TempData["Info"] = "Đơn hàng này đã được xác nhận đặt cọc trước đó.";
                return RedirectToAction(nameof(Deposit), new { id = orderId });
            }

            // Cho phép gửi yêu cầu / cập nhật ghi chú khi chưa cọc hoặc đang chờ
            order.TrangThaiCoc = TrangThaiCoc.ChoXacNhanCoc;
            order.ThoiGianYeuCauCoc = DateTime.Now;
            order.GhiChuCoc = note;

            _context.OrderLogs.Add(new OrderLog
            {
                MaDH = order.MaDH,
                NoiDung = "Khách gửi yêu cầu xác nhận đã chuyển khoản tiền cọc.",
                ThoiGian = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đã gửi yêu cầu xác nhận đặt cọc. Shop sẽ kiểm tra và phản hồi.";
            return RedirectToAction(nameof(Deposit), new { id = orderId });
        }

        // GET: /quy-dinh-dat-coc
        [HttpGet]
        [Route("quy-dinh-dat-coc")]
        public IActionResult DepositPolicy()
        {
            return View();
        }
    }
}
