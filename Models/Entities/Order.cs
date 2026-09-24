using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Models.Identity;

namespace MilkTeaWeb.Models.Entities;

public class Order
{
    public int OrderId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int StoreId { get; set; }

    public string StoreName { get; set; } = string.Empty;

    public string StoreAddress { get; set; } = string.Empty;

    public string StorePhone { get; set; } = string.Empty;

    public FulfillmentMethod FulfillmentMethod { get; set; }

    public string RecipientName { get; set; } = string.Empty;

    public string RecipientPhone { get; set; } = string.Empty;

    public string? DeliveryAddress { get; set; }

    public string? OrderNote { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal TotalAmount { get; set; }

    public OrderStatus OrderStatus { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Store Store { get; set; } = null!;

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
