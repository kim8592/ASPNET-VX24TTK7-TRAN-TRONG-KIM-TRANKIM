using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Cart;
using MilkTeaWeb.ViewModels.Products;

namespace MilkTeaWeb.Controllers;

public sealed class ProductsController(
    ApplicationDbContext context,
    CartService cartService,
    UserManager<ApplicationUser> userManager) : Controller
{
    private static readonly IReadOnlyList<ProductPriceRangeOptionViewModel> PriceRanges =
    [
        new() { Key = "under-35", Label = "Dưới 35.000 đ", ExclusiveMaximumStartingPrice = 35000m },
        new() { Key = "35-to-under-40", Label = "35–39k", MinimumStartingPrice = 35000m, ExclusiveMaximumStartingPrice = 40000m },
        new() { Key = "40-to-under-45", Label = "40–44k", MinimumStartingPrice = 40000m, ExclusiveMaximumStartingPrice = 45000m },
        new() { Key = "45-plus", Label = "Từ 45.000 đ", MinimumStartingPrice = 45000m }
    ];

    [HttpGet]
    public async Task<IActionResult> Index(
        int? categoryId,
        string? search,
        string? priceRange,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = (search ?? string.Empty).Trim();
        var normalizedPriceRangeKey = (priceRange ?? string.Empty).Trim();
        var selectedPriceRange = string.IsNullOrEmpty(normalizedPriceRangeKey)
            ? null
            : PriceRanges.SingleOrDefault(range => string.Equals(
                range.Key,
                normalizedPriceRangeKey,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(normalizedPriceRangeKey) && selectedPriceRange is null)
        {
            return NotFound();
        }

        var categories = await context.Categories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .Select(category => new ProductCategoryOptionViewModel
            {
                CategoryId = category.CategoryId,
                Name = category.Name
            })
            .ToListAsync(cancellationToken);

        var selectedCategory = categoryId is null
            ? null
            : categories.SingleOrDefault(category => category.CategoryId == categoryId.Value);
        if (categoryId is not null && selectedCategory is null)
        {
            return NotFound();
        }

        var products = context.Products.AsNoTracking()
            .Where(product => product.IsAvailable
                && product.Category.IsActive
                && product.ProductSizes.Any());

        if (categoryId is not null)
        {
            products = products.Where(product => product.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            products = products.Where(product => product.Name.Contains(normalizedSearch));
        }

        if (selectedPriceRange?.MinimumStartingPrice is decimal minimumStartingPrice)
        {
            products = products.Where(product => product.ProductSizes.Min(size => size.Price) >= minimumStartingPrice);
        }

        if (selectedPriceRange?.ExclusiveMaximumStartingPrice is decimal exclusiveMaximumStartingPrice)
        {
            products = products.Where(product => product.ProductSizes.Min(size => size.Price) < exclusiveMaximumStartingPrice);
        }

        var model = new ProductCatalogViewModel
        {
            Search = normalizedSearch,
            SelectedCategoryId = categoryId,
            SelectedCategoryName = selectedCategory?.Name,
            SelectedPriceRangeKey = selectedPriceRange?.Key,
            Categories = categories,
            PriceRanges = PriceRanges,
            Products = await products
                .OrderBy(product => product.Name)
                .Select(product => new ProductListItemViewModel
                {
                    ProductId = product.ProductId,
                    Name = product.Name,
                    Description = product.Description,
                    CategoryName = product.Category.Name,
                    StartingPrice = product.ProductSizes.Min(size => size.Price),
                    ImagePath = product.ProductImages
                        .OrderBy(image => image.DisplayOrder)
                        .ThenBy(image => image.ProductImageId)
                        .Select(image => image.ImagePath)
                        .FirstOrDefault()
                })
                .ToListAsync(cancellationToken)
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        int id,
        int? cartItemId,
        string? guestLineKey,
        CancellationToken cancellationToken)
    {
        if (cartItemId.HasValue && !string.IsNullOrWhiteSpace(guestLineKey))
        {
            return NotFound();
        }

        CartEditItemResult? editItem = null;
        if (cartItemId is int customerCartItemId)
        {
            var customer = await GetCurrentCustomerAsync();
            if (customer is null)
            {
                return NotFound();
            }

            editItem = await cartService.GetCustomerItemForEditAsync(customer, customerCartItemId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(guestLineKey))
        {
            if (User.IsInRole(RoleNames.Admin))
            {
                return NotFound();
            }

            editItem = await cartService.GetGuestItemForEditAsync(guestLineKey, cancellationToken);
        }

        if ((cartItemId.HasValue || !string.IsNullOrWhiteSpace(guestLineKey))
            && (editItem is null || editItem.ProductId != id))
        {
            return NotFound();
        }

        var model = await context.Products.AsNoTracking()
            .Where(product => product.ProductId == id
                && product.IsAvailable
                && product.Category.IsActive
                && product.ProductSizes.Any())
            .Select(product => new ProductDetailsViewModel
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                CategoryName = product.Category.Name,
                StartingPrice = product.ProductSizes.Min(size => size.Price),
                Images = product.ProductImages
                    .OrderBy(image => image.DisplayOrder)
                    .ThenBy(image => image.ProductImageId)
                    .Select(image => new ProductImageViewModel
                    {
                        ImagePath = image.ImagePath,
                        DisplayOrder = image.DisplayOrder
                    })
                    .ToList(),
                Sizes = product.ProductSizes
                    .OrderBy(size => size.Size.DisplayOrder)
                    .ThenBy(size => size.ProductSizeId)
                    .Select(size => new ProductSizeOptionViewModel
                    {
                        ProductSizeId = size.ProductSizeId,
                        Name = size.Size.Name,
                        Price = size.Price
                    })
                    .ToList(),
                SugarLevels = product.ProductSugarLevels
                    .OrderBy(level => level.SugarLevel.DisplayOrder)
                    .ThenBy(level => level.SugarLevelId)
                    .Select(level => new ProductLevelOptionViewModel
                    {
                        LevelId = level.SugarLevelId,
                        Percentage = level.SugarLevel.Percentage
                    })
                    .ToList(),
                IceLevels = product.ProductIceLevels
                    .OrderBy(level => level.IceLevel.DisplayOrder)
                    .ThenBy(level => level.IceLevelId)
                    .Select(level => new ProductLevelOptionViewModel
                    {
                        LevelId = level.IceLevelId,
                        Percentage = level.IceLevel.Percentage
                    })
                    .ToList(),
                Toppings = product.ProductToppings
                    .Where(relation => relation.Topping.IsActive)
                    .OrderBy(relation => relation.Topping.Name)
                    .ThenBy(relation => relation.ToppingId)
                    .Select(relation => new ProductToppingOptionViewModel
                    {
                        ToppingId = relation.ToppingId,
                        Name = relation.Topping.Name,
                        Price = relation.Topping.Price
                    })
                    .ToList(),
                Input = new AddToCartInputModel
                {
                    ProductSizeId = product.ProductSizes
                        .OrderBy(size => size.Size.DisplayOrder)
                        .ThenBy(size => size.ProductSizeId)
                        .Select(size => size.ProductSizeId)
                        .FirstOrDefault(),
                    SugarLevelId = product.ProductSugarLevels
                        .OrderBy(level => level.SugarLevel.DisplayOrder)
                        .Select(level => (int?)level.SugarLevelId)
                        .FirstOrDefault(),
                    IceLevelId = product.ProductIceLevels
                        .OrderBy(level => level.IceLevel.DisplayOrder)
                        .Select(level => (int?)level.IceLevelId)
                        .FirstOrDefault(),
                    Quantity = 1
                }
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (model is null)
        {
            return NotFound();
        }

        if (editItem is not null)
        {
            model.Input = ToInputModel(editItem);
            model.IsEditing = true;
        }

        return View(model);
    }

    private async Task<ApplicationUser?> GetCurrentCustomerAsync()
    {
        if (User.Identity?.IsAuthenticated != true
            || User.IsInRole(RoleNames.Admin)
            || !User.IsInRole(RoleNames.Customer))
        {
            return null;
        }

        return await userManager.GetUserAsync(User);
    }

    private static AddToCartInputModel ToInputModel(CartEditItemResult editItem) => new()
    {
        CartItemId = editItem.CartItemId,
        GuestLineKey = editItem.GuestCartItemKey,
        ProductSizeId = editItem.Configuration.ProductSizeId,
        SugarLevelId = editItem.Configuration.SugarLevelId,
        IceLevelId = editItem.Configuration.IceLevelId,
        ToppingIds = editItem.Configuration.ToppingIds,
        Quantity = editItem.Configuration.Quantity
    };
}
