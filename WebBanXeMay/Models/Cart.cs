using WebBanXeMay.Models;
using WebBanXeMay.Models.ViewModels;

public class Cart
{
    public int Id { get; set; }

    public string UserId { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CartLine> Items { get; set; } = new List<CartLine>();
}