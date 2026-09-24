using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Security;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Admin.Orders;
using MilkTeaWeb.ViewModels.Orders;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class OrdersController(
    ApplicationDbContext context,
    OrderService orderService,
    OrderNotificationService orderNotificationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(AdminOrderFilterInputModel filter, CancellationToken cancellationToken)
    {
        if (!IsValidFilter(filter) || !ModelState.IsValid)
        {
            filter = new AdminOrderFilterInputModel();
            TempData["Warning"] = "Bộ lọc đơn hàng không hợp lệ đã được bỏ qua.";
        }

        var ordersQuery = context.Orders.AsNoTracking().AsQueryable();
        if (filter.Status.HasValue)
        {
            ordersQuery = ordersQuery.Where(order => order.OrderStatus == filter.Status.Value);
        }

        if (filter.StoreId.HasValue)
        {
            ordersQuery = ordersQuery.Where(order => order.StoreId == filter.StoreId.Value);
        }

        var orders = await ordersQuery
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.OrderId)
            .Select(order => new AdminOrderListItemViewModel
            {
                OrderId = order.OrderId,
                CreatedAt = order.CreatedAt,
                StoreName = order.StoreName,
                FulfillmentMethod = order.FulfillmentMethod,
                OrderStatus = order.OrderStatus,
                TotalAmount = order.TotalAmount
            })
            .ToListAsync(cancellationToken);

        var stores = await context.Stores.AsNoTracking()
            .OrderBy(store => store.Name)
            .ThenBy(store => store.StoreId)
            .Select(store => new AdminOrderStoreOptionViewModel
            {
                StoreId = store.StoreId,
                Name = store.Name,
                IsActive = store.IsActive
            })
            .ToListAsync(cancellationToken);

        return View(new AdminOrderListViewModel
        {
            Filter = filter,
            Stores = stores,
            Orders = orders
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking()
            .Where(currentOrder => currentOrder.OrderId == id)
            .Include(currentOrder => currentOrder.OrderItems)
            .ThenInclude(orderItem => orderItem.OrderItemToppings)
            .SingleOrDefaultAsync(cancellationToken);
        return order is null ? NotFound() : View(ToDetailsViewModel(order));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStatus(
        int id,
        OrderStatusActionInputModel input,
        CancellationToken cancellationToken)
    {
        if (!IsValidExpectedStatus(input))
        {
            TempData["Warning"] = StaleStateMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await orderService.AdvanceOrderStatusAsync(id, input.ExpectedStatus, cancellationToken);
        ApplyMutationFeedback(result, isCancellation: false);
        if (result.Kind == AdminOrderStatusMutationKind.Succeeded)
        {
            await NotifyCustomerAfterStatusChangeAsync(id, result.NewStatus!.Value, cancellationToken);
        }

        return result.Kind == AdminOrderStatusMutationKind.NotFound
            ? NotFound()
            : RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int id,
        OrderStatusActionInputModel input,
        CancellationToken cancellationToken)
    {
        if (!IsValidExpectedStatus(input))
        {
            TempData["Warning"] = StaleStateMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await orderService.CancelOrderByAdminAsync(id, input.ExpectedStatus, cancellationToken);
        ApplyMutationFeedback(result, isCancellation: true);
        if (result.Kind == AdminOrderStatusMutationKind.Succeeded)
        {
            await NotifyCustomerAfterStatusChangeAsync(id, result.NewStatus!.Value, cancellationToken);
        }

        return result.Kind == AdminOrderStatusMutationKind.NotFound
            ? NotFound()
            : RedirectToAction(nameof(Details), new { id });
    }

    private async Task NotifyCustomerAfterStatusChangeAsync(int orderId, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        var detailsUrl = Url.ActionLink(
            nameof(MilkTeaWeb.Controllers.OrdersController.Details),
            "Orders",
            new { area = string.Empty, id = orderId },
            protocol: Request.Scheme) ?? string.Empty;
        var result = await orderNotificationService.SendOrderStatusChangedAsync(
            orderId,
            newStatus,
            detailsUrl,
            cancellationToken);
        if (result == OrderStatusNotificationResult.Failed)
        {
            TempData["Warning"] = "Trạng thái đơn hàng đã được cập nhật nhưng email thông báo chưa gửi được.";
        }
    }

    private void ApplyMutationFeedback(AdminOrderStatusMutationResult result, bool isCancellation)
    {
        switch (result.Kind)
        {
            case AdminOrderStatusMutationKind.Succeeded:
                TempData["Success"] = isCancellation
                    ? "Đơn hàng đã được hủy."
                    : $"Đơn hàng đã chuyển sang trạng thái {OrderDisplay.Status(result.NewStatus!.Value)}.";
                break;
            case AdminOrderStatusMutationKind.Stale:
                TempData["Warning"] = StaleStateMessage;
                break;
            case AdminOrderStatusMutationKind.InvalidTransition:
                TempData["Warning"] = "Không thể thực hiện thao tác với trạng thái đơn hàng hiện tại.";
                break;
            case AdminOrderStatusMutationKind.PersistenceFailure:
                TempData["Error"] = "Không thể cập nhật đơn hàng lúc này. Vui lòng thử lại sau.";
                break;
        }
    }

    private static bool IsValidFilter(AdminOrderFilterInputModel filter) =>
        (!filter.Status.HasValue || Enum.IsDefined(typeof(OrderStatus), filter.Status.Value))
        && (!filter.StoreId.HasValue || filter.StoreId.Value > 0);

    private bool IsValidExpectedStatus(OrderStatusActionInputModel input) =>
        ModelState.IsValid && Enum.IsDefined(typeof(OrderStatus), input.ExpectedStatus);

    private static AdminOrderDetailsViewModel ToDetailsViewModel(Order order)
    {
        var canAdvance = OrderService.TryGetNextStatus(order.OrderStatus, order.FulfillmentMethod, out _);
        return new AdminOrderDetailsViewModel
        {
            OrderId = order.OrderId,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
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
            Items = order.OrderItems
                .OrderBy(item => item.OrderItemId)
                .Select(item => new AdminOrderDetailsItemViewModel
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
                        .Select(topping => new AdminOrderDetailsToppingViewModel
                        {
                            ToppingName = topping.ToppingName,
                            UnitPrice = topping.UnitPrice
                        })
                        .ToList()
                })
                .ToList(),
            Subtotal = order.Subtotal,
            DeliveryFee = order.DeliveryFee,
            TotalAmount = order.TotalAmount,
            CanAdvance = canAdvance,
            CanCancel = OrderService.CanAdminCancel(order.OrderStatus),
            NextActionLabel = OrderService.GetNextActionLabel(order.OrderStatus, order.FulfillmentMethod)
        };
    }

    private const string StaleStateMessage = "Trạng thái đơn hàng đã thay đổi. Vui lòng kiểm tra trạng thái hiện tại trước khi thao tác lại.";
}
