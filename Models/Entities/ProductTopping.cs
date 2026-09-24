namespace MilkTeaWeb.Models.Entities;

public class ProductTopping
{
    public int ProductId { get; set; }

    public int ToppingId { get; set; }

    public Product Product { get; set; } = null!;

    public Topping Topping { get; set; } = null!;
}
