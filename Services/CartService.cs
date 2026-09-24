using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Cart;

namespace MilkTeaWeb.Services;

public sealed class CartService(
    ApplicationDbContext context,
    GuestCartSessionStore guestCartSessionStore,
    UserManager<ApplicationUser> userManager,
    ILogger<CartService> logger)
{
    public int GetGuestCartBadgeCount() =>
        guestCartSessionStore.Get().Items.Sum(item => item.Quantity);

    public async Task<int> GetCustomerCartBadgeCountAsync(
        ApplicationUser customer,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return 0;
        }

        // This is intentionally a read-only query. A Customer Cart remains lazy-created
        // by the first valid persistent mutation in AddCustomerItemAsync.
        var customerQuantity = await context.CartItems.AsNoTracking()
            .Where(item => item.Cart.UserId == customer.Id)
            .Select(item => (int?)item.Quantity)
            .SumAsync(cancellationToken) ?? 0;

        return customerQuantity + GetGuestCartBadgeCount();
    }

    public async Task<CartReadResult> GetGuestCartAsync(CancellationToken cancellationToken = default)
    {
        var guestCart = guestCartSessionStore.Get();
        var sources = guestCart.Items.Select(item => new CartLineSource(
            null,
            item.GuestCartItemKey,
            ToInput(item))).ToList();
        return await BuildReadResultAsync(sources, true, cancellationToken);
    }

    public async Task<CartReadResult> GetCustomerCartAsync(
        ApplicationUser customer,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return new CartReadResult();
        }

        var cart = await context.Carts.AsNoTracking()
            .Include(currentCart => currentCart.CartItems)
            .ThenInclude(item => item.CartItemToppings)
            .SingleOrDefaultAsync(currentCart => currentCart.UserId == customer.Id, cancellationToken);
        if (cart is null)
        {
            return new CartReadResult();
        }

        var sources = cart.CartItems.Select(item => new CartLineSource(
            item.CartItemId,
            null,
            new CartConfigurationInputModel
            {
                ProductSizeId = item.ProductSizeId,
                SugarLevelId = item.SugarLevelId,
                IceLevelId = item.IceLevelId,
                ToppingIds = item.CartItemToppings.Select(topping => topping.ToppingId).ToList(),
                Quantity = item.Quantity
            })).ToList();
        return await BuildReadResultAsync(sources, false, cancellationToken);
    }

    public async Task<CustomerCheckoutStateResult> GetCustomerCheckoutStateAsync(
        ApplicationUser customer,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return new CustomerCheckoutStateResult();
        }

        var customerCart = await GetCustomerCartAsync(customer, cancellationToken);
        var guestLeftovers = await GetGuestCartAsync(cancellationToken);
        return new CustomerCheckoutStateResult
        {
            CustomerCart = customerCart,
            GuestLeftovers = guestLeftovers
        };
    }

    public async Task<CartEditItemResult?> GetGuestItemForEditAsync(
        string guestCartItemKey,
        CancellationToken cancellationToken = default)
    {
        var item = guestCartSessionStore.Get().Items
            .SingleOrDefault(currentItem => currentItem.GuestCartItemKey == guestCartItemKey);
        if (item is null)
        {
            return null;
        }

        var productId = await context.ProductSizes.AsNoTracking()
            .Where(productSize => productSize.ProductSizeId == item.ProductSizeId)
            .Select(productSize => (int?)productSize.ProductId)
            .SingleOrDefaultAsync(cancellationToken);
        return productId is null
            ? null
            : new CartEditItemResult
            {
                ProductId = productId.Value,
                GuestCartItemKey = item.GuestCartItemKey,
                Configuration = ToInput(item)
            };
    }

    public async Task<CartEditItemResult?> GetCustomerItemForEditAsync(
        ApplicationUser customer,
        int cartItemId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return null;
        }

        var item = await context.CartItems.AsNoTracking()
            .Include(cartItem => cartItem.Cart)
            .Include(cartItem => cartItem.CartItemToppings)
            .Include(cartItem => cartItem.ProductSize)
            .SingleOrDefaultAsync(
                cartItem => cartItem.CartItemId == cartItemId && cartItem.Cart.UserId == customer.Id,
                cancellationToken);
        return item is null
            ? null
            : new CartEditItemResult
            {
                ProductId = item.ProductSize.ProductId,
                CartItemId = item.CartItemId,
                Configuration = ToInput(item)
            };
    }

    public async Task<CartOperationResult> AddGuestItemAsync(
        CartConfigurationInputModel input,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(input);
        var evaluation = await EvaluateAsync(normalized, cancellationToken);
        if (!evaluation.IsValid)
        {
            return CartOperationResult.Failure(FirstMessage(evaluation));
        }

        var guestCart = guestCartSessionStore.Get();
        var matchingItem = guestCart.Items.FirstOrDefault(item => SameConfiguration(ToInput(item), normalized));
        if (matchingItem is null)
        {
            guestCart.Items.Add(new GuestCartItemSessionModel
            {
                ProductSizeId = normalized.ProductSizeId,
                SugarLevelId = normalized.SugarLevelId,
                IceLevelId = normalized.IceLevelId,
                ToppingIds = normalized.ToppingIds,
                Quantity = normalized.Quantity
            });
        }
        else if (!TryIncreaseQuantity(matchingItem.Quantity, normalized.Quantity, out var quantity))
        {
            return CartOperationResult.Failure("Số lượng sản phẩm vượt quá giới hạn hệ thống.");
        }
        else
        {
            matchingItem.Quantity = quantity;
        }

        guestCartSessionStore.Save(guestCart);
        return CartOperationResult.Success();
    }

    public async Task<CartOperationResult> AddCustomerItemAsync(
        ApplicationUser customer,
        CartConfigurationInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return CartOperationResult.Failure("Tài khoản này không thể sử dụng giỏ hàng.");
        }

        var normalized = Normalize(input);
        var evaluation = await EvaluateAsync(normalized, cancellationToken);
        if (!evaluation.IsValid)
        {
            return CartOperationResult.Failure(FirstMessage(evaluation));
        }

        var cart = await GetTrackedCartAsync(customer.Id, cancellationToken);
        if (cart is null)
        {
            cart = new Cart { UserId = customer.Id };
            context.Carts.Add(cart);
        }

        if (!TryAddToCart(cart, normalized, out var errorMessage))
        {
            return CartOperationResult.Failure(errorMessage!);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CartOperationResult.Success();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Customer cart add failed.");
            return CartOperationResult.Failure("Không thể cập nhật giỏ hàng lúc này.");
        }
    }

    public async Task<CartOperationResult> UpdateGuestItemConfigurationAsync(
        string guestCartItemKey,
        CartConfigurationInputModel input,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(input);
        var evaluation = await EvaluateAsync(normalized, cancellationToken);
        if (!evaluation.IsValid)
        {
            return CartOperationResult.Failure(FirstMessage(evaluation));
        }

        var guestCart = guestCartSessionStore.Get();
        var item = guestCart.Items.SingleOrDefault(currentItem => currentItem.GuestCartItemKey == guestCartItemKey);
        if (item is null)
        {
            return CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.");
        }

        var matchingItem = guestCart.Items.FirstOrDefault(currentItem =>
            currentItem.GuestCartItemKey != guestCartItemKey
            && SameConfiguration(ToInput(currentItem), normalized));
        if (matchingItem is not null)
        {
            if (!TryIncreaseQuantity(matchingItem.Quantity, normalized.Quantity, out var quantity))
            {
                return CartOperationResult.Failure("Số lượng sản phẩm vượt quá giới hạn hệ thống.");
            }

            guestCart.Items.Remove(item);
            matchingItem.Quantity = quantity;
        }
        else
        {
            item.ProductSizeId = normalized.ProductSizeId;
            item.SugarLevelId = normalized.SugarLevelId;
            item.IceLevelId = normalized.IceLevelId;
            item.ToppingIds = normalized.ToppingIds;
            item.Quantity = normalized.Quantity;
        }

        guestCartSessionStore.Save(guestCart);
        return CartOperationResult.Success();
    }

    public async Task<CartOperationResult> UpdateCustomerItemConfigurationAsync(
        ApplicationUser customer,
        int cartItemId,
        CartConfigurationInputModel input,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return CartOperationResult.Failure("Tài khoản này không thể sử dụng giỏ hàng.");
        }

        var normalized = Normalize(input);
        var evaluation = await EvaluateAsync(normalized, cancellationToken);
        if (!evaluation.IsValid)
        {
            return CartOperationResult.Failure(FirstMessage(evaluation));
        }

        var cart = await GetTrackedCartAsync(customer.Id, cancellationToken);
        var item = cart?.CartItems.SingleOrDefault(currentItem => currentItem.CartItemId == cartItemId);
        if (item is null)
        {
            return CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.");
        }

        var matchingItem = cart!.CartItems.FirstOrDefault(currentItem =>
            currentItem.CartItemId != cartItemId
            && SameConfiguration(ToInput(currentItem), normalized));
        if (matchingItem is not null)
        {
            if (!TryIncreaseQuantity(matchingItem.Quantity, normalized.Quantity, out var quantity))
            {
                return CartOperationResult.Failure("Số lượng sản phẩm vượt quá giới hạn hệ thống.");
            }

            matchingItem.Quantity = quantity;
            context.CartItems.Remove(item);
        }
        else
        {
            ApplyConfiguration(item, normalized);
        }

        return await SaveCustomerMutationAsync(cancellationToken);
    }

    public async Task<CartOperationResult> UpdateGuestItemQuantityAsync(
        string guestCartItemKey,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (quantity <= 0)
        {
            return CartOperationResult.Failure("Số lượng phải lớn hơn 0.");
        }

        var guestCart = guestCartSessionStore.Get();
        var item = guestCart.Items.SingleOrDefault(currentItem => currentItem.GuestCartItemKey == guestCartItemKey);
        if (item is null)
        {
            return CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.");
        }

        item.Quantity = quantity;
        guestCartSessionStore.Save(guestCart);
        return CartOperationResult.Success();
    }

    public async Task<CartOperationResult> UpdateCustomerItemQuantityAsync(
        ApplicationUser customer,
        int cartItemId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return CartOperationResult.Failure("Tài khoản này không thể sử dụng giỏ hàng.");
        }

        if (quantity <= 0)
        {
            return CartOperationResult.Failure("Số lượng phải lớn hơn 0.");
        }

        var item = await context.CartItems
            .Include(cartItem => cartItem.Cart)
            .SingleOrDefaultAsync(cartItem => cartItem.CartItemId == cartItemId && cartItem.Cart.UserId == customer.Id, cancellationToken);
        if (item is null)
        {
            return CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.");
        }

        item.Quantity = quantity;
        return await SaveCustomerMutationAsync(cancellationToken);
    }

    public async Task<CartOperationResult> RemoveGuestItemAsync(string guestCartItemKey)
    {
        var guestCart = guestCartSessionStore.Get();
        var removed = guestCart.Items.RemoveAll(item => item.GuestCartItemKey == guestCartItemKey);
        if (removed == 0)
        {
            return CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.");
        }

        guestCartSessionStore.Save(guestCart);
        return CartOperationResult.Success();
    }

    public async Task<CartOperationResult> RemoveCustomerItemAsync(
        ApplicationUser customer,
        int cartItemId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return CartOperationResult.Failure("Tài khoản này không thể sử dụng giỏ hàng.");
        }

        var item = await context.CartItems
            .Include(cartItem => cartItem.Cart)
            .SingleOrDefaultAsync(cartItem => cartItem.CartItemId == cartItemId && cartItem.Cart.UserId == customer.Id, cancellationToken);
        if (item is null)
        {
            return CartOperationResult.Failure("Không tìm thấy mục trong giỏ hàng.");
        }

        context.CartItems.Remove(item);
        return await SaveCustomerMutationAsync(cancellationToken);
    }

    public async Task<CartOperationResult> ClearGuestCartAsync()
    {
        await Task.CompletedTask;
        guestCartSessionStore.Clear();
        return CartOperationResult.Success();
    }

    public async Task<CartOperationResult> ClearCustomerCartAsync(
        ApplicationUser customer,
        bool clearGuestLeftovers,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return CartOperationResult.Failure("Tài khoản này không thể sử dụng giỏ hàng.");
        }

        var cart = await GetTrackedCartAsync(customer.Id, cancellationToken);
        if (cart is not null)
        {
            context.CartItems.RemoveRange(cart.CartItems);
            var result = await SaveCustomerMutationAsync(cancellationToken);
            if (!result.Succeeded)
            {
                return result;
            }
        }

        if (clearGuestLeftovers)
        {
            guestCartSessionStore.Clear();
        }

        return CartOperationResult.Success();
    }

    public async Task<CartMergeResult> MergeGuestCartIntoCustomerAsync(
        ApplicationUser customer,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCustomerAsync(customer))
        {
            return new CartMergeResult { RetainedGuestLineCount = guestCartSessionStore.Get().Items.Count };
        }

        var guestCart = guestCartSessionStore.Get();
        if (guestCart.Items.Count == 0)
        {
            return new CartMergeResult();
        }

        var sources = guestCart.Items.Select(item => new CartLineSource(null, item.GuestCartItemKey, ToInput(item))).ToList();
        var catalog = await LoadCatalogAsync(sources.Select(source => source.Input), cancellationToken);
        var validSources = sources
            .Where(source => Evaluate(source.Input, catalog).IsValid)
            .ToList();
        var retainedKeys = sources.Except(validSources).Select(source => source.GuestCartItemKey!).ToHashSet(StringComparer.Ordinal);

        if (validSources.Count == 0)
        {
            return new CartMergeResult { RetainedGuestLineCount = guestCart.Items.Count };
        }

        var mergedKeys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var cart = await GetTrackedCartAsync(customer.Id, cancellationToken);

            foreach (var source in validSources)
            {
                if (cart is null)
                {
                    cart = new Cart { UserId = customer.Id };
                    context.Carts.Add(cart);
                }

                if (TryAddToCart(cart, source.Input, out _))
                {
                    mergedKeys.Add(source.GuestCartItemKey!);
                }
                else
                {
                    retainedKeys.Add(source.GuestCartItemKey!);
                }
            }

            if (mergedKeys.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Guest cart merge failed for Customer {CustomerId}.", customer.Id);
            return new CartMergeResult
            {
                DatabaseMergeFailed = true,
                RetainedGuestLineCount = guestCart.Items.Count
            };
        }

        guestCart.Items.RemoveAll(item => mergedKeys.Contains(item.GuestCartItemKey));
        try
        {
            guestCartSessionStore.Save(guestCart);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Guest cart session could not be updated after a successful merge.");
            return new CartMergeResult
            {
                MergedLineCount = mergedKeys.Count,
                RetainedGuestLineCount = guestCart.Items.Count,
                SessionUpdateFailed = true
            };
        }

        return new CartMergeResult
        {
            MergedLineCount = mergedKeys.Count,
            RetainedGuestLineCount = guestCart.Items.Count
        };
    }

    private async Task<CartReadResult> BuildReadResultAsync(
        IReadOnlyCollection<CartLineSource> sources,
        bool isGuestCart,
        CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(sources.Select(source => source.Input), cancellationToken);
        var items = sources.Select(source => ToLineResult(source, Evaluate(source.Input, catalog), catalog)).ToList();
        return new CartReadResult
        {
            IsGuestCart = isGuestCart,
            Items = items,
            Subtotal = items.Sum(item => item.CurrentLineTotal)
        };
    }

    private async Task<ConfigurationEvaluation> EvaluateAsync(
        CartConfigurationInputModel input,
        CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync([input], cancellationToken);
        return Evaluate(input, catalog);
    }

    private async Task<CatalogData> LoadCatalogAsync(
        IEnumerable<CartConfigurationInputModel> inputs,
        CancellationToken cancellationToken)
    {
        var normalizedInputs = inputs.Select(Normalize).ToList();
        var productSizeIds = normalizedInputs.Select(input => input.ProductSizeId).Distinct().ToList();
        var toppingIds = normalizedInputs.SelectMany(input => input.ToppingIds).Distinct().ToList();
        var sugarIds = normalizedInputs.Where(input => input.SugarLevelId.HasValue).Select(input => input.SugarLevelId!.Value).Distinct().ToList();
        var iceIds = normalizedInputs.Where(input => input.IceLevelId.HasValue).Select(input => input.IceLevelId!.Value).Distinct().ToList();

        var productSizes = await context.ProductSizes.AsNoTracking().AsSplitQuery()
            .Where(productSize => productSizeIds.Contains(productSize.ProductSizeId))
            .Include(productSize => productSize.Product)
            .ThenInclude(product => product.Category)
            .Include(productSize => productSize.Product)
            .ThenInclude(product => product.ProductImages)
            .Include(productSize => productSize.Product)
            .ThenInclude(product => product.ProductToppings)
            .ThenInclude(productTopping => productTopping.Topping)
            .Include(productSize => productSize.Product)
            .ThenInclude(product => product.ProductSugarLevels)
            .ThenInclude(productSugarLevel => productSugarLevel.SugarLevel)
            .Include(productSize => productSize.Product)
            .ThenInclude(product => product.ProductIceLevels)
            .ThenInclude(productIceLevel => productIceLevel.IceLevel)
            .Include(productSize => productSize.Size)
            .ToListAsync(cancellationToken);
        var toppings = await context.Toppings.AsNoTracking()
            .Where(topping => toppingIds.Contains(topping.ToppingId))
            .ToDictionaryAsync(topping => topping.ToppingId, cancellationToken);
        var sugars = await context.SugarLevels.AsNoTracking()
            .Where(level => sugarIds.Contains(level.SugarLevelId))
            .ToDictionaryAsync(level => level.SugarLevelId, cancellationToken);
        var ices = await context.IceLevels.AsNoTracking()
            .Where(level => iceIds.Contains(level.IceLevelId))
            .ToDictionaryAsync(level => level.IceLevelId, cancellationToken);

        return new CatalogData(
            productSizes.ToDictionary(productSize => productSize.ProductSizeId),
            toppings,
            sugars,
            ices);
    }

    private static ConfigurationEvaluation Evaluate(CartConfigurationInputModel input, CatalogData catalog)
    {
        var normalized = Normalize(input);
        var messages = new List<string>();
        if (normalized.Quantity <= 0)
        {
            messages.Add("Số lượng phải lớn hơn 0.");
        }

        if (!catalog.ProductSizes.TryGetValue(normalized.ProductSizeId, out var productSize))
        {
            messages.Add("Kích cỡ sản phẩm không hợp lệ.");
            return new ConfigurationEvaluation(normalized, null, messages);
        }

        var product = productSize.Product;
        if (!product.IsAvailable || !product.Category.IsActive || productSize.Price <= 0)
        {
            messages.Add("Sản phẩm hiện không thể thêm vào giỏ hàng.");
        }

        ValidateLevel(
            normalized.SugarLevelId,
            product.ProductSugarLevels.Select(level => level.SugarLevelId),
            "mức đường",
            messages);
        ValidateLevel(
            normalized.IceLevelId,
            product.ProductIceLevels.Select(level => level.IceLevelId),
            "mức đá",
            messages);

        foreach (var toppingId in normalized.ToppingIds)
        {
            var isValidTopping = catalog.Toppings.TryGetValue(toppingId, out var topping)
                && topping.IsActive
                && product.ProductToppings.Any(productTopping => productTopping.ToppingId == toppingId);
            if (!isValidTopping)
            {
                messages.Add("Topping đã chọn không còn hợp lệ.");
                break;
            }
        }

        return new ConfigurationEvaluation(normalized, productSize, messages);
    }

    private static void ValidateLevel(
        int? selectedLevelId,
        IEnumerable<int> supportedLevelIds,
        string label,
        ICollection<string> messages)
    {
        var supported = supportedLevelIds.ToHashSet();
        if (supported.Count == 0 && selectedLevelId.HasValue)
        {
            messages.Add($"Sản phẩm không hỗ trợ {label}.");
        }
        else if (supported.Count > 0 && (!selectedLevelId.HasValue || !supported.Contains(selectedLevelId.Value)))
        {
            messages.Add($"{char.ToUpper(label[0])}{label[1..]} đã chọn không hợp lệ.");
        }
    }

    private static CartLineResult ToLineResult(
        CartLineSource source,
        ConfigurationEvaluation evaluation,
        CatalogData catalog)
    {
        var productSize = evaluation.ProductSize;
        var normalized = evaluation.Input;
        var toppings = normalized.ToppingIds.Select(toppingId => catalog.Toppings.TryGetValue(toppingId, out var topping)
            ? new CartToppingResult { ToppingId = topping.ToppingId, Name = topping.Name, Price = topping.Price }
            : new CartToppingResult { ToppingId = toppingId, Name = "Topping không còn tồn tại" }).ToList();
        var unitPrice = productSize?.Price ?? 0;
        unitPrice += toppings.Sum(topping => topping.Price);

        return new CartLineResult
        {
            CartItemId = source.CartItemId,
            GuestCartItemKey = source.GuestCartItemKey,
            ProductSizeId = normalized.ProductSizeId,
            ProductId = productSize?.ProductId,
            ProductImagePath = productSize?.Product.ProductImages
                .OrderBy(image => image.DisplayOrder)
                .ThenBy(image => image.ProductImageId)
                .Select(image => image.ImagePath)
                .FirstOrDefault(),
            CanReconfigure = productSize is not null
                && productSize.Product.IsAvailable
                && productSize.Product.Category.IsActive,
            ProductName = productSize?.Product.Name ?? "Sản phẩm không còn tồn tại",
            SizeName = productSize?.Size.Name ?? "Kích cỡ không còn tồn tại",
            SugarPercentage = normalized.SugarLevelId is int sugarLevelId && catalog.SugarLevels.TryGetValue(sugarLevelId, out var sugarLevel) ? sugarLevel.Percentage : null,
            IcePercentage = normalized.IceLevelId is int iceLevelId && catalog.IceLevels.TryGetValue(iceLevelId, out var iceLevel) ? iceLevel.Percentage : null,
            Toppings = toppings,
            Quantity = normalized.Quantity,
            ProductSizeUnitPrice = productSize?.Price ?? 0m,
            CurrentUnitPrice = unitPrice,
            CurrentLineTotal = unitPrice * normalized.Quantity,
            IsValid = evaluation.IsValid,
            ValidationMessages = evaluation.Messages
        };
    }

    private async Task<Cart?> GetTrackedCartAsync(string userId, CancellationToken cancellationToken) =>
        await context.Carts
            .Include(cart => cart.CartItems)
            .ThenInclude(cartItem => cartItem.CartItemToppings)
            .SingleOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);

    private static bool TryAddToCart(Cart cart, CartConfigurationInputModel input, out string? errorMessage)
    {
        var matchingItem = cart.CartItems.FirstOrDefault(item => SameConfiguration(
            new CartConfigurationInputModel
            {
                ProductSizeId = item.ProductSizeId,
                SugarLevelId = item.SugarLevelId,
                IceLevelId = item.IceLevelId,
                ToppingIds = item.CartItemToppings.Select(topping => topping.ToppingId).ToList(),
                Quantity = item.Quantity
            },
            input));
        if (matchingItem is not null)
        {
            if (!TryIncreaseQuantity(matchingItem.Quantity, input.Quantity, out var quantity))
            {
                errorMessage = "Số lượng sản phẩm vượt quá giới hạn hệ thống.";
                return false;
            }

            matchingItem.Quantity = quantity;
            errorMessage = null;
            return true;
        }

        cart.CartItems.Add(new CartItem
        {
            ProductSizeId = input.ProductSizeId,
            SugarLevelId = input.SugarLevelId,
            IceLevelId = input.IceLevelId,
            Quantity = input.Quantity,
            CartItemToppings = input.ToppingIds.Select(toppingId => new CartItemTopping { ToppingId = toppingId }).ToList()
        });
        errorMessage = null;
        return true;
    }

    private async Task<CartOperationResult> SaveCustomerMutationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return CartOperationResult.Success();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Customer cart mutation failed.");
            return CartOperationResult.Failure("Không thể cập nhật giỏ hàng lúc này.");
        }
    }

    private async Task<bool> IsCustomerAsync(ApplicationUser user) =>
        await userManager.IsInRoleAsync(user, RoleNames.Customer)
        && !await userManager.IsInRoleAsync(user, RoleNames.Admin);

    private static CartConfigurationInputModel Normalize(CartConfigurationInputModel input) => new()
    {
        ProductSizeId = input.ProductSizeId,
        SugarLevelId = input.SugarLevelId,
        IceLevelId = input.IceLevelId,
        ToppingIds = (input.ToppingIds ?? []).Distinct().OrderBy(toppingId => toppingId).ToList(),
        Quantity = input.Quantity
    };

    private static CartConfigurationInputModel ToInput(GuestCartItemSessionModel item) => new()
    {
        ProductSizeId = item.ProductSizeId,
        SugarLevelId = item.SugarLevelId,
        IceLevelId = item.IceLevelId,
        ToppingIds = item.ToppingIds ?? [],
        Quantity = item.Quantity
    };

    private static CartConfigurationInputModel ToInput(CartItem item) => new()
    {
        ProductSizeId = item.ProductSizeId,
        SugarLevelId = item.SugarLevelId,
        IceLevelId = item.IceLevelId,
        ToppingIds = item.CartItemToppings.Select(topping => topping.ToppingId).ToList(),
        Quantity = item.Quantity
    };

    private void ApplyConfiguration(CartItem item, CartConfigurationInputModel input)
    {
        item.ProductSizeId = input.ProductSizeId;
        item.SugarLevelId = input.SugarLevelId;
        item.IceLevelId = input.IceLevelId;
        item.Quantity = input.Quantity;

        var toppingIds = input.ToppingIds.ToHashSet();
        foreach (var existingTopping in item.CartItemToppings.ToList())
        {
            if (!toppingIds.Contains(existingTopping.ToppingId))
            {
                context.CartItemToppings.Remove(existingTopping);
            }
        }

        var existingIds = item.CartItemToppings.Select(topping => topping.ToppingId).ToHashSet();
        foreach (var toppingId in input.ToppingIds.Where(toppingId => !existingIds.Contains(toppingId)))
        {
            item.CartItemToppings.Add(new CartItemTopping { CartItemId = item.CartItemId, ToppingId = toppingId });
        }
    }

    private static bool SameConfiguration(CartConfigurationInputModel first, CartConfigurationInputModel second)
    {
        var normalizedFirst = Normalize(first);
        var normalizedSecond = Normalize(second);
        return normalizedFirst.ProductSizeId == normalizedSecond.ProductSizeId
            && normalizedFirst.SugarLevelId == normalizedSecond.SugarLevelId
            && normalizedFirst.IceLevelId == normalizedSecond.IceLevelId
            && normalizedFirst.ToppingIds.SequenceEqual(normalizedSecond.ToppingIds);
    }

    private static bool TryIncreaseQuantity(int current, int additional, out int quantity)
    {
        if (additional <= 0 || current > int.MaxValue - additional)
        {
            quantity = 0;
            return false;
        }

        quantity = current + additional;
        return true;
    }

    private static string FirstMessage(ConfigurationEvaluation evaluation) =>
        evaluation.Messages.FirstOrDefault() ?? "Cấu hình sản phẩm không hợp lệ.";

    private sealed record CatalogData(
        IReadOnlyDictionary<int, ProductSize> ProductSizes,
        IReadOnlyDictionary<int, Topping> Toppings,
        IReadOnlyDictionary<int, SugarLevel> SugarLevels,
        IReadOnlyDictionary<int, IceLevel> IceLevels);

    private sealed record CartLineSource(
        int? CartItemId,
        string? GuestCartItemKey,
        CartConfigurationInputModel Input);

    private sealed record ConfigurationEvaluation(
        CartConfigurationInputModel Input,
        ProductSize? ProductSize,
        List<string> Messages)
    {
        public bool IsValid => Messages.Count == 0;
    }
}
