using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Models.Identity;

namespace MilkTeaWeb.Services;

public sealed class OrderNotificationService(
    ApplicationDbContext context,
    EmailService emailService,
    ILogger<OrderNotificationService> logger)
{
    public async Task<bool> SendOrderPlacedConfirmationAsync(
        ApplicationUser customer,
        int orderId,
        string detailsUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customer.Email) || string.IsNullOrWhiteSpace(detailsUrl))
        {
            logger.LogWarning(
                "Order confirmation email could not be prepared for Order {OrderId} and Customer {CustomerId}.",
                orderId,
                customer.Id);
            return false;
        }

        try
        {
            var order = await context.Orders.AsNoTracking()
                .Where(currentOrder => currentOrder.OrderId == orderId && currentOrder.UserId == customer.Id)
                .Include(currentOrder => currentOrder.OrderItems)
                .ThenInclude(orderItem => orderItem.OrderItemToppings)
                .SingleOrDefaultAsync(cancellationToken);
            if (order is null)
            {
                logger.LogWarning(
                    "Order {OrderId} was not found for Customer {CustomerId} while preparing a confirmation email.",
                    orderId,
                    customer.Id);
                return false;
            }

            await emailService.SendOrderConfirmationEmailAsync(new OrderConfirmationEmail
            {
                RecipientEmail = customer.Email,
                RecipientName = order.RecipientName,
                OrderId = order.OrderId,
                CreatedAt = order.CreatedAt,
                StatusLabel = FormatStatus(order.OrderStatus),
                FulfillmentMethod = order.FulfillmentMethod,
                StoreName = order.StoreName,
                StoreAddress = order.StoreAddress,
                StorePhone = order.StorePhone,
                RecipientPhone = order.RecipientPhone,
                DeliveryAddress = order.DeliveryAddress,
                OrderNote = order.OrderNote,
                PaymentMethod = order.PaymentMethod,
                Items = order.OrderItems
                    .OrderBy(orderItem => orderItem.OrderItemId)
                    .Select(orderItem => new OrderConfirmationEmailItem
                    {
                        ProductName = orderItem.ProductName,
                        SizeName = orderItem.SizeName,
                        SugarPercentage = orderItem.SugarPercentage,
                        IcePercentage = orderItem.IcePercentage,
                        UnitPrice = orderItem.UnitPrice,
                        Quantity = orderItem.Quantity,
                        LineTotal = orderItem.LineTotal,
                        Toppings = orderItem.OrderItemToppings
                            .OrderBy(topping => topping.OrderItemToppingId)
                            .Select(topping => new OrderConfirmationEmailTopping
                            {
                                Name = topping.ToppingName,
                                UnitPrice = topping.UnitPrice
                            })
                            .ToList()
                    })
                    .ToList(),
                Subtotal = order.Subtotal,
                DeliveryFee = order.DeliveryFee,
                TotalAmount = order.TotalAmount,
                DetailsUrl = detailsUrl
            });
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Order confirmation email delivery failed for Order {OrderId} and Customer {CustomerId}.",
                orderId,
                customer.Id);
            return false;
        }
    }

    public async Task<OrderStatusNotificationResult> SendOrderStatusChangedAsync(
        int orderId,
        OrderStatus newStatus,
        string detailsUrl,
        CancellationToken cancellationToken = default)
    {
        if (!IsPotentiallyNotifiedStatus(newStatus))
        {
            return OrderStatusNotificationResult.Skipped;
        }

        if (string.IsNullOrWhiteSpace(detailsUrl))
        {
            logger.LogWarning(
                "Order status email could not be prepared because its details URL is empty for Order {OrderId}.",
                orderId);
            return OrderStatusNotificationResult.Failed;
        }

        try
        {
            var order = await context.Orders.AsNoTracking()
                .Where(currentOrder => currentOrder.OrderId == orderId)
                .Include(currentOrder => currentOrder.User)
                .SingleOrDefaultAsync(cancellationToken);
            if (order is null)
            {
                logger.LogWarning("Order {OrderId} was not found while preparing its status email.", orderId);
                return OrderStatusNotificationResult.Failed;
            }

            if (!ShouldNotifyCustomer(newStatus, order.FulfillmentMethod))
            {
                return OrderStatusNotificationResult.Skipped;
            }

            if (string.IsNullOrWhiteSpace(order.User.Email))
            {
                logger.LogWarning(
                    "Order status email could not be prepared because Customer {CustomerId} has no email for Order {OrderId}.",
                    order.UserId,
                    orderId);
                return OrderStatusNotificationResult.Failed;
            }

            await emailService.SendOrderStatusNotificationEmailAsync(new OrderStatusNotificationEmail
            {
                RecipientEmail = order.User.Email,
                RecipientName = order.RecipientName,
                OrderId = order.OrderId,
                StatusLabel = FormatStatus(newStatus),
                StatusMessage = FormatStatusMessage(newStatus),
                FulfillmentMethod = order.FulfillmentMethod,
                StoreName = order.StoreName,
                StoreAddress = order.StoreAddress,
                StorePhone = order.StorePhone,
                DeliveryAddress = order.DeliveryAddress,
                DetailsUrl = detailsUrl
            });
            return OrderStatusNotificationResult.Sent;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Order status email delivery failed for Order {OrderId} and status {OrderStatus}.",
                orderId,
                newStatus);
            return OrderStatusNotificationResult.Failed;
        }
    }

    private static bool ShouldNotifyCustomer(OrderStatus status, FulfillmentMethod fulfillmentMethod) => status switch
    {
        OrderStatus.Confirmed => true,
        OrderStatus.Ready => fulfillmentMethod == FulfillmentMethod.Pickup,
        OrderStatus.Delivering => fulfillmentMethod == FulfillmentMethod.Delivery,
        OrderStatus.Cancelled => true,
        _ => false
    };

    private static bool IsPotentiallyNotifiedStatus(OrderStatus status) => status is
        OrderStatus.Confirmed or OrderStatus.Ready or OrderStatus.Delivering or OrderStatus.Cancelled;

    private static string FormatStatusMessage(OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => "Cửa hàng đã tiếp nhận và xác nhận đơn hàng của bạn.",
        OrderStatus.Ready => "Món đã sẵn sàng. Bạn vui lòng đến cửa hàng để nhận món.",
        OrderStatus.Delivering => "Đơn hàng đang được giao đến bạn.",
        OrderStatus.Cancelled => "Đơn hàng đã được hủy. Nếu cần hỗ trợ, bạn có thể liên hệ cửa hàng.",
        _ => "Đơn hàng của bạn vừa có cập nhật mới."
    };

    private static string FormatStatus(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Chờ xác nhận",
        OrderStatus.Confirmed => "Đã xác nhận",
        OrderStatus.Preparing => "Đang pha chế",
        OrderStatus.Ready => "Sẵn sàng nhận món",
        OrderStatus.Delivering => "Đang giao hàng",
        OrderStatus.Completed => "Hoàn thành",
        OrderStatus.Cancelled => "Đã hủy",
        _ => "Không xác định"
    };
}

public enum OrderStatusNotificationResult
{
    Skipped,
    Sent,
    Failed
}
