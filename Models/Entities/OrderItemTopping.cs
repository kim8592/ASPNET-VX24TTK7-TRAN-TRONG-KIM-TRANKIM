namespace MilkTeaWeb.Models.Entities;

public class OrderItemTopping
{
    public int OrderItemToppingId { get; set; }

    public int OrderItemId { get; set; }

    public int? ToppingId { get; set; }

    public string ToppingName { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public OrderItem OrderItem { get; set; } = null!;

    public Topping? Topping { get; set; }
}
