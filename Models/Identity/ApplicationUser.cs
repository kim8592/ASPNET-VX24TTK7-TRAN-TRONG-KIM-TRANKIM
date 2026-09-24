using Microsoft.AspNetCore.Identity;
using MilkTeaWeb.Models.Entities;

namespace MilkTeaWeb.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public string? DefaultDeliveryAddress { get; set; }

    public Cart? Cart { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
