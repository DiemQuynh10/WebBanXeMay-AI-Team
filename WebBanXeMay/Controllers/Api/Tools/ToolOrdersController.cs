using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Dtos.ToolApi;
using WebBanXeMay.Helpers;
using WebBanXeMay.Infrastructure.Security;
using WebBanXeMay.Models;

namespace WebBanXeMay.Controllers.Api.Tools
{
    [ApiController]
    [Route("api/tools/orders")]
    public class ToolOrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        public ToolOrdersController(AppDbContext db, IConfiguration config)
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

        // GET: /api/tools/orders/lookup?maDH=12&phone=0987xxxxxx
        [HttpGet("lookup")]
        public async Task<IActionResult> Lookup([FromQuery] int maDH, [FromQuery] string phone)
        {
            if (!CheckKey(out var err)) return err!;
            if (maDH <= 0) return BadRequest(new { error = "maDH invalid" });

            var phoneNorm = PhoneHelper.Normalize(phone);
            if (string.IsNullOrWhiteSpace(phoneNorm))
                return BadRequest(new { error = "phone required" });

            var order = await _db.DonHangs
                .Include(x => x.ChiTietDHs)
                    .ThenInclude(ct => ct.SanPham) // nếu ChiTietDH có navigation SanPham
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.MaDH == maDH);

            if (order == null)
                return NotFound(new { error = "Order not found" });

            var orderPhoneNorm = PhoneHelper.Normalize(order.Phone);
            if (!string.Equals(orderPhoneNorm, phoneNorm, StringComparison.Ordinal))
                return NotFound(new { error = "Order not found (or phone not match)" });

            var dto = new OrderStatusDto
            {
                MaDH = order.MaDH,
                NgayDH = order.NgayDH,
                TongTien = order.TongTien,
                TrangThai = order.TrangThai.ToString(),

                NguoiNhan = order.NguoiNhan,
                Phone = order.Phone,
                DiaChi = order.DiaChi,

                SoTienCoc = order.SoTienCoc,
                TrangThaiCoc = order.TrangThaiCoc.ToString(),
                HanCoc = order.HanCoc,
                PhuongThucCoc = order.PhuongThucCoc,
                MaGiaoDichCoc = order.MaGiaoDichCoc,
                NgayCoc = order.NgayCoc,
                ThoiGianYeuCauCoc = order.ThoiGianYeuCauCoc,
                ThoiGianXacNhanCoc = order.ThoiGianXacNhanCoc,
                GhiChuCoc = order.GhiChuCoc,

                SoTienGiam = order.SoTienGiam,
                MaVoucher = order.MaVoucher,
                VoucherId = order.VoucherId
            };

            // Mapping item: tuỳ ChiTietDH của bạn có field tên gì
            dto.Items = order.ChiTietDHs.Select(ct => new OrderItemDto
            {
                MaSP = ct.MaSP,
                TenSP = ct.SanPham != null ? ct.SanPham.TenSP : $"SP#{ct.MaSP}",
                SoLuong = ct.SoLuong,
                DonGia = ct.Gia
            }).ToList();

            return Ok(dto);
        }
    }
}