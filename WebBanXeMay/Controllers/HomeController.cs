// Controllers/HomeController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Data;
using WebBanXeMay.Models;
using WebBanXeMay.Models.ViewModels;
using System.Linq;

public class HomeController : Controller
{
    private readonly AppDbContext _db;
    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        // ===== 1) Lấy top bán chạy theo MaSP =====
        var soldAgg = await _db.ChiTietDHs
            .Include(ct => ct.DonHang)
            .Where(ct => ct.DonHang!.TrangThai == TrangThaiDonHang.HoanTat)
            .GroupBy(ct => ct.MaSP)
            .Select(g => new
            {
                MaSP = g.Key,
                Sold = g.Sum(x => x.SoLuong),
                Revenue = g.Sum(x => x.SoLuong * x.Gia)
            })
            .OrderByDescending(x => x.Sold)
            .ThenByDescending(x => x.Revenue)
            .Take(8)
            .ToListAsync();

        var ids = soldAgg.Select(x => x.MaSP).ToList();

        // ===== 2) Lấy thông tin sản phẩm tương ứng (kèm Hãng/Loại) =====
        var spList = await _db.SanPhams
            .Include(sp => sp.ThuongHieu)
            .Include(sp => sp.Loai)
            .Where(sp => ids.Contains(sp.MaSP))
            .ToListAsync();

        var map = spList.ToDictionary(sp => sp.MaSP);
        var topSelling = soldAgg
            .Where(a => map.ContainsKey(a.MaSP))
            .Select(a => new TopSellingVM
            {
                SanPham = map[a.MaSP],
                Sold = a.Sold,
                Revenue = a.Revenue
            })
            .ToList();

        ViewBag.TopSelling = topSelling;


        // =================================================================
        // ===== 3) Lấy Đánh giá Nổi bật cho mục "Khách hàng nói gì" =====
        // =================================================================
        var featuredReviews = await _db.DanhGias
            .Include(d => d.SanPham) // Nếu muốn hiển thị tên sản phẩm được đánh giá
            .Where(d => d.IsFeatured == true)
            .OrderByDescending(d => d.NgayDanhGia)
            .Take(3) // Lấy tối đa 3 đánh giá mới nhất, giống như trong ảnh mẫu
            .ToListAsync();

        // Gửi danh sách đánh giá nổi bật tới View
        ViewBag.FeaturedReviews = featuredReviews;


        return View();
    }
    public async Task<IActionResult> UuDai()
    {
        var now = DateTime.Now;
        var vouchers = _db.Vouchers
            .Where(v => !v.IsDeleted
                     && v.IsActive
                     && v.NgayBatDau <= now
                     && v.NgayKetThuc >= now
                     && v.SoLuongDaDung < v.SoLuong)
            .ToList();

        return View(vouchers);
    }
}