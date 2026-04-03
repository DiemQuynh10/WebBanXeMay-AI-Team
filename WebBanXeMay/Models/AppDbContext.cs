using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace WebBanXeMay.Models
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<TuVan> TuVans { get; set; }
        public DbSet<Loai> Loais => Set<Loai>();
        public DbSet<ThuongHieu> ThuongHieus => Set<ThuongHieu>();
        public DbSet<SanPham> SanPhams => Set<SanPham>();
        public DbSet<DonHang> DonHangs => Set<DonHang>();
        public DbSet<ChiTietDH> ChiTietDHs => Set<ChiTietDH>();

        public DbSet<Cart> Carts => Set<Cart>();
        public DbSet<CartLine> CartLines => Set<CartLine>();
        public DbSet<UuDai> UuDais { get; set; }
        public DbSet<DanhGia> DanhGias { get; set; }
        public DbSet<OrderLog> OrderLogs { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Voucher> Vouchers { get; set; }
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // Unique index
            b.Entity<Loai>().HasIndex(x => x.Slug).IsUnique();
            b.Entity<ThuongHieu>().HasIndex(x => x.Slug).IsUnique();
            b.Entity<SanPham>().HasIndex(x => x.Slug).IsUnique();

            // Precision & default
            b.Entity<SanPham>().Property(x => x.Gia).HasColumnType("decimal(18,2)");
            b.Entity<DonHang>().Property(x => x.TongTien).HasColumnType("decimal(18,2)");
            b.Entity<ChiTietDH>().Property(x => x.Gia).HasColumnType("decimal(18,2)");

            b.Entity<SanPham>().Property(x => x.IsActive).HasDefaultValue(true);

            // Relationship
            b.Entity<SanPham>()
                .HasOne(x => x.Loai)
                .WithMany(l => l.SanPhams)
                .HasForeignKey(x => x.MaLoai)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<SanPham>()
                .HasOne(x => x.ThuongHieu)
                .WithMany(t => t.SanPhams)
                .HasForeignKey(x => x.MaTH)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<DonHang>()
                .HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<ChiTietDH>()
                .HasOne(ct => ct.DonHang)
                .WithMany(d => d.ChiTietDHs)
                .HasForeignKey(ct => ct.MaDH)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<ChiTietDH>()
                .HasOne(ct => ct.SanPham)
                .WithMany()
                .HasForeignKey(ct => ct.MaSP)
                .OnDelete(DeleteBehavior.Restrict);

            // Một sản phẩm chỉ 1 lần trong 1 đơn
            b.Entity<ChiTietDH>()
                .HasIndex(ct => new { ct.MaDH, ct.MaSP })
                .IsUnique();
            // Mỗi user tối đa 1 cart “đang mở”
            b.Entity<Cart>()
                .HasIndex(x => x.UserId)
                .IsUnique();

            b.Entity<CartLine>()
                .HasOne(x => x.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(x => x.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            // (tuỳ chọn) ràng buộc dữ liệu
            b.Entity<CartLine>()
                .Property(x => x.Gia)
                .HasColumnType("decimal(18,2)");
        }
    }
}
