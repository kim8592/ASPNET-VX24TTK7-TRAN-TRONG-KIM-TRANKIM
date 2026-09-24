namespace MilkTeaWeb.Models.Entities;

public class CartItemTopping
{
    public int CartItemId { get; set; }

    public int ToppingId { get; set; }

    public CartItem CartItem { get; set; } = null!;

    public Topping Topping { get; set; } = null!;
}
