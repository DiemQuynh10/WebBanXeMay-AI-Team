using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebBanXeMay.Models;

namespace WebBanXeMay.Data
{
    public static class DataSeeder
    {
        public static async Task SeedAsync(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AppDbContext context)
        {
            // 1️⃣ Tạo Roles
            string[] roles = { "Admin", "Customer" };
            foreach (var role in roles)
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));

            // 2️⃣ Tạo tài khoản Admin
            if (await userManager.FindByEmailAsync("admin@webbanxemay.com") == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = "admin@webbanxemay.com",
                    Email = "admin@webbanxemay.com",
                    TenHienThi = "Quản trị viên"
                };
                await userManager.CreateAsync(admin, "Admin@123");
                await userManager.AddToRoleAsync(admin, "Admin");
            }

            // 3️⃣ Seed Loại xe
            if (!context.Loais.Any())
            {
                context.Loais.AddRange(
                    new Loai { TenLoai = "Xe Số", Slug = "xe-so" },
                    new Loai { TenLoai = "Tay Ga", Slug = "tay-ga" },
                    new Loai { TenLoai = "Côn Tay", Slug = "con-tay" }
                );
                await context.SaveChangesAsync();
            }

            // 4️⃣ Seed Thương Hiệu
            if (!context.ThuongHieus.Any())
            {
                context.ThuongHieus.AddRange(
                    new ThuongHieu { TenTH = "Honda", Slug = "honda" },
                    new ThuongHieu { TenTH = "Yamaha", Slug = "yamaha" },
                    new ThuongHieu { TenTH = "Suzuki", Slug = "suzuki" },
                    new ThuongHieu { TenTH = "SYM", Slug = "sym" },
                    new ThuongHieu { TenTH = "Piaggio", Slug = "piaggio" }
                );
                await context.SaveChangesAsync();
            }

            //10 seed sản phẩm
            if (!context.SanPhams.Any())
            {
                context.SanPhams.AddRange(

                    // ===== HONDA =====
                    new SanPham { TenSP = "Honda Air Blade", Slug = "airblade", Gia = 45000000, SoLuong = 10, MaLoai = 2, MaTH = 1, ImageUrl = "/images/honda/airblade.jpg" },
                    new SanPham { TenSP = "Honda CBR 150R", Slug = "cbr150r", Gia = 72000000, SoLuong = 6, MaLoai = 3, MaTH = 1, ImageUrl = "/images/honda/cbr150r.jpg" },
                    new SanPham { TenSP = "Honda Cub 125", Slug = "cub125", Gia = 85000000, SoLuong = 5, MaLoai = 1, MaTH = 1, ImageUrl = "/images/honda/cub125.jpg" },
                    new SanPham { TenSP = "Honda Future", Slug = "future", Gia = 31000000, SoLuong = 8, MaLoai = 1, MaTH = 1, ImageUrl = "/images/honda/future.jpg" },
                    new SanPham { TenSP = "Honda PCX", Slug = "pcx", Gia = 56000000, SoLuong = 7, MaLoai = 2, MaTH = 1, ImageUrl = "/images/honda/pcx.jpg" },
                    new SanPham { TenSP = "Honda Rebel 500", Slug = "rebel500", Gia = 180000000, SoLuong = 3, MaLoai = 3, MaTH = 1, ImageUrl = "/images/honda/rebel500.jpg" },
                    new SanPham { TenSP = "Honda SH 150i", Slug = "sh150i", Gia = 95000000, SoLuong = 5, MaLoai = 2, MaTH = 1, ImageUrl = "/images/honda/sh150i.jpg" },
                    new SanPham { TenSP = "Honda Vision", Slug = "vision", Gia = 31000000, SoLuong = 9, MaLoai = 2, MaTH = 1, ImageUrl = "/images/honda/vision.jpg" },
                    new SanPham { TenSP = "Honda Wave", Slug = "wave", Gia = 19000000, SoLuong = 12, MaLoai = 1, MaTH = 1, ImageUrl = "/images/honda/wave.jpg" },
                    new SanPham { TenSP = "Honda Winner X", Slug = "winnerx", Gia = 47000000, SoLuong = 6, MaLoai = 3, MaTH = 1, ImageUrl = "/images/honda/winnerx.jpg" },

                    // ===== YAMAHA =====
                    new SanPham { TenSP = "Yamaha Exciter", Slug = "exciter", Gia = 48000000, SoLuong = 7, MaLoai = 3, MaTH = 2, ImageUrl = "/images/yamaha/exciter.jpg" },
                    new SanPham { TenSP = "Yamaha Freego", Slug = "freego", Gia = 33000000, SoLuong = 8, MaLoai = 2, MaTH = 2, ImageUrl = "/images/yamaha/freego.jpg" },
                    new SanPham { TenSP = "Yamaha Grande", Slug = "grande", Gia = 45000000, SoLuong = 6, MaLoai = 2, MaTH = 2, ImageUrl = "/images/yamaha/grande.jpg" },
                    new SanPham { TenSP = "Yamaha Jupiter", Slug = "jupiter", Gia = 30000000, SoLuong = 10, MaLoai = 1, MaTH = 2, ImageUrl = "/images/yamaha/jupiter.jpg" },
                    new SanPham { TenSP = "Yamaha Latte", Slug = "latte", Gia = 42000000, SoLuong = 8, MaLoai = 2, MaTH = 2, ImageUrl = "/images/yamaha/latte.jpg" },
                    new SanPham { TenSP = "Yamaha MT15", Slug = "mt15", Gia = 78000000, SoLuong = 5, MaLoai = 3, MaTH = 2, ImageUrl = "/images/yamaha/mt15.jpg" },
                    new SanPham { TenSP = "Yamaha NVX 155", Slug = "nvx155", Gia = 55000000, SoLuong = 4, MaLoai = 2, MaTH = 2, ImageUrl = "/images/yamaha/nvx155.jpg" },
                    new SanPham { TenSP = "Yamaha R15 V4", Slug = "r15v4", Gia = 90000000, SoLuong = 3, MaLoai = 3, MaTH = 2, ImageUrl = "/images/yamaha/r15v4.jpg" },
                    new SanPham { TenSP = "Yamaha Sirius", Slug = "sirius", Gia = 21000000, SoLuong = 9, MaLoai = 1, MaTH = 2, ImageUrl = "/images/yamaha/sirius.jpg" },
                    new SanPham { TenSP = "Yamaha XRS 155", Slug = "xrs155", Gia = 87000000, SoLuong = 2, MaLoai = 3, MaTH = 2, ImageUrl = "/images/yamaha/xrs155.jpg" },

                    // ===== SUZUKI =====
                    new SanPham { TenSP = "Suzuki Address 110", Slug = "address110", Gia = 28000000, SoLuong = 6, MaLoai = 1, MaTH = 3, ImageUrl = "/images/suzuki/address110.jpg" },
                    new SanPham { TenSP = "Suzuki Axelo 125", Slug = "axelo125", Gia = 29000000, SoLuong = 5, MaLoai = 1, MaTH = 3, ImageUrl = "/images/suzuki/axelo125.jpg" },
                    new SanPham { TenSP = "Suzuki Burgman 125", Slug = "burgman125", Gia = 49000000, SoLuong = 4, MaLoai = 2, MaTH = 3, ImageUrl = "/images/suzuki/burgman125.jpg" },
                    new SanPham { TenSP = "Suzuki GD110", Slug = "gd110", Gia = 28000000, SoLuong = 8, MaLoai = 1, MaTH = 3, ImageUrl = "/images/suzuki/gd110.jpg" },
                    new SanPham { TenSP = "Suzuki GRX R150", Slug = "grx-r150", Gia = 71000000, SoLuong = 3, MaLoai = 3, MaTH = 3, ImageUrl = "/images/suzuki/grx-r150.jpg" },
                    new SanPham { TenSP = "Suzuki Impulse 125", Slug = "impulse125", Gia = 29000000, SoLuong = 6, MaLoai = 2, MaTH = 3, ImageUrl = "/images/suzuki/immpulse125.jpg" },
                    new SanPham { TenSP = "Suzuki Raider 150", Slug = "raider150", Gia = 52000000, SoLuong = 5, MaLoai = 3, MaTH = 3, ImageUrl = "/images/suzuki/raider150.jpg" },
                    new SanPham { TenSP = "Suzuki Satria F150", Slug = "satriaf150", Gia = 54000000, SoLuong = 5, MaLoai = 3, MaTH = 3, ImageUrl = "/images/suzuki/satriaf150.jpg" },
                    new SanPham { TenSP = "Suzuki Smash 110", Slug = "smash110", Gia = 18000000, SoLuong = 9, MaLoai = 1, MaTH = 3, ImageUrl = "/images/suzuki/smash110.jpg" },
                    new SanPham { TenSP = "Suzuki V-Strom", Slug = "vstrom", Gia = 129000000, SoLuong = 2, MaLoai = 3, MaTH = 3, ImageUrl = "/images/suzuki/vstrom.jpg" },

                    // ===== SYM =====
                    new SanPham { TenSP = "SYM Angela 50", Slug = "angela50", Gia = 18000000, SoLuong = 7, MaLoai = 1, MaTH = 4, ImageUrl = "/images/sym/angela50.jpg" },
                    new SanPham { TenSP = "SYM Attila Venus", Slug = "atila-venus", Gia = 28000000, SoLuong = 4, MaLoai = 2, MaTH = 4, ImageUrl = "/images/sym/atila-venus.jpg" },
                    new SanPham { TenSP = "SYM Elegant 110", Slug = "elegant110", Gia = 21000000, SoLuong = 8, MaLoai = 1, MaTH = 4, ImageUrl = "/images/sym/elegant110.jpg" },
                    new SanPham { TenSP = "SYM Elite 50", Slug = "elite50", Gia = 22000000, SoLuong = 5, MaLoai = 2, MaTH = 4, ImageUrl = "/images/sym/elite50.jpg" },
                    new SanPham { TenSP = "SYM Galaxy 125", Slug = "galaxy125", Gia = 25000000, SoLuong = 6, MaLoai = 1, MaTH = 4, ImageUrl = "/images/sym/galaxy125.jpg" },
                    new SanPham { TenSP = "SYM Galaxy Sport", Slug = "galaxysport", Gia = 27000000, SoLuong = 5, MaLoai = 3, MaTH = 4, ImageUrl = "/images/sym/galaxysport.jpg" },
                    new SanPham { TenSP = "SYM Husky", Slug = "husky", Gia = 36000000, SoLuong = 3, MaLoai = 3, MaTH = 4, ImageUrl = "/images/sym/husky.jpg" },
                    new SanPham { TenSP = "SYM Passing 50", Slug = "passing50", Gia = 20000000, SoLuong = 7, MaLoai = 1, MaTH = 4, ImageUrl = "/images/sym/passing50.jpg" },
                    new SanPham { TenSP = "SYM Shark Mini", Slug = "sharkmini", Gia = 28000000, SoLuong = 5, MaLoai = 2, MaTH = 4, ImageUrl = "/images/sym/sharkmini.jpg" },
                    new SanPham { TenSP = "SYM Star SR 125", Slug = "star-sr-125", Gia = 26000000, SoLuong = 6, MaLoai = 1, MaTH = 4, ImageUrl = "/images/sym/star-sr-125.jpg" },

                    // ===== PIAGGIO =====
                    new SanPham { TenSP = "Piaggio Beverly 300", Slug = "beverly300", Gia = 130000000, SoLuong = 3, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/beverly300.jpg" },
                    new SanPham { TenSP = "Piaggio Liberty 125", Slug = "liberty125", Gia = 58000000, SoLuong = 6, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/liberty125.jpg" },
                    new SanPham { TenSP = "Piaggio Medley 150", Slug = "medley150", Gia = 87000000, SoLuong = 3, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/medley150.jpg" },
                    new SanPham { TenSP = "Piaggio MP3 400", Slug = "mp3400", Gia = 320000000, SoLuong = 1, MaLoai = 3, MaTH = 5, ImageUrl = "/images/piaggio/mp3400.jpg" },
                    new SanPham { TenSP = "Piaggio Vespa GTS 150", Slug = "vespa-gts150", Gia = 95000000, SoLuong = 3, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/vespa-gts150.jpg" },
                    new SanPham { TenSP = "Piaggio Vespa LX", Slug = "vespa-lx", Gia = 62000000, SoLuong = 4, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/vespa-lx.jpg" },
                    new SanPham { TenSP = "Piaggio Vespa Primavera", Slug = "vespa-primavera", Gia = 78000000, SoLuong = 5, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/vespa-primavera.jpg" },
                    new SanPham { TenSP = "Piaggio Vespa Sprint", Slug = "vespa-sprint", Gia = 73000000, SoLuong = 5, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/vespa-sprint.jpg" },
                    new SanPham { TenSP = "Piaggio Vespa Sprint S", Slug = "vespa-sprint-s", Gia = 76000000, SoLuong = 5, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/vespa-sprint-s.jpg" },
                    new SanPham { TenSP = "Piaggio Zip 100", Slug = "zip100", Gia = 37000000, SoLuong = 6, MaLoai = 2, MaTH = 5, ImageUrl = "/images/piaggio/zip100.jpg" }
                );
                context.SaveChanges();
            }

            await context.SaveChangesAsync();
            if (!context.UuDais.Any())
            {
                var deals = new List<UuDai>
    {
        new UuDai
        {
            TenUuDai = "Giảm 10% xe Honda",
            MoTa = "Tri ân khách hàng, giảm ngay 10% khi mua bất kỳ xe nào của hãng Honda.",
            HinhAnhUrl = "/images/deals/honda-deal.jpg",
            LoaiKM = LoaiKhuyenMai.PhanTram,
            GiaTri = 10,
            NgayBatDau = DateTime.Now.AddDays(-5),
            NgayKetThuc = DateTime.Now.AddDays(25),
            ThuongHieuId = 1
        },
        new UuDai
        {
            TenUuDai = "Giảm 2.000.000đ cho Exciter",
            MoTa = "Mua Yamaha Exciter 155 VVA ngay hôm nay, giảm trực tiếp 2 triệu đồng tiền mặt.",
            HinhAnhUrl = "/images/deals/exciter-deal.jpg",
            LoaiKM = LoaiKhuyenMai.SoTienGiam,
            GiaTri = 2000000,
            NgayBatDau = DateTime.Now,
            NgayKetThuc = DateTime.Now.AddDays(30),
            SanPhamId = 11 // ⚠️ cần ID chính xác của Exciter trong DB (xem chú thích bên dưới)
        },
        new UuDai
        {
            TenUuDai = "Tặng Mũ bảo hiểm xịn",
            MoTa = "Tặng 1 mũ bảo hiểm 3/4 trị giá 800.000đ khi mua bất kỳ dòng xe tay ga nào.",
            HinhAnhUrl = "/images/deals/helmet-deal.jpg",
            LoaiKM = LoaiKhuyenMai.QuaTang,
            GiaTri = 1,
            NgayBatDau = DateTime.Now.AddDays(-2),
            NgayKetThuc = DateTime.Now.AddDays(15),
            LoaiId = 2
        },
        new UuDai
        {
            TenUuDai = "Miễn phí vận chuyển",
            MoTa = "Miễn phí vận chuyển toàn quốc cho tất cả đơn hàng xe máy trong tháng này.",
            HinhAnhUrl = "/images/deals/shipping-deal.jpg",
            LoaiKM = LoaiKhuyenMai.QuaTang,
            GiaTri = 0,
            NgayBatDau = DateTime.Now,
            NgayKetThuc = DateTime.Now.AddMonths(1).AddDays(-DateTime.Now.Day + 1).AddDays(-1)
        }
    };

                context.UuDais.AddRange(deals);
                await context.SaveChangesAsync();
            }


        }
    }
}