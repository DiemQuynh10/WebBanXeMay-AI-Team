// Models/ViewModels/TopSellingVM.cs
using WebBanXeMay.Models;

namespace WebBanXeMay.Models.ViewModels
{
    public class TopSellingVM
    {
        public SanPham SanPham { get; set; } = default!;
        public int Sold { get; set; }
        public decimal Revenue { get; set; }
    }
}
