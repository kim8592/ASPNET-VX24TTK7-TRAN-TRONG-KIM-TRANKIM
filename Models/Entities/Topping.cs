namespace MilkTeaWeb.Models.Entities;

public class Topping
{
    public int ToppingId { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ProductTopping> ProductToppings { get; set; } = new List<ProductTopping>();

    public ICollection<CartItemTopping> CartItemToppings { get; set; } = new List<CartItemTopping>();

    public ICollection<OrderItemTopping> OrderItemToppings { get; set; } = new List<OrderItemTopping>();
}
