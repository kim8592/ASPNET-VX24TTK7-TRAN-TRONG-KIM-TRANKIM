namespace MilkTeaWeb.Models.Entities;

public class Product
{
    public int ProductId { get; set; }

    public int CategoryId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsAvailable { get; set; } = true;

    public Category Category { get; set; } = null!;

    public ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();

    public ICollection<ProductSize> ProductSizes { get; set; } = new List<ProductSize>();

    public ICollection<ProductTopping> ProductToppings { get; set; } = new List<ProductTopping>();

    public ICollection<ProductSugarLevel> ProductSugarLevels { get; set; } = new List<ProductSugarLevel>();

    public ICollection<ProductIceLevel> ProductIceLevels { get; set; } = new List<ProductIceLevel>();

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
