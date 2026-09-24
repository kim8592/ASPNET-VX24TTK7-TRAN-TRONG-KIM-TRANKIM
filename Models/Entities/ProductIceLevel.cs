namespace MilkTeaWeb.Models.Entities;

public class ProductIceLevel
{
    public int ProductId { get; set; }

    public int IceLevelId { get; set; }

    public Product Product { get; set; } = null!;

    public IceLevel IceLevel { get; set; } = null!;
}
