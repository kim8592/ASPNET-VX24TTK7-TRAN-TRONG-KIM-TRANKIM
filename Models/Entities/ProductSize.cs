namespace MilkTeaWeb.Models.Entities;

public class ProductSize
{
    public int ProductSizeId { get; set; }

    public int ProductId { get; set; }

    public int SizeId { get; set; }

    public decimal Price { get; set; }

    public Product Product { get; set; } = null!;

    public Size Size { get; set; } = null!;

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
