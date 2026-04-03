namespace WebBanXeMay.Helpers;
using WebBanXeMay.Models;

public static class TrangThaiExtensions
{
    public static string ToLabel(this TrangThaiDonHang s) => s switch
    {
        TrangThaiDonHang.ChoXacNhan => "Chờ xác nhận",
        TrangThaiDonHang.DangXuLy => "Đang xử lý",
        TrangThaiDonHang.DangGiao => "Đang giao",
        TrangThaiDonHang.HoanTat => "Hoàn tất",
        TrangThaiDonHang.DaHuy => "Đã hủy",
        _ => s.ToString()
    };

    // class bootstrap/tailwind tùy m
    public static string ToBadgeClass(this TrangThaiDonHang s) => s switch
    {
        TrangThaiDonHang.ChoXacNhan => "badge text-bg-warning",
        TrangThaiDonHang.DangXuLy => "badge text-bg-info",
        TrangThaiDonHang.DangGiao => "badge text-bg-primary",
        TrangThaiDonHang.HoanTat => "badge text-bg-success",
        TrangThaiDonHang.DaHuy => "badge text-bg-secondary",
        _ => "badge text-bg-light"
    };
}
