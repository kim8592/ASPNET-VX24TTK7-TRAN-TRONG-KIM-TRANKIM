using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Options;
using MilkTeaWeb.ViewModels.Cart;
using MilkTeaWeb.ViewModels.Checkout;

namespace MilkTeaWeb.Services;

public sealed class OrderService(
    ApplicationDbContext context,
    CartService cartService,
    IOptions<CheckoutOptions> checkoutOptions,
    ILogger<OrderService> logger)
{
    public async Task<PlaceOrderResult> PlaceOrderAsync(
        ApplicationUser customer,
        CheckoutInputModel input,
        CancellationToken cancellationToken = default)
    {
        CheckoutInputRules.Normalize(input);
        var inputErrors = CheckoutInputRules.Validate(input);
        if (inputErrors.Count > 0)
        {
            return PlaceOrderResult.InvalidInput(inputErrors);
        }

        var fixedDeliveryFee = checkoutOptions.Value.FixedDeliveryFee;
        if (fixedDeliveryFee <= 0m)
        {
            logger.LogError("Checkout delivery fee is invalid while placing an order: {DeliveryFee}.", fixedDeliveryFee);
            return PlaceOrderResult.ConfigurationFailure();
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            // CartService is the shared authority for configuration validity and current catalog prices.
            // It uses the same scoped DbContext, so these reads participate in this transaction.
            var checkoutState = await cartService.GetCustomerCheckoutStateAsync(customer, cancellationToken);
            if (!checkoutState.IsReady)
            {
                return PlaceOrderResult.CartNotReady(checkoutState);
            }

            var store = await context.Stores.SingleOrDefaultAsync(
                currentStore => currentStore.StoreId == input.StoreId && currentStore.IsActive,
                cancellationToken);
            if (store is null)
            {
                return PlaceOrderResult.StoreUnavailable();
            }

            CheckoutInputRules.ApplyFulfillmentRules(input);
            var fulfillmentMethod = input.FulfillmentMethod!.Value;
            var deliveryFee = fulfillmentMethod == FulfillmentMethod.Delivery ? fixedDeliveryFee : 0m;
            var paymentMethod = fulfillmentMethod == FulfillmentMethod.Pickup
                ? PaymentMethod.PayAtStore
                : PaymentMethod.CashOnDelivery;

            var cart = await context.Carts
                .Include(currentCart => currentCart.CartItems)
                .SingleOrDefaultAsync(currentCart => currentCart.UserId == customer.Id, cancellationToken);
            if (cart is null || cart.CartItems.Count != checkoutState.CustomerCart.Items.Count)
            {
                return PlaceOrderResult.CartNotReady(checkoutState);
            }

            var orderItems = checkoutState.CustomerCart.Items
                .OrderBy(item => item.CartItemId)
                .Select(item => new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    SizeName = item.SizeName,
                    SugarPercentage = item.SugarPercentage,
                    IcePercentage = item.IcePercentage,
                    UnitPrice = item.ProductSizeUnitPrice,
                    Quantity = item.Quantity,
                    LineTotal = item.CurrentLineTotal,
                    OrderItemToppings = item.Toppings
                        .OrderBy(topping => topping.ToppingId)
                        .Select(topping => new OrderItemTopping
                        {
                            ToppingId = topping.ToppingId,
                            ToppingName = topping.Name,
                            UnitPrice = topping.Price
                        })
                        .ToList()
                })
                .ToList();
            var subtotal = orderItems.Sum(item => item.LineTotal);
            var now = DateTime.UtcNow;
            var order = new Order
            {
                UserId = customer.Id,
                StoreId = store.StoreId,
                StoreName = store.Name,
                StoreAddress = store.Address,
                StorePhone = store.Phone,
                FulfillmentMethod = fulfillmentMethod,
                RecipientName = input.RecipientName,
                RecipientPhone = input.RecipientPhone,
                DeliveryAddress = input.DeliveryAddress,
                OrderNote = input.OrderNote,
                Subtotal = subtotal,
                DeliveryFee = deliveryFee,
                TotalAmount = subtotal + deliveryFee,
                OrderStatus = OrderStatus.Pending,
                PaymentMethod = paymentMethod,
                CreatedAt = now,
                OrderItems = orderItems
            };

            context.Orders.Add(order);
            context.CartItems.RemoveRange(cart.CartItems);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PlaceOrderResult.Success(order.OrderId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Place Order transaction failed for Customer {CustomerId}.", customer.Id);

            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogWarning(
                    rollbackException,
                    "Place Order rollback could not complete for Customer {CustomerId}.",
                    customer.Id);
            }

            return PlaceOrderResult.PersistenceFailure();
        }
    }

    public async Task<CancelOrderResult> CancelPendingOrderAsync(
        ApplicationUser customer,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var isOwned = await context.Orders.AsNoTracking()
            .AnyAsync(order => order.OrderId == orderId && order.UserId == customer.Id, cancellationToken);
        if (!isOwned)
        {
            return CancelOrderResult.NotFound();
        }

        try
        {
            var changed = await context.Orders
                .Where(order => order.OrderId == orderId
                    && order.UserId == customer.Id
                    && order.OrderStatus == OrderStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.OrderStatus, OrderStatus.Cancelled)
                    .SetProperty(order => order.UpdatedAt, DateTime.UtcNow), cancellationToken);
            return changed == 1 ? CancelOrderResult.Success() : CancelOrderResult.NotPending();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Customer Order cancellation failed for Order {OrderId}.", orderId);
            return CancelOrderResult.PersistenceFailure();
        }
    }

    public async Task<AdminOrderStatusMutationResult> AdvanceOrderStatusAsync(
        int orderId,
        OrderStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        var currentOrder = await GetOrderLifecycleStateAsync(orderId, cancellationToken);
        if (currentOrder is null)
        {
            return AdminOrderStatusMutationResult.NotFound();
        }

        if (!Enum.IsDefined(typeof(OrderStatus), expectedStatus)
            || currentOrder.OrderStatus != expectedStatus)
        {
            return AdminOrderStatusMutationResult.Stale();
        }

        if (!TryGetNextStatus(currentOrder.OrderStatus, currentOrder.FulfillmentMethod, out var nextStatus))
        {
            return AdminOrderStatusMutationResult.InvalidTransition();
        }

        try
        {
            var changed = await context.Orders
                .Where(order => order.OrderId == orderId && order.OrderStatus == expectedStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.OrderStatus, nextStatus)
                    .SetProperty(order => order.UpdatedAt, DateTime.UtcNow), cancellationToken);
            return changed == 1
                ? AdminOrderStatusMutationResult.Success(nextStatus)
                : AdminOrderStatusMutationResult.Stale();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin Order status advancement failed for Order {OrderId}.", orderId);
            return AdminOrderStatusMutationResult.PersistenceFailure();
        }
    }

    public async Task<AdminOrderStatusMutationResult> CancelOrderByAdminAsync(
        int orderId,
        OrderStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        var currentOrder = await GetOrderLifecycleStateAsync(orderId, cancellationToken);
        if (currentOrder is null)
        {
            return AdminOrderStatusMutationResult.NotFound();
        }

        if (!Enum.IsDefined(typeof(OrderStatus), expectedStatus)
            || currentOrder.OrderStatus != expectedStatus)
        {
            return AdminOrderStatusMutationResult.Stale();
        }

        if (!CanAdminCancel(currentOrder.OrderStatus))
        {
            return AdminOrderStatusMutationResult.InvalidTransition();
        }

        try
        {
            var changed = await context.Orders
                .Where(order => order.OrderId == orderId && order.OrderStatus == expectedStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.OrderStatus, OrderStatus.Cancelled)
                    .SetProperty(order => order.UpdatedAt, DateTime.UtcNow), cancellationToken);
            return changed == 1
                ? AdminOrderStatusMutationResult.Success(OrderStatus.Cancelled)
                : AdminOrderStatusMutationResult.Stale();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Admin Order cancellation failed for Order {OrderId}.", orderId);
            return AdminOrderStatusMutationResult.PersistenceFailure();
        }
    }

    public static bool TryGetNextStatus(
        OrderStatus currentStatus,
        FulfillmentMethod fulfillmentMethod,
        out OrderStatus nextStatus)
    {
        nextStatus = currentStatus switch
        {
            OrderStatus.Pending => OrderStatus.Confirmed,
            OrderStatus.Confirmed => OrderStatus.Preparing,
            OrderStatus.Preparing => OrderStatus.Ready,
            OrderStatus.Ready when fulfillmentMethod == FulfillmentMethod.Delivery => OrderStatus.Delivering,
            OrderStatus.Ready when fulfillmentMethod == FulfillmentMethod.Pickup => OrderStatus.Completed,
            OrderStatus.Delivering when fulfillmentMethod == FulfillmentMethod.Delivery => OrderStatus.Completed,
            _ => currentStatus
        };

        return nextStatus != currentStatus;
    }

    public static bool CanAdminCancel(OrderStatus orderStatus) => orderStatus is not OrderStatus.Completed and not OrderStatus.Cancelled;

    public static string? GetNextActionLabel(OrderStatus currentStatus, FulfillmentMethod fulfillmentMethod) =>
        TryGetNextStatus(currentStatus, fulfillmentMethod, out var nextStatus)
            ? nextStatus switch
            {
                OrderStatus.Confirmed => "Xác nhận đơn",
                OrderStatus.Preparing => "Bắt đầu pha chế",
                OrderStatus.Ready => "Đánh dấu sẵn sàng",
                OrderStatus.Delivering => "Bắt đầu giao hàng",
                OrderStatus.Completed => "Hoàn thành đơn",
                _ => null
            }
            : null;

    private async Task<OrderLifecycleState?> GetOrderLifecycleStateAsync(int orderId, CancellationToken cancellationToken) =>
        await context.Orders.AsNoTracking()
            .Where(order => order.OrderId == orderId)
            .Select(order => new OrderLifecycleState(order.OrderId, order.OrderStatus, order.FulfillmentMethod))
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record OrderLifecycleState(
        int OrderId,
        OrderStatus OrderStatus,
        FulfillmentMethod FulfillmentMethod);
}

public sealed class PlaceOrderResult
{
    public int? OrderId { get; init; }

    public PlaceOrderFailureKind? FailureKind { get; init; }

    public IReadOnlyList<CheckoutInputValidationError> InputErrors { get; init; } = [];

    public CustomerCheckoutStateResult? CheckoutState { get; init; }

    public bool Succeeded => OrderId.HasValue;

    public static PlaceOrderResult Success(int orderId) => new() { OrderId = orderId };

    public static PlaceOrderResult InvalidInput(IReadOnlyList<CheckoutInputValidationError> errors) => new()
    {
        FailureKind = PlaceOrderFailureKind.InvalidInput,
        InputErrors = errors
    };

    public static PlaceOrderResult CartNotReady(CustomerCheckoutStateResult checkoutState) => new()
    {
        FailureKind = PlaceOrderFailureKind.CartNotReady,
        CheckoutState = checkoutState
    };

    public static PlaceOrderResult StoreUnavailable() => new() { FailureKind = PlaceOrderFailureKind.StoreUnavailable };

    public static PlaceOrderResult ConfigurationFailure() => new() { FailureKind = PlaceOrderFailureKind.ConfigurationFailure };

    public static PlaceOrderResult PersistenceFailure() => new() { FailureKind = PlaceOrderFailureKind.PersistenceFailure };
}

public enum PlaceOrderFailureKind
{
    InvalidInput,
    CartNotReady,
    StoreUnavailable,
    ConfigurationFailure,
    PersistenceFailure
}

public sealed class CancelOrderResult
{
    public CancelOrderResultKind Kind { get; init; }

    public static CancelOrderResult Success() => new() { Kind = CancelOrderResultKind.Succeeded };

    public static CancelOrderResult NotFound() => new() { Kind = CancelOrderResultKind.NotFound };

    public static CancelOrderResult NotPending() => new() { Kind = CancelOrderResultKind.NotPending };

    public static CancelOrderResult PersistenceFailure() => new() { Kind = CancelOrderResultKind.PersistenceFailure };
}

public enum CancelOrderResultKind
{
    Succeeded,
    NotFound,
    NotPending,
    PersistenceFailure
}

public sealed class AdminOrderStatusMutationResult
{
    public AdminOrderStatusMutationKind Kind { get; init; }

    public OrderStatus? NewStatus { get; init; }

    public static AdminOrderStatusMutationResult Success(OrderStatus newStatus) => new()
    {
        Kind = AdminOrderStatusMutationKind.Succeeded,
        NewStatus = newStatus
    };

    public static AdminOrderStatusMutationResult NotFound() => new() { Kind = AdminOrderStatusMutationKind.NotFound };

    public static AdminOrderStatusMutationResult Stale() => new() { Kind = AdminOrderStatusMutationKind.Stale };

    public static AdminOrderStatusMutationResult InvalidTransition() => new() { Kind = AdminOrderStatusMutationKind.InvalidTransition };

    public static AdminOrderStatusMutationResult PersistenceFailure() => new() { Kind = AdminOrderStatusMutationKind.PersistenceFailure };
}

public enum AdminOrderStatusMutationKind
{
    Succeeded,
    NotFound,
    Stale,
    InvalidTransition,
    PersistenceFailure
}
