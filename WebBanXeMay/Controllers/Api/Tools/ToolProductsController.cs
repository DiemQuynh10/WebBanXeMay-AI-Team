using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Dtos.ToolApi;
using WebBanXeMay.Infrastructure.Security;
using WebBanXeMay.Models;

namespace WebBanXeMay.Controllers.Api.Tools
{
    [ApiController]
    [Route("api/tools/products")]
    public class ToolProductsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public ToolProductsController(AppDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        private bool CheckKey(out IActionResult? error)
        {
            if (!ToolApiKeyValidator.IsValid(Request, _config))
            {
                error = ToolApiKeyValidator.UnauthorizedResult();
                return false;
            }
            error = null;
            return true;
        }
        private static string NormalizeText(string? input)
        {
            return (input ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static IQueryable<SanPham> ApplyCategoryFilter(IQueryable<SanPham> query, string? category)
        {
            var requestedCategory = NormalizeText(category);

            if (string.IsNullOrWhiteSpace(requestedCategory))
                return query;

            if (requestedCategory.Contains("xe ga") || requestedCategory.Contains("tay ga") || requestedCategory.Contains("scooter"))
            {
                return query.Where(x =>
                    x.Loai != null &&
                    (
                        x.Loai.TenLoai.ToLower().Contains("ga") ||
                        x.Loai.TenLoai.ToLower().Contains("tay ga") ||
                        x.Loai.TenLoai.ToLower().Contains("scooter")
                    ));
            }

            if (requestedCategory.Contains("xe số") || requestedCategory == "số")
            {
                return query.Where(x =>
                    x.Loai != null &&
                    x.Loai.TenLoai.ToLower().Contains("số"));
            }

            if (requestedCategory.Contains("côn") || requestedCategory.Contains("côn tay"))
            {
                return query.Where(x =>
                    x.Loai != null &&
                    x.Loai.TenLoai.ToLower().Contains("côn"));
            }

            return query.Where(x =>
                x.Loai != null &&
                x.Loai.TenLoai.ToLower().Contains(requestedCategory));
        }

        // GET: /api/tools/products/search?keyword=vision&take=10
        [HttpGet("search")]
        public async Task<IActionResult> Search(
            [FromQuery] string? keyword,
            [FromQuery] int take = 10)
        {
            if (!CheckKey(out var err)) return err!;
            take = Math.Clamp(take, 1, 50);

            var query = _db.SanPhams
                .Include(x => x.ThuongHieu)
                .Include(x => x.Loai)
                .Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = NormalizeText(keyword);

                query = query.Where(x =>
                    x.TenSP.ToLower().Contains(k) ||
                    (x.ThuongHieu != null && x.ThuongHieu.TenTH.ToLower().Contains(k)) ||
                    (x.Loai != null && x.Loai.TenLoai.ToLower().Contains(k)) ||
                    (!string.IsNullOrEmpty(x.MoTa) && x.MoTa.ToLower().Contains(k)));
            }
            var publicBaseUrl = _config["PublicBaseUrl"];
            var baseUrl = !string.IsNullOrWhiteSpace(publicBaseUrl)
                ? publicBaseUrl.TrimEnd('/')
                : $"{Request.Scheme}://{Request.Host}";
            var items = await query
                .OrderBy(x => x.TenSP)
                .Take(take)
                .Select(x => new ProductSummaryDto
                {
                    Id = x.MaSP,
                    Ten = x.TenSP,
                    Slug = x.Slug,
                    Gia = x.Gia,
                    SoLuong = x.SoLuong,
                    CC = x.CC,
                    ImageUrl = string.IsNullOrEmpty(x.ImageUrl) ? null : baseUrl + x.ImageUrl,
                    ThuongHieu = x.ThuongHieu != null ? x.ThuongHieu.TenTH : "",
                    Loai = x.Loai != null ? x.Loai.TenLoai : "",
                    Tags = x.Tags
                })
                .ToListAsync();

            return Ok(new { count = items.Count, items });
        }
        // GET: /api/tools/products/by-price-range?minPrice=30000000&maxPrice=50000000&take=10
        [HttpGet("by-price-range")]
        public async Task<IActionResult> GetByPriceRange(
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] int take = 10)
        {
            if (!CheckKey(out var err)) return err!;
            take = Math.Clamp(take, 1, 50);

            var query = _db.SanPhams
                .Include(x => x.ThuongHieu)
                .Include(x => x.Loai)
                .Where(x => x.IsActive);

            if (minPrice.HasValue)
            {
                query = query.Where(x => x.Gia >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(x => x.Gia <= maxPrice.Value);
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var items = await query
                .OrderBy(x => x.Gia)
                .ThenBy(x => x.TenSP)
                .Take(take)
                .Select(x => new ProductSummaryDto
                {
                    Id = x.MaSP,
                    Ten = x.TenSP,
                    Slug = x.Slug,
                    Gia = x.Gia,
                    SoLuong = x.SoLuong,
                    CC = x.CC,
                    ImageUrl = string.IsNullOrEmpty(x.ImageUrl) ? null : baseUrl + x.ImageUrl,
                    ThuongHieu = x.ThuongHieu != null ? x.ThuongHieu.TenTH : "",
                    Loai = x.Loai != null ? x.Loai.TenLoai : "",
                    Tags = x.Tags
                })
                .ToListAsync();

            return Ok(new
            {
                count = items.Count,
                minPrice,
                maxPrice,
                items
            });
        }

        // GET: /api/tools/products/5
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            if (!CheckKey(out var err)) return err!;

            var p = await _db.SanPhams
                .Include(x => x.ThuongHieu)
                .Include(x => x.Loai)
                .FirstOrDefaultAsync(x => x.MaSP == id);

            if (p == null) return NotFound(new { error = "Product not found" });

            var dto = new ProductDetailDto
            {
                Id = p.MaSP,
                Ten = p.TenSP,
                Slug = p.Slug,
                Gia = p.Gia,
                SoLuong = p.SoLuong,
                CC = p.CC,
                ImageUrl = p.ImageUrl,
                ThuongHieu = p.ThuongHieu != null ? p.ThuongHieu.TenTH : "",
                Loai = p.Loai != null ? p.Loai.TenLoai : "",
                MoTa = p.MoTa,
                IsActive = p.IsActive
            };

            return Ok(dto);
        }
        [HttpGet("by-brand-and-price")]
        public async Task<IActionResult> GetByBrandAndPrice(
    [FromQuery] string? brand,
    [FromQuery] decimal maxPrice,
    [FromQuery] string? category,
    [FromQuery] int take = 10)
        {
            if (!CheckKey(out var err)) return err!;
            take = Math.Clamp(take, 1, 50);

            if (string.IsNullOrWhiteSpace(brand))
            {
                return BadRequest(new { message = "Brand is required." });
            }

            var normalizedBrand = NormalizeText(brand);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var query = _db.SanPhams
                .Include(x => x.ThuongHieu)
                .Include(x => x.Loai)
                .Where(x => x.IsActive
                            && x.ThuongHieu != null
                            && x.ThuongHieu.TenTH.ToLower().Contains(normalizedBrand)
                            && x.Gia <= maxPrice);

            query = ApplyCategoryFilter(query, category);

            var items = await query
                .OrderBy(x => x.Gia)
                .ThenBy(x => x.TenSP)
                .Take(take)
                .Select(x => new ProductSummaryDto
                {
                    Id = x.MaSP,
                    Ten = x.TenSP,
                    Slug = x.Slug,
                    Gia = x.Gia,
                    SoLuong = x.SoLuong,
                    CC = x.CC,
                    ImageUrl = string.IsNullOrEmpty(x.ImageUrl) ? null : baseUrl + x.ImageUrl,
                    ThuongHieu = x.ThuongHieu != null ? x.ThuongHieu.TenTH : "",
                    Loai = x.Loai != null ? x.Loai.TenLoai : "",
                    Tags = x.Tags
                })
                .ToListAsync();

            return Ok(new
            {
                count = items.Count,
                brand,
                maxPrice,
                category,
                items
            });
        }
        [HttpGet("by-filters")]
        public async Task<IActionResult> GetByFilters(
    [FromQuery] string? brand,
    [FromQuery] decimal? minPrice,
    [FromQuery] decimal? maxPrice,
    [FromQuery] string? category,
    [FromQuery] int take = 10)
        {
            if (!CheckKey(out var err)) return err!;
            take = Math.Clamp(take, 1, 50);

            var query = _db.SanPhams
                .Include(x => x.ThuongHieu)
                .Include(x => x.Loai)
                .Where(x => x.IsActive);

            if (!string.IsNullOrWhiteSpace(brand))
            {
                var normalizedBrand = brand.Trim().ToLower();
                query = query.Where(x => x.ThuongHieu != null &&
                                         x.ThuongHieu.TenTH.ToLower().Contains(normalizedBrand));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(x => x.Gia >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(x => x.Gia <= maxPrice.Value);
            }

            query = ApplyCategoryFilter(query, category);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var items = await query
                .OrderBy(x => x.Gia)
                .ThenBy(x => x.TenSP)
                .Take(take)
                .Select(x => new ProductSummaryDto
                {
                    Id = x.MaSP,
                    Ten = x.TenSP,
                    Slug = x.Slug,
                    Gia = x.Gia,
                    SoLuong = x.SoLuong,
                    CC = x.CC,
                    ImageUrl = string.IsNullOrEmpty(x.ImageUrl) ? null : baseUrl + x.ImageUrl,
                    ThuongHieu = x.ThuongHieu != null ? x.ThuongHieu.TenTH : "",
                    Loai = x.Loai != null ? x.Loai.TenLoai : "",
                    Tags= x.Tags
                })
                .ToListAsync();

            return Ok(new
            {
                count = items.Count,
                brand,
                minPrice,
                maxPrice,
                category,
                items
            });
        }
    }

}