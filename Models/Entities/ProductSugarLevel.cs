namespace MilkTeaWeb.Models.Entities;

public class ProductSugarLevel
{
    public int ProductId { get; set; }

    public int SugarLevelId { get; set; }

    public Product Product { get; set; } = null!;

    public SugarLevel SugarLevel { get; set; } = null!;
}
