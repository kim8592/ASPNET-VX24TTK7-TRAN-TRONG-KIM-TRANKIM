namespace MilkTeaWeb.Models.Entities;

public class Size
{
    public int SizeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public ICollection<ProductSize> ProductSizes { get; set; } = new List<ProductSize>();
}
