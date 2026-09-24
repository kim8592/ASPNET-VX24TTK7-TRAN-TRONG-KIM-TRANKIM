namespace MilkTeaWeb.Models.Entities;

public class SugarLevel
{
    public int SugarLevelId { get; set; }

    public int Percentage { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<ProductSugarLevel> ProductSugarLevels { get; set; } = new List<ProductSugarLevel>();

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
