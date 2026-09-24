namespace MilkTeaWeb.Models.Entities;

public class IceLevel
{
    public int IceLevelId { get; set; }

    public int Percentage { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<ProductIceLevel> ProductIceLevels { get; set; } = new List<ProductIceLevel>();

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
