namespace MilkTeaWeb.Models.Entities;

public class ProductImage
{
    public int ProductImageId { get; set; }

    public int ProductId { get; set; }

    public string ImagePath { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public Product Product { get; set; } = null!;
}
