using MilkTeaWeb.Models.Identity;

namespace MilkTeaWeb.Models.Entities;

public class Cart
{
    public int CartId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
