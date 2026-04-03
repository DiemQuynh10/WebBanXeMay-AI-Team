using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using Microsoft.AspNetCore.Authorization;
using System.Text.RegularExpressions;
using WebBanXeMay.Areas.Admin.ViewModels;
using ClosedXML.Excel;

namespace WebBanXeMay.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Policy = "AdminOnly")]
    public class ProductsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public ProductsController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        // ================================================================
        // INDEX (có tìm kiếm + phân trang)
        // ================================================================
        // ================================================================
        // INDEX (tìm kiếm + lọc nâng cao + phân trang)
        // ================================================================
        public async Task<IActionResult> Index(
            string? q,
            int? maLoai,
            int? maTH,
            int? stock,          // 1: Còn nhiều, 2: Sắp hết, 3: Hết hàng, 4: Ngừng KD
            decimal? minPrice,
            decimal? maxPrice,
            int page = 1)
        {
            int pageSize = 10;

            var baseQuery = _context.SanPhams.AsQueryable();

            ViewBag.TotalProductCount = await baseQuery.CountAsync();
            ViewBag.ActiveProductCount = await baseQuery.CountAsync(x => x.IsActive);
            ViewBag.InactiveProductCount = await baseQuery.CountAsync(x => !x.IsActive);
            ViewBag.OutOfStockCount = await baseQuery.CountAsync(x => x.IsActive && x.SoLuong <= 0);
            ViewBag.LowStockCount = await baseQuery.CountAsync(x => x.IsActive && x.SoLuong > 0 && x.SoLuong <= 5);

            var query = _context.SanPhams
                                .Include(s => s.Loai)
                                .Include(s => s.ThuongHieu)
                                .AsQueryable();

            // Tìm kiếm theo tên / slug
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(x => x.TenSP.Contains(q) || x.Slug.Contains(q));

            // Lọc theo loại & hãng
            if (maLoai.HasValue)
                query = query.Where(x => x.MaLoai == maLoai.Value);

            if (maTH.HasValue)
                query = query.Where(x => x.MaTH == maTH.Value);

            // Lọc theo tình trạng kho
            if (stock.HasValue)
            {
                switch (stock.Value)
                {
                    case 1: // Còn nhiều hàng (>5)
                        query = query.Where(x => x.IsActive && x.SoLuong > 5);
                        break;
                    case 2: // Sắp hết (1–5)
                        query = query.Where(x => x.IsActive && x.SoLuong > 0 && x.SoLuong <= 5);
                        break;
                    case 3: // Hết hàng (0)
                        query = query.Where(x => x.IsActive && x.SoLuong <= 0);
                        break;
                    case 4: // Ngừng kinh doanh
                        query = query.Where(x => !x.IsActive);
                        break;
                }
            }

            // Lọc theo khoảng giá
            if (minPrice.HasValue)
                query = query.Where(x => x.Gia >= minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(x => x.Gia <= maxPrice.Value);

            // Đếm tổng
            int totalItems = await query.CountAsync();

            // Lấy trang hiện tại
            var list = await query.OrderByDescending(x => x.MaSP)
                                  .Skip((page - 1) * pageSize)
                                  .Take(pageSize)
                                  .ToListAsync();

            // ViewBag dùng cho phân trang + giữ bộ lọc
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            ViewBag.PageIndex = page;
            ViewBag.TotalItems = totalItems;

            ViewBag.CurrentSearch = q;
            ViewBag.CurrentLoai = maLoai;
            ViewBag.CurrentTH = maTH;
            ViewBag.CurrentStock = stock;
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;

            // Dropdown loại & hãng
            ViewBag.LoaiList = new SelectList(await _context.Loais.ToListAsync(), "MaLoai", "TenLoai", maLoai);
            ViewBag.THList = new SelectList(await _context.ThuongHieus.ToListAsync(), "MaTH", "TenTH", maTH);

            return View(list);
        }

        // ================================================================
        // CREATE (GET)
        // ================================================================
        public async Task<IActionResult> Create()
        {
            await FillDropdowns();
            return View(new ProductCreateViewModel());
        }

        // ================================================================
        // CREATE (POST)
        // ================================================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCreateViewModel model, IFormFile? image)
        {
            if (!ModelState.IsValid)
            {
                await FillDropdowns();
                return View(model);
            }

            var slug = ToSlug(model.TenSP);
            if (await _context.SanPhams.AnyAsync(s => s.Slug == slug))
            {
                ModelState.AddModelError("TenSP", "Sản phẩm này đã tồn tại (slug trùng).");
                await FillDropdowns();
                return View(model);
            }

            string? imageUrl = null;
            if (image != null && image.Length > 0)
            {
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(image.FileName)}";
                var path = Path.Combine(_env.WebRootPath, "images", "products", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = new FileStream(path, FileMode.Create);
                await image.CopyToAsync(fs);
                imageUrl = $"/images/products/{fileName}";
            }

            var sp = new SanPham
            {
                TenSP = model.TenSP,
                Gia = model.Gia,
                SoLuong = model.SoLuong,
                CC = model.CC,
                MoTa = model.MoTa,
                IsActive = model.IsActive,
                MaLoai = model.MaLoai,
                MaTH = model.MaTH,
                Slug = slug,
                ImageUrl = imageUrl
            };

            _context.SanPhams.Add(sp);
            await _context.SaveChangesAsync();

            // Thêm thông báo thành công
            TempData["SuccessMessage"] = "Thêm sản phẩm thành công!";
            return RedirectToAction(nameof(Index));
        }

        // ================================================================
        // EDIT (GET)
        // ================================================================
        public async Task<IActionResult> Edit(int id)
        {
            var sp = await _context.SanPhams.AsNoTracking().FirstOrDefaultAsync(x => x.MaSP == id);
            if (sp == null) return NotFound();

            var vm = new ProductEditViewModel
            {
                MaSP = sp.MaSP,
                TenSP = sp.TenSP,
                Gia = sp.Gia,
                SoLuong = sp.SoLuong,
                CC = sp.CC,
                MoTa = sp.MoTa,
                IsActive = sp.IsActive,
                MaLoai = sp.MaLoai,
                MaTH = sp.MaTH,
                ImageUrl = sp.ImageUrl,
                LoaiXeList = new SelectList(await _context.Loais.ToListAsync(), "MaLoai", "TenLoai", sp.MaLoai),
                HangXeList = new SelectList(await _context.ThuongHieus.ToListAsync(), "MaTH", "TenTH", sp.MaTH)
            };

            return View(vm);
        }

        // ================================================================
        // EDIT (POST)
        // ================================================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ProductEditViewModel model, IFormFile? NewImageFile)
        {
            if (id != model.MaSP)
                return BadRequest();

            var sp = await _context.SanPhams.FirstOrDefaultAsync(s => s.MaSP == id);
            if (sp == null)
                return NotFound();

            if (!ModelState.IsValid)
            {
                model.LoaiXeList = new SelectList(await _context.Loais.ToListAsync(), "MaLoai", "TenLoai", model.MaLoai);
                model.HangXeList = new SelectList(await _context.ThuongHieus.ToListAsync(), "MaTH", "TenTH", model.MaTH);
                return View(model);
            }

            if (NewImageFile != null && NewImageFile.Length > 0)
            {
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(NewImageFile.FileName)}";
                var path = Path.Combine(_env.WebRootPath, "images", "products", fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                using var fs = new FileStream(path, FileMode.Create);
                await NewImageFile.CopyToAsync(fs);

                sp.ImageUrl = $"/images/products/{fileName}";
            }

            sp.TenSP = model.TenSP;
            sp.Gia = model.Gia;
            sp.SoLuong = model.SoLuong;
            sp.CC = model.CC;
            sp.MoTa = model.MoTa;
            sp.IsActive = model.IsActive;
            sp.MaLoai = model.MaLoai;
            sp.MaTH = model.MaTH;

            try
            {
                await _context.SaveChangesAsync();

                // Thêm thông báo thành công
                TempData["SuccessMessage"] = "Cập nhật sản phẩm thành công!";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError(string.Empty, "Dữ liệu vừa bị người khác chỉnh sửa. Vui lòng thử lại.");
                model.LoaiXeList = new SelectList(await _context.Loais.ToListAsync(), "MaLoai", "TenLoai", model.MaLoai);
                model.HangXeList = new SelectList(await _context.ThuongHieus.ToListAsync(), "MaTH", "TenTH", model.MaTH);
                return View(model);
            }
        }

        // ================================================================
        // DELETE
        // ================================================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var sp = await _context.SanPhams.FindAsync(id);
            if (sp == null) return NotFound();

            _context.SanPhams.Remove(sp);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Xóa sản phẩm thành công!";
            return RedirectToAction(nameof(Index));
        }
        // ================================================================
        // EXPORT EXCEL
        // ================================================================
        // ================================================================
        // EXPORT EXCEL (theo đúng bộ lọc hiện tại)
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> ExportExcel(
            string? q,
            int? maLoai,
            int? maTH,
            int? stock,
            decimal? minPrice,
            decimal? maxPrice)
        {
            var query = _context.SanPhams
                                .Include(s => s.Loai)
                                .Include(s => s.ThuongHieu)
                                .AsQueryable();

            // Tìm kiếm
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(x => x.TenSP.Contains(q) || x.Slug.Contains(q));

            // Lọc loại & hãng
            if (maLoai.HasValue)
                query = query.Where(x => x.MaLoai == maLoai.Value);

            if (maTH.HasValue)
                query = query.Where(x => x.MaTH == maTH.Value);

            // Lọc tình trạng kho (y như Index)
            if (stock.HasValue)
            {
                switch (stock.Value)
                {
                    case 1: // Còn nhiều hàng (>5)
                        query = query.Where(x => x.IsActive && x.SoLuong > 5);
                        break;
                    case 2: // Sắp hết (1–5)
                        query = query.Where(x => x.IsActive && x.SoLuong > 0 && x.SoLuong <= 5);
                        break;
                    case 3: // Hết hàng (0)
                        query = query.Where(x => x.IsActive && x.SoLuong <= 0);
                        break;
                    case 4: // Ngừng kinh doanh
                        query = query.Where(x => !x.IsActive);
                        break;
                }
            }

            // Lọc khoảng giá
            if (minPrice.HasValue)
                query = query.Where(x => x.Gia >= minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(x => x.Gia <= maxPrice.Value);

            var products = await query
                .OrderByDescending(x => x.MaSP)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("SanPham");

            int row = 1;

            // Header
            ws.Cell(row, 1).Value = "STT";
            ws.Cell(row, 2).Value = "Mã SP";
            ws.Cell(row, 3).Value = "Tên sản phẩm";
            ws.Cell(row, 4).Value = "Hãng";
            ws.Cell(row, 5).Value = "Loại";
            ws.Cell(row, 6).Value = "Giá";
            ws.Cell(row, 7).Value = "Số lượng";
            ws.Cell(row, 8).Value = "Trạng thái";

            var headerRange = ws.Range(row, 1, row, 8);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            // Data
            int stt = 1;
            foreach (var p in products)
            {
                row++;
                ws.Cell(row, 1).Value = stt++;
                ws.Cell(row, 2).Value = p.MaSP;
                ws.Cell(row, 3).Value = p.TenSP;
                ws.Cell(row, 4).Value = p.ThuongHieu?.TenTH;
                ws.Cell(row, 5).Value = p.Loai?.TenLoai;
                ws.Cell(row, 6).Value = p.Gia;
                ws.Cell(row, 7).Value = p.SoLuong;
                ws.Cell(row, 8).Value = p.IsActive ? "Đang bán" : "Ngừng KD";
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var fileName = $"DanhSachSanPham_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName
            );
        }

        // ================================================================
        // HELPER FUNCTIONS
        // ================================================================
        private async Task FillDropdowns(int? maLoai = null, int? maTH = null)
        {
            ViewBag.LoaiList = new SelectList(await _context.Loais.ToListAsync(), "MaLoai", "TenLoai", maLoai);
            ViewBag.THList = new SelectList(await _context.ThuongHieus.ToListAsync(), "MaTH", "TenTH", maTH);
        }

        private static string ToSlug(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var s = text.Trim().ToLowerInvariant();
            s = s.Replace('đ', 'd');

            var decomp = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decomp.Length);
            foreach (var ch in decomp)
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            s = sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
            s = Regex.Replace(s, @"\s+", "-");
            s = Regex.Replace(s, @"[^a-z0-9\\-]", "");
            s = Regex.Replace(s, @"-+", "-").Trim('-');
            return s;
        }
        // ================================================================
        // BULK ACTION (Ẩn/Hiện/Xóa nhiều sản phẩm cùng lúc)
        // ================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAction(string ids, string actionType)
        {
            if (string.IsNullOrWhiteSpace(ids) || string.IsNullOrWhiteSpace(actionType))
            {
                TempData["SuccessMessage"] = "Vui lòng chọn sản phẩm và thao tác.";
                return RedirectToAction(nameof(Index));
            }

            var idList = ids
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(i => i.HasValue)
                .Select(i => i!.Value)
                .ToList();

            if (!idList.Any())
            {
                TempData["SuccessMessage"] = "Không có sản phẩm hợp lệ được chọn.";
                return RedirectToAction(nameof(Index));
            }

            var products = await _context.SanPhams
                .Where(p => idList.Contains(p.MaSP))
                .ToListAsync();

            if (!products.Any())
            {
                TempData["SuccessMessage"] = "Không tìm thấy sản phẩm nào tương ứng.";
                return RedirectToAction(nameof(Index));
            }

            switch (actionType)
            {
                case "activate":
                    foreach (var p in products)
                        p.IsActive = true;
                    TempData["SuccessMessage"] = $"Đã chuyển {products.Count} sản phẩm sang trạng thái ĐANG BÁN.";
                    break;

                case "deactivate":
                    foreach (var p in products)
                        p.IsActive = false;
                    TempData["SuccessMessage"] = $"Đã chuyển {products.Count} sản phẩm sang trạng thái NGỪNG KINH DOANH.";
                    break;

                case "delete":
                    _context.SanPhams.RemoveRange(products);
                    TempData["SuccessMessage"] = $"Đã xóa {products.Count} sản phẩm.";
                    break;

                default:
                    TempData["SuccessMessage"] = "Thao tác không hợp lệ.";
                    return RedirectToAction(nameof(Index));
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        // ================================================================
        // QUICK UPDATE GIÁ + SỐ LƯỢNG (AJAX)
        // ================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickUpdate(int id, decimal gia, int soLuong)
        {
            var sp = await _context.SanPhams.FirstOrDefaultAsync(x => x.MaSP == id);
            if (sp == null)
            {
                return Json(new { ok = false, message = "Không tìm thấy sản phẩm." });
            }

            if (soLuong < 0) soLuong = 0;
            if (gia <= 0)
            {
                return Json(new { ok = false, message = "Giá phải lớn hơn 0." });
            }

            sp.Gia = gia;
            sp.SoLuong = soLuong;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                return Json(new { ok = false, message = "Có lỗi khi lưu dữ liệu." });
            }

            var giaFormatted = sp.Gia.ToString("N0");

            // Tính lại text + class cho badge số lượng
            string stockText;
            string badgeClass;

            if (!sp.IsActive)
            {
                stockText = "Ngừng KD";
                badgeClass = "bg-secondary";
            }
            else if (sp.SoLuong <= 0)
            {
                stockText = "Hết hàng";
                badgeClass = "bg-danger";
            }
            else if (sp.SoLuong <= 5)
            {
                stockText = $"Sắp hết ({sp.SoLuong})";
                badgeClass = "bg-warning text-dark";
            }
            else
            {
                stockText = $"Còn hàng ({sp.SoLuong})";
                badgeClass = "bg-success";
            }

            return Json(new
            {
                ok = true,
                giaFormatted,
                soLuong = sp.SoLuong,
                stockText,
                badgeClass
            });
        }
        // ================================================================
        // QUICK VIEW - Xem nhanh thông tin sản phẩm (AJAX + Partial)
        // ================================================================
        [HttpGet]
        public async Task<IActionResult> QuickDetails(int id)
        {
            var sp = await _context.SanPhams
                .Include(x => x.Loai)
                .Include(x => x.ThuongHieu)
                .FirstOrDefaultAsync(x => x.MaSP == id);

            if (sp == null) return NotFound();

            return PartialView("_QuickDetails", sp);
        }

    }
}