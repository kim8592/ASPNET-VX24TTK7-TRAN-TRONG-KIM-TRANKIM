using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Enums;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Options;
using MilkTeaWeb.Security;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Cart;
using MilkTeaWeb.ViewModels.Checkout;

namespace MilkTeaWeb.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class CheckoutController(
    ApplicationDbContext context,
    CartService cartService,
    OrderService orderService,
    OrderNotificationService orderNotificationService,
    UserManager<ApplicationUser> userManager,
    IOptions<CheckoutOptions> checkoutOptions,
    ILogger<CheckoutController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        if (!TryGetFixedDeliveryFee(out var fixedDeliveryFee))
        {
            return RedirectToCartWithConfigurationError();
        }

        var checkoutState = await cartService.GetCustomerCheckoutStateAsync(customer, cancellationToken);
        if (!checkoutState.IsReady)
        {
            return RedirectToCartForReadiness(checkoutState);
        }

        var stores = await GetActiveStoresAsync(cancellationToken);
        if (stores.Count == 0)
        {
            TempData["Error"] = "Hiện chưa có cửa hàng hoạt động để tiếp nhận đơn hàng.";
            return RedirectToAction("Index", "Cart");
        }

        // M10 intentionally does not keep a checkout draft. Returning to GET /Checkout
        // starts a new form from the Customer profile and current cart data.
        var input = new CheckoutInputModel
        {
            FulfillmentMethod = FulfillmentMethod.Pickup,
            RecipientName = customer.FullName,
            RecipientPhone = customer.PhoneNumber ?? string.Empty,
            DeliveryAddress = customer.DefaultDeliveryAddress
        };
        return View(BuildCheckoutViewModel(input, stores, checkoutState.CustomerCart, fixedDeliveryFee));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(CheckoutInputModel input, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        if (!TryGetFixedDeliveryFee(out var fixedDeliveryFee))
        {
            return RedirectToCartWithConfigurationError();
        }

        var checkoutState = await cartService.GetCustomerCheckoutStateAsync(customer, cancellationToken);
        if (!checkoutState.IsReady)
        {
            return RedirectToCartForReadiness(checkoutState);
        }

        var stores = await GetActiveStoresAsync(cancellationToken);
        if (stores.Count == 0)
        {
            TempData["Error"] = "Hiện chưa có cửa hàng hoạt động để tiếp nhận đơn hàng.";
            return RedirectToAction("Index", "Cart");
        }

        CheckoutInputRules.Normalize(input);
        ValidateInput(input, stores);
        if (!ModelState.IsValid)
        {
            return View("Index", BuildCheckoutViewModel(input, stores, checkoutState.CustomerCart, fixedDeliveryFee));
        }

        var store = stores.Single(storeOption => storeOption.StoreId == input.StoreId);
        var fulfillmentMethod = input.FulfillmentMethod!.Value;
        var deliveryFee = fulfillmentMethod == FulfillmentMethod.Delivery ? fixedDeliveryFee : 0m;
        return View(new CheckoutReviewViewModel
        {
            Items = checkoutState.CustomerCart.Items.Select(ToCheckoutLine).ToList(),
            Store = store,
            FulfillmentMethod = fulfillmentMethod,
            RecipientName = input.RecipientName,
            RecipientPhone = input.RecipientPhone,
            DeliveryAddress = fulfillmentMethod == FulfillmentMethod.Delivery ? input.DeliveryAddress : null,
            OrderNote = input.OrderNote,
            PaymentMethod = fulfillmentMethod == FulfillmentMethod.Pickup
                ? PaymentMethod.PayAtStore
                : PaymentMethod.CashOnDelivery,
            Subtotal = checkoutState.CustomerCart.Subtotal,
            DeliveryFee = deliveryFee,
            Total = checkoutState.CustomerCart.Subtotal + deliveryFee
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutInputModel input, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (customer is null)
        {
            return Forbid();
        }

        var result = await orderService.PlaceOrderAsync(customer, input, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Đơn hàng đã được đặt thành công.";
            var detailsUrl = Url.ActionLink(
                nameof(OrdersController.Details),
                "Orders",
                new { id = result.OrderId },
                protocol: Request.Scheme) ?? string.Empty;
            var emailSent = await orderNotificationService.SendOrderPlacedConfirmationAsync(
                customer,
                result.OrderId!.Value,
                detailsUrl,
                cancellationToken);
            if (!emailSent)
            {
                TempData["Warning"] = "Đơn hàng đã được tạo nhưng email xác nhận chưa gửi được. Bạn vẫn có thể xem đơn hàng tại đây.";
            }

            return RedirectToAction("Details", "Orders", new { id = result.OrderId });
        }

        switch (result.FailureKind)
        {
            case PlaceOrderFailureKind.CartNotReady:
                return RedirectToCartForReadiness(result.CheckoutState ?? new CustomerCheckoutStateResult());
            case PlaceOrderFailureKind.StoreUnavailable:
                TempData["Error"] = "Cửa hàng đã chọn không còn hoạt động. Vui lòng chọn cửa hàng khác.";
                return RedirectToAction(nameof(Index));
            case PlaceOrderFailureKind.InvalidInput:
                TempData["Error"] = "Thông tin nhận hàng không hợp lệ. Vui lòng kiểm tra và thử lại.";
                return RedirectToAction(nameof(Index));
            case PlaceOrderFailureKind.ConfigurationFailure:
                return RedirectToCartWithConfigurationError();
            default:
                TempData["Error"] = "Không thể đặt hàng lúc này. Giỏ hàng của bạn chưa bị thay đổi.";
                return RedirectToAction("Index", "Cart");
        }
    }

    private async Task<ApplicationUser?> GetCurrentCustomerAsync()
    {
        var customer = await userManager.GetUserAsync(User);
        if (customer is null)
        {
            return null;
        }

        // The normal role invariant is one role per account. Check both roles here so
        // a malformed dual-role account cannot enter the Customer checkout flow.
        var isCustomer = await userManager.IsInRoleAsync(customer, RoleNames.Customer);
        var isAdmin = await userManager.IsInRoleAsync(customer, RoleNames.Admin);
        return isCustomer && !isAdmin ? customer : null;
    }

    private async Task<List<CheckoutStoreOptionViewModel>> GetActiveStoresAsync(CancellationToken cancellationToken) =>
        await context.Stores.AsNoTracking()
            .Where(store => store.IsActive)
            .OrderBy(store => store.Name)
            .Select(store => new CheckoutStoreOptionViewModel
            {
                StoreId = store.StoreId,
                Name = store.Name,
                Address = store.Address,
                Phone = store.Phone
            })
            .ToListAsync(cancellationToken);

    private static CheckoutViewModel BuildCheckoutViewModel(
        CheckoutInputModel input,
        IReadOnlyList<CheckoutStoreOptionViewModel> stores,
        CartReadResult cart,
        decimal fixedDeliveryFee) => new()
        {
            Input = input,
            Stores = stores,
            Items = cart.Items.Select(ToCheckoutLine).ToList(),
            Subtotal = cart.Subtotal,
            DeliveryFee = fixedDeliveryFee
        };

    private static CheckoutCartLineViewModel ToCheckoutLine(CartLineResult line) => new()
    {
        ImagePath = line.ProductImagePath,
        ProductName = line.ProductName,
        SizeName = line.SizeName,
        SugarPercentage = line.SugarPercentage,
        IcePercentage = line.IcePercentage,
        Toppings = line.Toppings.Select(topping => new CheckoutToppingViewModel
        {
            Name = topping.Name,
            Price = topping.Price
        }).ToList(),
        Quantity = line.Quantity,
        CurrentUnitPrice = line.CurrentUnitPrice,
        CurrentLineTotal = line.CurrentLineTotal
    };

    private void ValidateInput(CheckoutInputModel input, IReadOnlyCollection<CheckoutStoreOptionViewModel> stores)
    {
        ModelState.Clear();
        foreach (var error in CheckoutInputRules.Validate(input))
        {
            var modelStateKey = string.IsNullOrEmpty(error.MemberName)
                ? string.Empty
                : $"Input.{error.MemberName}";
            ModelState.AddModelError(modelStateKey, error.ErrorMessage);
        }

        if (!stores.Any(store => store.StoreId == input.StoreId))
        {
            ModelState.AddModelError("Input.StoreId", "Cửa hàng đã chọn không còn hoạt động.");
        }

    }

    private bool TryGetFixedDeliveryFee(out decimal fixedDeliveryFee)
    {
        fixedDeliveryFee = checkoutOptions.Value.FixedDeliveryFee;
        if (fixedDeliveryFee > 0m)
        {
            return true;
        }

        logger.LogError("Checkout delivery fee is invalid: {DeliveryFee}.", fixedDeliveryFee);
        return false;
    }

    private IActionResult RedirectToCartWithConfigurationError()
    {
        TempData["Error"] = "Không thể khởi tạo thanh toán vào lúc này. Vui lòng thử lại sau.";
        return RedirectToAction("Index", "Cart");
    }

    private IActionResult RedirectToCartForReadiness(CustomerCheckoutStateResult checkoutState)
    {
        TempData["Error"] = checkoutState.HasUnresolvedGuestLeftovers
            ? "Một số sản phẩm cần được kiểm tra lại trước khi đặt hàng."
            : checkoutState.CustomerCart.Items.Count == 0
                ? "Giỏ hàng đang trống. Hãy chọn sản phẩm trước khi đặt hàng."
                : "Giỏ hàng đã thay đổi. Hãy xử lý các sản phẩm cần kiểm tra lại trước khi đặt hàng.";
        return RedirectToAction("Index", "Cart");
    }
}
