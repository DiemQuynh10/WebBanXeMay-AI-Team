using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Areas.Admin.ViewModels;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using static WebBanXeMay.Areas.Admin.ViewModels.OrderDetailViewModel;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
    public class OrdersController : Controller
    {
        private readonly AppDbContext _context;
        public OrdersController(AppDbContext context) => _context = context;

        // ================== DANH SÁCH ĐƠN HÀNG ==================
        // GET: /Admin/Orders
        public async Task<IActionResult> Index(
            string? status,
            string? coc,
            string? q,               // từ khóa tìm kiếm
            DateTime? fromDate,      // lọc từ ngày
            DateTime? toDate,        // lọc đến ngày
            int? overdue,            // 1 = chỉ lấy đơn chờ > 24h (gọi từ Dashboard)
            int page = 1)            // phân trang
        {
            const int PAGE_SIZE = 10;
            if (page < 1) page = 1;

            IQueryable<DonHang> query = _context.DonHangs
                .Include(d => d.ChiTietDHs).ThenInclude(ct => ct.SanPham)
                .Include(d => d.User);

            // --- Lọc theo trạng thái đơn ---
            TrangThaiDonHang? parsedStatus = null;
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<TrangThaiDonHang>(status, ignoreCase: true, out var st))
            {
                parsedStatus = st;
                query = query.Where(d => d.TrangThai == st);
            }

            // --- Lọc theo trạng thái cọc ---
            TrangThaiCoc? parsedCoc = null;
            if (!string.IsNullOrWhiteSpace(coc) &&
                Enum.TryParse<TrangThaiCoc>(coc, ignoreCase: true, out var stCoc))
            {
                parsedCoc = stCoc;
                query = query.Where(d => d.TrangThaiCoc == stCoc);
            }

            // --- Lọc theo khoảng ngày đặt hàng ---
            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;
                query = query.Where(d => d.NgayDH >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date.AddDays(1); // < toDate+1
                query = query.Where(d => d.NgayDH < to);
            }

            // --- Lọc đơn chờ > 24h (gọi từ Dashboard) ---
            if (overdue.HasValue && overdue.Value == 1)
            {
                var limit = DateTime.Now.AddHours(-24);
                query = query.Where(d =>
                    d.TrangThai == TrangThaiDonHang.ChoXacNhan &&
                    d.NgayDH <= limit);
            }

            // --- Tìm kiếm theo mã đơn / tên khách / người nhận / SĐT ---
            if (!string.IsNullOrWhiteSpace(q))
            {
                var keyword = q.Trim().ToLower();
                query = query.Where(d =>
                    d.MaDH.ToString().Contains(keyword) ||
                    (d.User.TenHienThi != null && d.User.TenHienThi.ToLower().Contains(keyword)) ||
                    (d.NguoiNhan != null && d.NguoiNhan.ToLower().Contains(keyword)) ||
                    (d.Phone != null && d.Phone.Contains(keyword))
                );
            }

            // --- Đếm tổng, tính phân trang ---
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)PAGE_SIZE);
            if (page > totalPages && totalPages > 0) page = totalPages;

            var orders = await query
                .OrderByDescending(d => d.NgayDH)
                .Skip((page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .ToListAsync();

            // --- Gửi dữ liệu filter cho View ---
            ViewBag.Statuses = Enum.GetNames(typeof(TrangThaiDonHang)).ToList();
            ViewBag.CurrentStatus = status ?? "";

            ViewBag.CocStatuses = Enum.GetNames(typeof(TrangThaiCoc)).ToList();
            ViewBag.CurrentCoc = coc ?? "";

            ViewBag.Search = q;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.Overdue = overdue;          // để View giữ trạng thái khi phân trang

            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;
            ViewBag.PageSize = PAGE_SIZE;

            return View(orders);
        }


        // ================== CHI TIẾT ĐƠN HÀNG ==================
        // GET: /Admin/Orders/Details/5
        // GET: /Admin/Orders/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var model = await _context.DonHangs
                .Where(d => d.MaDH == id)
                .Select(d => new OrderDetailViewModel
                {
                    OrderId = d.MaDH,
                    CustomerName = d.User.TenHienThi ?? "Không rõ",
                    OrderDate = d.NgayDH,
                    Status = d.TrangThai.ToString(),
                    ShippingRecipient = d.NguoiNhan ?? d.User.TenHienThi ?? "Không rõ",
                    ShippingAddress = d.DiaChi ?? "Không có",
                    ShippingPhone = d.Phone ?? "Không có",
                    TotalAmount = d.TongTien,

                    DepositAmount = d.SoTienCoc,
                    DepositStatus = d.TrangThaiCoc.ToString(),
                    DepositDueDate = d.HanCoc,
                    DepositMethod = d.PhuongThucCoc,
                    RemainingAmount = d.SoTienCoc.HasValue ? d.TongTien - d.SoTienCoc.Value : d.TongTien,

                    Items = d.ChiTietDHs.Select(ct => new OrderDetailViewModel.OrderItemViewModel
                    {
                        ProductId = ct.MaSP,
                        ProductName = ct.SanPham.TenSP,
                        ImageUrl = ct.SanPham.ImageUrl ?? "/images/no-image.png",
                        Quantity = ct.SoLuong,
                        UnitPrice = ct.Gia
                    }).ToList(),

                    Reviews = _context.DanhGias
                        .Where(r => r.MaDH == d.MaDH)
                        .Select(r => new ReviewViewModel
                        {
                            ReviewId = r.MaDanhGia,
                            ProductId = r.MaSP,
                            ProductName = r.SanPham.TenSP,
                            ReviewerName = r.TenNguoiDanhGia,
                            Comment = r.NoiDung,
                            Rating = r.DiemDanhGia,
                            CreatedAt = r.NgayDanhGia
                        }).ToList(),

                    Logs = _context.OrderLogs
                        .Where(l => l.MaDH == d.MaDH)
                        .OrderByDescending(l => l.ThoiGian)
                        .Select(l => new OrderDetailViewModel.LogItemViewModel
                        {
                            ThoiGian = l.ThoiGian,
                            NoiDung = l.NoiDung
                        }).ToList(),

                    IsDepositOverdue =
    d.SoTienCoc.HasValue && d.SoTienCoc.Value > 0 &&    // có cọc
    d.HanCoc.HasValue &&                                // có hạn cọc
    d.HanCoc < DateTime.Now &&                          // đã quá hạn
    (
        d.TrangThaiCoc == TrangThaiCoc.ChuaCoc ||       // chưa nộp
        d.TrangThaiCoc == TrangThaiCoc.ChoXacNhanCoc || // chờ xác nhận
        d.TrangThaiCoc == TrangThaiCoc.DaCoc)    // đã cọc nhưng chưa xử lý xong đơn
                })
                .FirstOrDefaultAsync();

            if (model == null) return NotFound();

            return View(model);
        }


        // ================== CẬP NHẬT TRẠNG THÁI ĐƠN HÀNG ==================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(
            int id,
            string nextStatus,
            string? statusFilter,
            string? cocFilter)
        {
            var order = await _context.DonHangs.FirstOrDefaultAsync(d => d.MaDH == id);
            if (order == null)
            {
                TempData["Error"] = "❌ Không tìm thấy đơn hàng.";
                return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
            }

            if (!Enum.TryParse<TrangThaiDonHang>(nextStatus, ignoreCase: true, out var stMoi))
            {
                TempData["Error"] = "❌ Trạng thái không hợp lệ.";
                return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
            }

            var stCu = order.TrangThai;

            // Thứ tự chuẩn của flow
            var flow = new List<TrangThaiDonHang>
            {
                TrangThaiDonHang.ChoXacNhan,
                TrangThaiDonHang.DangXuLy,
                TrangThaiDonHang.DangGiao,
                TrangThaiDonHang.HoanTat
            };

            int oldIndex = flow.IndexOf(stCu);
            int newIndex = flow.IndexOf(stMoi);
            bool isAllowed = false;

            // 1. Không đổi gì -> OK
            if (stCu == stMoi)
            {
                isAllowed = true;
            }
            // 2. Chuyển sang Đã Hủy (cho phép từ mọi trạng thái, trừ khi đã Hoàn tất)
            else if (stMoi == TrangThaiDonHang.DaHuy)
            {
                if (stCu != TrangThaiDonHang.HoanTat)
                    isAllowed = true;
            }
            // 3. Đã Hủy rồi thì không khôi phục lại
            else if (stCu == TrangThaiDonHang.DaHuy)
            {
                isAllowed = false;
            }
            // 4. Đã Hoàn tất thì không đổi nữa
            else if (stCu == TrangThaiDonHang.HoanTat)
            {
                isAllowed = false;
            }
            // 5. Còn lại: chỉ cho đi xuôi (index mới > index cũ)
            else
            {
                if (oldIndex != -1 && newIndex != -1 && newIndex > oldIndex)
                    isAllowed = true;
            }

            if (!isAllowed)
            {
                TempData["Error"] =
                    $"❌ Không thể chuyển từ '{stCu}' sang '{stMoi}'. Vui lòng tuân thủ quy trình.";
                return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
            }

            // ==== RÀNG BUỘC CỌC ====
            bool hasDeposit = order.SoTienCoc.HasValue && order.SoTienCoc.Value > 0;

            if (hasDeposit)
            {
                // Mọi trạng thái cọc KHÔNG phải đã xử lý
                bool isPendingDeposit =
                    order.TrangThaiCoc != TrangThaiCoc.DaCoc &&
                    order.TrangThaiCoc != TrangThaiCoc.HoanCoc &&
                    order.TrangThaiCoc != TrangThaiCoc.MatCoc;

                // 1. Nếu vẫn đang chờ cọc (ví dụ ChoXacNhanCoc) thì
                //    chỉ được Hủy đơn, không cho nhảy sang Đang xử lý / Đang giao / Hoàn tất
                if (isPendingDeposit &&
                    stMoi != TrangThaiDonHang.DaHuy &&
                    stMoi != stCu)
                {
                    TempData["Error"] =
                        "❌ Đơn hàng có đặt cọc. " +
                        "Cần xác nhận ĐÃ CỌC hoặc HỦY đơn trước khi chuyển sang trạng thái khác.";
                    return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
                }

                // 2. Nếu tiền cọc đã HOÀN / MẤT thì chỉ cho phép đưa đơn sang ĐÃ HỦY
                bool isDepositedButClosed =
                    (order.TrangThaiCoc == TrangThaiCoc.HoanCoc ||
                     order.TrangThaiCoc == TrangThaiCoc.MatCoc);

                if (isDepositedButClosed && stMoi != TrangThaiDonHang.DaHuy)
                {
                    TempData["Error"] =
                        "❌ Tiền cọc đã được xử lý (Hoàn cọc / Mất cọc). " +
                        "Chỉ có thể chuyển đơn sang trạng thái ĐÃ HỦY.";
                    return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
                }

                // 3. Khi giao hàng hoặc hoàn tất phải ĐÃ CỌC
                if ((stMoi == TrangThaiDonHang.DangGiao || stMoi == TrangThaiDonHang.HoanTat) &&
                    order.TrangThaiCoc != TrangThaiCoc.DaCoc)
                {
                    TempData["Error"] =
                        "❌ Phải xác nhận 'Đã cọc' trước khi chuyển sang Đang giao hoặc Hoàn tất.";
                    return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
                }
            }

            // OK -> Lưu + ghi log lịch sử
            order.TrangThai = stMoi;

            _context.OrderLogs.Add(new OrderLog
            {
                MaDH = order.MaDH,
                NoiDung = $"Cập nhật trạng thái đơn từ '{stCu}' sang '{stMoi}'.",
                ThoiGian = DateTime.Now
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "✅ Cập nhật trạng thái đơn hàng thành công!";
            return RedirectToAction(nameof(Index), new { status = statusFilter, coc = cocFilter });
        }

        // ================== TẠO ĐƠN HÀNG TỪ ADMIN ==================
        // GET: /Admin/Orders/Create
        public async Task<IActionResult> Create()
        {
            var sanPhams = await _context.SanPhams.OrderBy(sp => sp.TenSP).ToListAsync();
            var users = await _context.Users.OrderBy(u => u.UserName).ToListAsync();

            ViewBag.UserId = new SelectList(users, "Id", "UserName");

            var vm = new DonHangViewModel
            {
                DonHang = new DonHang(),
                DanhSachSanPham = sanPhams
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DonHangViewModel vm)
        {
            var allProducts = await _context.SanPhams.ToDictionaryAsync(sp => sp.MaSP, sp => sp.Gia);

            if (ModelState.IsValid)
            {
                vm.DonHang.NgayDH = DateTime.Now;
                vm.DonHang.TrangThai = TrangThaiDonHang.ChoXacNhan;
                vm.DonHang.TongTien = 0;

                _context.Add(vm.DonHang);

                if (vm.ChiTietDHs != null && vm.ChiTietDHs.Any())
                {
                    foreach (var chiTiet in vm.ChiTietDHs)
                    {
                        if (chiTiet.MaSP > 0 && chiTiet.SoLuong > 0)
                        {
                            if (allProducts.TryGetValue(chiTiet.MaSP, out var gia))
                            {
                                chiTiet.Gia = gia;
                                chiTiet.DonHang = vm.DonHang;
                                _context.ChiTietDHs.Add(chiTiet);
                                vm.DonHang.TongTien += (chiTiet.SoLuong * chiTiet.Gia);
                            }
                        }
                    }
                }
                await _context.SaveChangesAsync();

                _context.OrderLogs.Add(new OrderLog
                {
                    MaDH = vm.DonHang.MaDH,
                    NoiDung = "Tạo đơn hàng mới từ Admin",
                    ThoiGian = DateTime.Now
                });
                await _context.SaveChangesAsync();

                TempData["Success"] = "Tạo đơn hàng mới thành công!";
                return RedirectToAction(nameof(Index));
            }

            var users = await _context.Users.OrderBy(u => u.UserName).ToListAsync();
            ViewBag.UserId = new SelectList(users, "Id", "UserName", vm.DonHang.UserId);
            vm.DanhSachSanPham = await _context.SanPhams.OrderBy(sp => sp.TenSP).ToListAsync();

            return View(vm);
        }

        // ================== XỬ LÝ ĐẶT CỌC ==================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmDeposit(int id)
        {
            var order = await _context.DonHangs.FirstOrDefaultAsync(d => d.MaDH == id);
            if (order == null) return NotFound();

            var oldCoc = order.TrangThaiCoc;
            order.TrangThaiCoc = TrangThaiCoc.DaCoc;
            order.ThoiGianXacNhanCoc = DateTime.Now;

            if (order.TrangThai == TrangThaiDonHang.ChoXacNhan)
                order.TrangThai = TrangThaiDonHang.DangXuLy;

            _context.OrderLogs.Add(new OrderLog
            {
                MaDH = order.MaDH,
                NoiDung = $"Xác nhận đã nhận tiền cọc (từ '{oldCoc}' sang '{order.TrangThaiCoc}').",
                ThoiGian = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Đã xác nhận đặt cọc cho đơn #{order.MaDH}";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefundDeposit(int id)
        {
            var order = await _context.DonHangs.FirstOrDefaultAsync(d => d.MaDH == id);
            if (order == null) return NotFound();

            var oldCoc = order.TrangThaiCoc;
            order.TrangThaiCoc = TrangThaiCoc.HoanCoc;
            order.NgayCoc = DateTime.Now;

            _context.OrderLogs.Add(new OrderLog
            {
                MaDH = order.MaDH,
                NoiDung = $"Hoàn lại tiền cọc (từ '{oldCoc}' sang '{order.TrangThaiCoc}').",
                ThoiGian = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Đã hoàn cọc cho đơn #{order.MaDH}";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoseDeposit(int id)
        {
            var order = await _context.DonHangs.FirstOrDefaultAsync(d => d.MaDH == id);
            if (order == null) return NotFound();

            var oldCoc = order.TrangThaiCoc;

            order.TrangThaiCoc = TrangThaiCoc.MatCoc;
            order.TrangThai = TrangThaiDonHang.DaHuy;
            order.NgayCoc = DateTime.Now;

            _context.OrderLogs.Add(new OrderLog
            {
                MaDH = order.MaDH,
                NoiDung = $"Đánh dấu MẤT CỌC (từ '{oldCoc}' sang '{order.TrangThaiCoc}').",
                ThoiGian = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Đã đánh dấu MẤT CỌC cho đơn #{order.MaDH}";
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
