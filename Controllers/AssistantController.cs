using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Assistant;
using MilkTeaWeb.ViewModels.Orders;

namespace MilkTeaWeb.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class AssistantController(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LatestOrder(CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        var order = await context.Orders.AsNoTracking()
            .Where(currentOrder => currentOrder.UserId == customer.Id)
            .OrderByDescending(currentOrder => currentOrder.CreatedAt)
            .Select(currentOrder => new
            {
                currentOrder.OrderId,
                currentOrder.CreatedAt,
                currentOrder.StoreName,
                currentOrder.FulfillmentMethod,
                currentOrder.OrderStatus,
                currentOrder.TotalAmount
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return Json(new AssistantLatestOrderViewModel { HasOrder = false });
        }

        var vietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");
        var detailsUrl = Url.Action(
            nameof(OrdersController.Details),
            "Orders",
            new { id = order.OrderId }) ?? "/Orders";

        return Json(new AssistantLatestOrderViewModel
        {
            HasOrder = true,
            OrderId = order.OrderId,
            CreatedAtLabel = order.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy, HH:mm", vietnameseCulture),
            StoreName = order.StoreName,
            FulfillmentLabel = OrderDisplay.Fulfillment(order.FulfillmentMethod),
            StatusLabel = OrderDisplay.Status(order.OrderStatus, order.FulfillmentMethod),
            StatusKey = order.OrderStatus.ToString().ToLowerInvariant(),
            TotalAmountLabel = $"{order.TotalAmount.ToString("N0", vietnameseCulture)} đ",
            DetailsUrl = detailsUrl,
            CanCancel = order.OrderStatus == OrderStatus.Pending
        });
    }

    private async Task<ApplicationUser?> GetCurrentCustomerAsync()
    {
        var customer = await userManager.GetUserAsync(User);
        if (customer is null)
        {
            return null;
        }

        var isCustomer = await userManager.IsInRoleAsync(customer, RoleNames.Customer);
        var isAdmin = await userManager.IsInRoleAsync(customer, RoleNames.Admin);
        return isCustomer && !isAdmin ? customer : null;
    }
}
