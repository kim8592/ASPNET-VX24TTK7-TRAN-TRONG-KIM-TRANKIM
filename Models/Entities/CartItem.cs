namespace MilkTeaWeb.Models.Entities;

public class CartItem
{
    public int CartItemId { get; set; }

    public int CartId { get; set; }

    public int ProductSizeId { get; set; }

    public int? SugarLevelId { get; set; }

    public int? IceLevelId { get; set; }

    public int Quantity { get; set; }

    public Cart Cart { get; set; } = null!;

    public ProductSize ProductSize { get; set; } = null!;

    public SugarLevel? SugarLevel { get; set; }

    public IceLevel? IceLevel { get; set; }

    public ICollection<CartItemTopping> CartItemToppings { get; set; } = new List<CartItemTopping>();
}
