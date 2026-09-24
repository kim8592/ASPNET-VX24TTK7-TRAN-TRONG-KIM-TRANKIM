using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Cart;

namespace MilkTeaWeb.Controllers;

public sealed class CartController(
    ApplicationDbContext context,
    CartService cartService,
    UserManager<ApplicationUser> userManager) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            TempData["Info"] = "Tài khoản quản trị không sử dụng giỏ hàng mua sắm.";
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var customer = await GetCurrentCustomerAsync();
        if (User.Identity?.IsAuthenticated == true && customer is null)
        {
            return Challenge();
        }

        if (customer is null)
        {
            var guestCart = await cartService.GetGuestCartAsync(cancellationToken);
            return View(ToPageModel(
                guestCart,
                null,
                isCustomer: false,
                canCheckout: guestCart.Items.Count > 0 && !guestCart.HasInvalidItems,
                checkoutUnavailableMessage: guestCart.Items.Count > 0 && guestCart.HasInvalidItems
                    ? "Hãy xử lý các sản phẩm cần kiểm tra lại trước khi đặt hàng."
                    : null));
        }

        // Both reads are deliberately non-mutating: do not create an empty Cart just to render /Cart.
        var checkoutState = await cartService.GetCustomerCheckoutStateAsync(customer, cancellationToken);
        return View(ToPageModel(
            checkoutState.CustomerCart,
            checkoutState.GuestLeftovers,
            isCustomer: true,
            canCheckout: checkoutState.IsReady,
            checkoutUnavailableMessage: GetCheckoutUnavailableMessage(checkoutState)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add([Bind(Prefix = "Input")] AddToCartInputModel input, CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            TempData["Error"] = "Tài khoản quản trị không thể thêm sản phẩm vào giỏ hàng.";
            return RedirectToAction("Index", "Products");
        }

        var isEditing = input.CartItemId.HasValue || !string.IsNullOrWhiteSpace(input.GuestLineKey);
        if ((input.CartItemId.HasValue && !string.IsNullOrWhiteSpace(input.GuestLineKey))
            || !ModelState.IsValid)
        {
            TempData["Error"] = isEditing
                ? "Vui lòng kiểm tra lại tùy chọn sản phẩm."
                : "Vui lòng kiểm tra lại cấu hình và số lượng sản phẩm.";
            return await RedirectAfterAddFailureAsync(
                input.ProductSizeId,
                cancellationToken,
                input.CartItemId,
                input.GuestLineKey);
        }

        var customer = await GetCurrentCustomerAsync();
        if (User.Identity?.IsAuthenticated == true && customer is null)
        {
            return Challenge();
        }

        CartOperationResult result;
        if (isEditing)
        {
            result = !string.IsNullOrWhiteSpace(input.GuestLineKey)
                ? await cartService.UpdateGuestItemConfigurationAsync(input.GuestLineKey, input.ToConfiguration(), cancellationToken)
                : customer is null
                    ? CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.")
                    : await cartService.UpdateCustomerItemConfigurationAsync(customer, input.CartItemId!.Value, input.ToConfiguration(), cancellationToken);
        }
        else
        {
            result = customer is null
                ? await cartService.AddGuestItemAsync(input.ToConfiguration(), cancellationToken)
                : await cartService.AddCustomerItemAsync(customer, input.ToConfiguration(), cancellationToken);
        }

        if (result.Succeeded)
        {
            TempData["Success"] = isEditing
                ? "Đã cập nhật tùy chọn sản phẩm."
                : "Đã thêm sản phẩm vào giỏ hàng.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Error"] = result.ErrorMessage ?? "Không thể thêm sản phẩm vào giỏ hàng.";
        return await RedirectAfterAddFailureAsync(
            input.ProductSizeId,
            cancellationToken,
            input.CartItemId,
            input.GuestLineKey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(UpdateCartQuantityInputModel input, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (User.Identity?.IsAuthenticated == true && customer is null && !User.IsInRole(RoleNames.Admin))
        {
            return Challenge();
        }

        var result = await UpdateQuantityAsync(customer, input, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã cập nhật số lượng."
            : result.ErrorMessage ?? "Không thể cập nhật số lượng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(RemoveCartItemInputModel input, CancellationToken cancellationToken)
    {
        var customer = await GetCurrentCustomerAsync();
        if (User.Identity?.IsAuthenticated == true && customer is null && !User.IsInRole(RoleNames.Admin))
        {
            return Challenge();
        }

        var result = await RemoveAsync(customer, input, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã xóa sản phẩm khỏi giỏ hàng."
            : result.ErrorMessage ?? "Không thể xóa sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            TempData["Error"] = "Tài khoản quản trị không sử dụng giỏ hàng mua sắm.";
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        var customer = await GetCurrentCustomerAsync();
        if (User.Identity?.IsAuthenticated == true && customer is null)
        {
            return Challenge();
        }

        var result = customer is null
            ? await cartService.ClearGuestCartAsync()
            : await cartService.ClearCustomerCartAsync(customer, clearGuestLeftovers: true, cancellationToken: cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã xóa toàn bộ giỏ hàng."
            : result.ErrorMessage ?? "Không thể xóa giỏ hàng.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<CartOperationResult> UpdateQuantityAsync(
        ApplicationUser? customer,
        UpdateCartQuantityInputModel input,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return CartOperationResult.Failure("Tài khoản quản trị không sử dụng giỏ hàng mua sắm.");
        }

        if (!ModelState.IsValid || !HasExactlyOneSource(input.CartItemId, input.GuestLineKey))
        {
            return CartOperationResult.Failure("Yêu cầu cập nhật giỏ hàng không hợp lệ.");
        }

        if (input.GuestLineKey is not null)
        {
            return await cartService.UpdateGuestItemQuantityAsync(input.GuestLineKey, input.Quantity, cancellationToken);
        }

        return customer is null
            ? CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.")
            : await cartService.UpdateCustomerItemQuantityAsync(customer, input.CartItemId!.Value, input.Quantity, cancellationToken);
    }

    private async Task<CartOperationResult> RemoveAsync(
        ApplicationUser? customer,
        RemoveCartItemInputModel input,
        CancellationToken cancellationToken)
    {
        if (User.IsInRole(RoleNames.Admin))
        {
            return CartOperationResult.Failure("Tài khoản quản trị không sử dụng giỏ hàng mua sắm.");
        }

        if (!HasExactlyOneSource(input.CartItemId, input.GuestLineKey))
        {
            return CartOperationResult.Failure("Yêu cầu xóa giỏ hàng không hợp lệ.");
        }

        if (input.GuestLineKey is not null)
        {
            return await cartService.RemoveGuestItemAsync(input.GuestLineKey);
        }

        return customer is null
            ? CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.")
            : await cartService.RemoveCustomerItemAsync(customer, input.CartItemId!.Value, cancellationToken);
    }

    private async Task<ApplicationUser?> GetCurrentCustomerAsync()
    {
        if (User.Identity?.IsAuthenticated != true || User.IsInRole(RoleNames.Admin) || !User.IsInRole(RoleNames.Customer))
        {
            return null;
        }

        return await userManager.GetUserAsync(User);
    }

    private async Task<IActionResult> RedirectAfterAddFailureAsync(
        int productSizeId,
        CancellationToken cancellationToken,
        int? cartItemId = null,
        string? guestLineKey = null)
    {
        var target = await context.ProductSizes.AsNoTracking()
            .Where(size => size.ProductSizeId == productSizeId)
            .Select(size => new
            {
                size.ProductId,
                IsSellable = size.Product.IsAvailable && size.Product.Category.IsActive && size.Product.ProductSizes.Any()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return target is { IsSellable: true }
            ? RedirectToAction("Details", "Products", new { id = target.ProductId, cartItemId, guestLineKey })
            : RedirectToAction("Index", "Products");
    }

    private static bool HasExactlyOneSource(int? cartItemId, string? guestLineKey) =>
        (cartItemId.HasValue && !string.IsNullOrWhiteSpace(guestLineKey)) == false
        && (cartItemId.HasValue || !string.IsNullOrWhiteSpace(guestLineKey));

    private static CartPageViewModel ToPageModel(
        CartReadResult cart,
        CartReadResult? guestLeftovers,
        bool isCustomer,
        bool canCheckout,
        string? checkoutUnavailableMessage) => new()
        {
            IsCustomer = isCustomer,
            Items = cart.Items.Select(ToLine).ToList(),
            GuestLeftovers = guestLeftovers?.Items.Select(ToLine).ToList() ?? [],
            Subtotal = cart.Subtotal + (guestLeftovers?.Subtotal ?? 0),
            CanCheckout = canCheckout,
            CheckoutUnavailableMessage = checkoutUnavailableMessage
        };

    private static string? GetCheckoutUnavailableMessage(CustomerCheckoutStateResult checkoutState)
    {
        if (checkoutState.HasUnresolvedGuestLeftovers)
        {
            return "Một số sản phẩm cần kiểm tra lại trước khi đặt hàng.";
        }

        if (checkoutState.CustomerCart.Items.Count == 0)
        {
            return "Giỏ hàng chưa có sản phẩm hợp lệ để đặt hàng.";
        }

        return checkoutState.CustomerCart.HasInvalidItems
            ? "Hãy xử lý các sản phẩm cần kiểm tra lại trước khi đặt hàng."
            : null;
    }

    private static CartLineViewModel ToLine(CartLineResult line) => new()
    {
        CartItemId = line.CartItemId,
        GuestLineKey = line.GuestCartItemKey,
        ProductId = line.ProductId,
        ImagePath = line.ProductImagePath,
        ProductName = line.ProductName,
        SizeName = line.SizeName,
        SugarPercentage = line.SugarPercentage,
        IcePercentage = line.IcePercentage,
        Toppings = line.Toppings.Select(topping => new CartToppingViewModel { Name = topping.Name, Price = topping.Price }).ToList(),
        Quantity = line.Quantity,
        CurrentUnitPrice = line.CurrentUnitPrice,
        CurrentLineTotal = line.CurrentLineTotal,
        IsValid = line.IsValid,
        ValidationMessages = line.ValidationMessages,
        CanReconfigure = line.CanReconfigure
    };
}
