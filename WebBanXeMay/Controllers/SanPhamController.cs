using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Models;

public class SanPhamController : Controller
{
    private readonly AppDbContext _db;
    public SanPhamController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index(
     string? brand,
     string? loai,
     string? model,   // thêm model
     string? q,
     int page = 1,
     int pageSize = 12)
    {
        var query = _db.SanPhams
            .Include(x => x.ThuongHieu)
            .Include(x => x.Loai)
            .Where(x => x.IsActive);

        if (!string.IsNullOrWhiteSpace(brand))
            query = query.Where(x => x.ThuongHieu!.Slug == brand);

        if (!string.IsNullOrWhiteSpace(loai))
            query = query.Where(x => x.Loai!.Slug == loai);

        // === Lọc theo model (Vision, Wave, Airblade…) ===
        if (!string.IsNullOrWhiteSpace(model))
        {
            string keyword = model.ToLower();
            query = query.Where(x => x.TenSP.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.TenSP.Contains(q));

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(x => x.TenSP)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Brand = brand;
        ViewBag.Loai = loai;
        ViewBag.Model = model; // <-- thêm
        ViewBag.Q = q;

        ViewBag.Brands = await _db.ThuongHieus.OrderBy(x => x.TenTH).ToListAsync();
        ViewBag.Loais = await _db.Loais.OrderBy(x => x.TenLoai).ToListAsync();

        return View(items);
    }


    [HttpGet]
    [Route("SanPham/Details")]
    public async Task<IActionResult> Details(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return NotFound();

        var sanPham = await _db.SanPhams
            .Include(sp => sp.ThuongHieu)
            .Include(sp => sp.Loai)
            .FirstOrDefaultAsync(m => m.Slug == slug);

        if (sanPham == null) return NotFound();

        // Đánh giá (m đã có)
        var productReviews = await _db.DanhGias
            .Where(d => d.MaSP == sanPham.MaSP)
            .OrderByDescending(d => d.NgayDanhGia)
            .ToListAsync();
        ViewBag.ProductReviews = productReviews;

        // ====== THÊM: Ảnh & Thông số demo (chạy ngay, sau này thay bằng DB) ======
        ViewBag.Images = new[] { sanPham.ImageUrl ?? "/images/no-image.png" }; // có 1 ảnh cũng ok
        ViewBag.Specs = new Dictionary<string, string>
        {
            ["Phân khúc"] = sanPham.Loai?.TenLoai ?? "Xe máy",
            ["Thương hiệu"] = sanPham.ThuongHieu?.TenTH ?? "—",
            ["Tình trạng"] = sanPham.IsActive ? "Đang bán" : "Ngừng kinh doanh"
        };
        // ========================================================================
        // ===== SẢN PHẨM LIÊN QUAN =====
        // Lấy các sản phẩm đang bán, khác sản phẩm hiện tại
        var relatedQuery = _db.SanPhams
            .Include(x => x.ThuongHieu)
            .Include(x => x.Loai)
            .Where(x => x.IsActive && x.Slug != slug && x.MaSP != sanPham.MaSP);

        // Ưu tiên cùng Loại, nếu không có thì cùng Thương hiệu
        if (sanPham.Loai != null)
        {
            relatedQuery = relatedQuery.Where(x => x.Loai!.Slug == sanPham.Loai.Slug);
        }
        else if (sanPham.ThuongHieu != null)
        {
            relatedQuery = relatedQuery.Where(x => x.ThuongHieu!.Slug == sanPham.ThuongHieu.Slug);
        }

        // Sắp xếp & giới hạn số lượng
        var related = await relatedQuery
            .OrderByDescending(x => x.Gia)
            .Take(4)
            .ToListAsync();

        ViewBag.Related = related;

        return View(sanPham);
    }


}
