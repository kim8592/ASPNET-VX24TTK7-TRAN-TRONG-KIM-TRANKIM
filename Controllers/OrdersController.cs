using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Orders;

namespace MilkTeaWeb.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class OrdersController(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    OrderService orderService,
    OrderNotificationService orderNotificationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? status, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        OrderStatus? selectedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusName = status.Trim();
            var isKnownStatusName = Enum.GetNames<OrderStatus>()
                .Contains(statusName, StringComparer.OrdinalIgnoreCase);

            if (!isKnownStatusName
                || !Enum.TryParse<OrderStatus>(statusName, ignoreCase: true, out var parsedStatus)
                || !Enum.IsDefined(typeof(OrderStatus), parsedStatus))
            {
                return NotFound();
            }

            selectedStatus = parsedStatus;
        }

        var ordersQuery = context.Orders.AsNoTracking()
            .Where(order => order.UserId == customer.Id)
            .AsQueryable();

        if (selectedStatus.HasValue)
        {
            ordersQuery = ordersQuery.Where(order => order.OrderStatus == selectedStatus.Value);
        }

        var orders = await ordersQuery
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => new OrderHistoryItemViewModel
            {
                OrderId = order.OrderId,
                CreatedAt = order.CreatedAt,
                OrderStatus = order.OrderStatus,
                FulfillmentMethod = order.FulfillmentMethod,
                StoreName = order.StoreName,
                TotalAmount = order.TotalAmount
            })
            .ToListAsync(cancellationToken);
        return View(new OrderHistoryViewModel
        {
            Orders = orders,
            SelectedStatus = selectedStatus,
            StatusOptions = OrderDisplay.StatusFilterOptions()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        var order = await context.Orders.AsNoTracking()
            .Where(currentOrder => currentOrder.OrderId == id && currentOrder.UserId == customer.Id)
            .Include(currentOrder => currentOrder.OrderItems)
            .ThenInclude(orderItem => orderItem.OrderItemToppings)
            .SingleOrDefaultAsync(cancellationToken);
        return order is null ? NotFound() : View(ToDetailsViewModel(order));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        var result = await orderService.CancelPendingOrderAsync(customer, id, cancellationToken);
        switch (result.Kind)
        {
            case CancelOrderResultKind.Succeeded:
                TempData["Success"] = "Đơn hàng đã được hủy.";
                var detailsUrl = Url.ActionLink(
                    nameof(Details),
                    "Orders",
                    new { area = string.Empty, id },
                    protocol: Request.Scheme) ?? string.Empty;
                var notificationResult = await orderNotificationService.SendOrderStatusChangedAsync(
                    id,
                    OrderStatus.Cancelled,
                    detailsUrl,
                    cancellationToken);
                if (notificationResult == OrderStatusNotificationResult.Failed)
                {
                    TempData["Warning"] = "Đơn hàng đã được hủy nhưng email thông báo chưa gửi được.";
                }
                break;
            case CancelOrderResultKind.NotFound:
                return NotFound();
            case CancelOrderResultKind.NotPending:
                TempData["Warning"] = "Trạng thái đơn hàng đã thay đổi. Vui lòng kiểm tra lại trước khi thao tác.";
                break;
            default:
                TempData["Error"] = "Không thể hủy đơn hàng lúc này. Vui lòng thử lại sau.";
                break;
        }

        return RedirectToAction(nameof(Details), new { id });
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

    private static OrderDetailsViewModel ToDetailsViewModel(Order order) => new()
    {
        OrderId = order.OrderId,
        CreatedAt = order.CreatedAt,
        OrderStatus = order.OrderStatus,
        FulfillmentMethod = order.FulfillmentMethod,
        PaymentMethod = order.PaymentMethod,
        StoreName = order.StoreName,
        StoreAddress = order.StoreAddress,
        StorePhone = order.StorePhone,
        RecipientName = order.RecipientName,
        RecipientPhone = order.RecipientPhone,
        DeliveryAddress = order.DeliveryAddress,
        OrderNote = order.OrderNote,
        StatusTimeline = OrderDisplay.Timeline(order.OrderStatus, order.FulfillmentMethod),
        Items = order.OrderItems
            .OrderBy(item => item.OrderItemId)
            .Select(item => new OrderDetailsItemViewModel
            {
                ProductName = item.ProductName,
                SizeName = item.SizeName,
                SugarPercentage = item.SugarPercentage,
                IcePercentage = item.IcePercentage,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                LineTotal = item.LineTotal,
                Toppings = item.OrderItemToppings
                    .OrderBy(topping => topping.OrderItemToppingId)
                    .Select(topping => new OrderDetailsToppingViewModel
                    {
                        ToppingName = topping.ToppingName,
                        UnitPrice = topping.UnitPrice
                    })
                    .ToList()
            })
            .ToList(),
        Subtotal = order.Subtotal,
        DeliveryFee = order.DeliveryFee,
        TotalAmount = order.TotalAmount
    };
}
