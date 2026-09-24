namespace MilkTeaWeb.Options;

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    public decimal FixedDeliveryFee { get; set; }
}
