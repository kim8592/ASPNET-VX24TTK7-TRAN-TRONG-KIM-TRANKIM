using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Security;
using MilkTeaWeb.Services;
using MilkTeaWeb.ViewModels.Admin.Products;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ProductsController(ApplicationDbContext context, ProductImageService imageService, ILogger<ProductsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await context.Products.AsNoTracking().OrderBy(x => x.Name).Select(x => new AdminProductListItemViewModel
    {
        ProductId = x.ProductId, Name = x.Name, CategoryName = x.Category.Name, IsAvailable = x.IsAvailable,
        IsSellable = x.IsAvailable && x.Category.IsActive && x.ProductSizes.Any(),
        SellabilityReason = Sellability(x.IsAvailable, x.Category.IsActive, x.ProductSizes.Any()),
        ImagePath = x.ProductImages.OrderBy(i => i.DisplayOrder).ThenBy(i => i.ProductImageId).Select(i => i.ImagePath).FirstOrDefault()
    }).ToListAsync());

    [HttpGet] public async Task<IActionResult> Create() { var model = new AdminProductInputModel { IsAvailable = true }; await Categories(model); return View(model); }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AdminProductInputModel model)
    {
        Normalize(model); await ValidateCategory(model);
        if (!ModelState.IsValid) { await Categories(model); return View(model); }
        var product = new Product { Name = model.Name, Description = model.Description, CategoryId = model.CategoryId, IsAvailable = model.IsAvailable };
        context.Products.Add(product);
        try { await context.SaveChangesAsync(); TempData["Success"] = "Đã tạo sản phẩm. Hãy cấu hình kích cỡ và tuỳ chọn."; return RedirectToAction(nameof(Configuration), new { id = product.ProductId }); }
        catch (DbUpdateException e) { logger.LogError(e, "Product create failed."); ModelState.AddModelError("", "Không thể lưu sản phẩm lúc này."); await Categories(model); return View(model); }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var p = await context.Products.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == id); if (p is null) return NotFound();
        var model = new AdminProductInputModel { Name = p.Name, Description = p.Description, CategoryId = p.CategoryId, IsAvailable = p.IsAvailable }; await Categories(model); return View(model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminProductInputModel model)
    {
        Normalize(model); await ValidateCategory(model); if (!ModelState.IsValid) { await Categories(model); return View(model); }
        var p = await context.Products.FindAsync(id); if (p is null) return NotFound();
        p.Name = model.Name; p.Description = model.Description; p.CategoryId = model.CategoryId; p.IsAvailable = model.IsAvailable;
        try { await context.SaveChangesAsync(); TempData["Success"] = "Đã cập nhật sản phẩm."; return RedirectToAction(nameof(Index)); }
        catch (DbUpdateException e) { logger.LogError(e, "Product edit failed."); ModelState.AddModelError("", "Không thể lưu sản phẩm lúc này."); await Categories(model); return View(model); }
    }

    [HttpGet]
    public async Task<IActionResult> Configuration(int id)
    {
        var p = await ProductWithCategory(id); return p is null ? NotFound() : View(await ConfigurationModel(p));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Configuration(int id, ProductConfigurationInputModel input)
    {
        var p = await ProductWithCategory(id); if (p is null) return NotFound();
        await ValidateConfiguration(input, id);
        if (!ModelState.IsValid) return View(await ConfigurationModel(p, input));
        var selected = input.Sizes.Where(x => x.IsSelected).ToDictionary(x => x.SizeId);
        var existing = await context.ProductSizes.Where(x => x.ProductId == id).ToListAsync();
        var removedProductSizeIds = existing
            .Where(productSize => !selected.ContainsKey(productSize.SizeId))
            .Select(productSize => productSize.ProductSizeId)
            .ToList();
        if (removedProductSizeIds.Count > 0
            && await context.CartItems.AnyAsync(cartItem => removedProductSizeIds.Contains(cartItem.ProductSizeId)))
        {
            ModelState.AddModelError(nameof(input.Sizes), "Không thể bỏ kích cỡ đang được dùng trong giỏ hàng của khách.");
            return View(await ConfigurationModel(p, input));
        }
        context.ProductSizes.RemoveRange(existing.Where(x => !selected.ContainsKey(x.SizeId)));
        foreach (var row in selected.Values)
        {
            var relation = existing.SingleOrDefault(x => x.SizeId == row.SizeId);
            if (relation is null) context.ProductSizes.Add(new ProductSize { ProductId = id, SizeId = row.SizeId, Price = row.Price!.Value });
            else relation.Price = row.Price!.Value;
        }
        await Sync(id, input);
        try { await context.SaveChangesAsync(); TempData["Success"] = "Đã lưu cấu hình sản phẩm."; return RedirectToAction(nameof(Configuration), new { id }); }
        catch (DbUpdateException e) { logger.LogError(e, "Configuration save failed."); ModelState.AddModelError("", "Không thể lưu cấu hình lúc này."); return View(await ConfigurationModel(p, input)); }
    }

    [HttpGet]
    public async Task<IActionResult> Images(int id)
    {
        var p = await context.Products.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == id); return p is null ? NotFound() : View(await ImagesModel(p));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImage(int id, [Bind(Prefix = "Upload")] UploadProductImageInputModel input)
    {
        var p = await context.Products.AsNoTracking().FirstOrDefaultAsync(x => x.ProductId == id); if (p is null) return NotFound();
        if (!ModelState.IsValid) return View("Images", await ImagesModel(p, input));
        try { await imageService.UploadAsync(id, input.File!); TempData["Success"] = "Đã tải ảnh lên."; }
        catch (ProductImageValidationException e) { TempData["Error"] = e.Message; }
        catch (Exception e) { logger.LogError(e, "Image upload failed."); TempData["Error"] = "Không thể tải ảnh lên lúc này."; }
        return RedirectToAction(nameof(Images), new { id });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int id, int productImageId)
    {
        var image = await context.ProductImages.FirstOrDefaultAsync(x => x.ProductId == id && x.ProductImageId == productImageId); if (image is null) return NotFound();
        context.ProductImages.Remove(image);
        try { await context.SaveChangesAsync(); TempData[await imageService.DeleteManagedFileAsync(image) ? "Success" : "Warning"] = "Đã xóa ảnh sản phẩm."; }
        catch (DbUpdateException e) { logger.LogError(e, "Image delete failed."); TempData["Error"] = "Không thể xóa ảnh lúc này."; }
        return RedirectToAction(nameof(Images), new { id });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateImageOrder(int id, UpdateProductImageOrderInputModel input)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Thứ tự ảnh không hợp lệ."; return RedirectToAction(nameof(Images), new { id }); }
        var image = await context.ProductImages.FirstOrDefaultAsync(x => x.ProductId == id && x.ProductImageId == input.ProductImageId); if (image is null) return NotFound();
        image.DisplayOrder = input.DisplayOrder;
        try { await context.SaveChangesAsync(); TempData["Success"] = "Đã cập nhật thứ tự ảnh."; }
        catch (DbUpdateException e) { logger.LogError(e, "Image order update failed."); TempData["Error"] = "Không thể cập nhật thứ tự ảnh."; }
        return RedirectToAction(nameof(Images), new { id });
    }

    private async Task ValidateConfiguration(ProductConfigurationInputModel input, int productId)
    {
        input.Sizes ??= []; input.SupportedToppingIds ??= []; input.SupportedSugarLevelIds ??= []; input.SupportedIceLevelIds ??= [];
        var sizeIds = input.Sizes.Select(x => x.SizeId).ToList();
        if (sizeIds.Count != sizeIds.Distinct().Count() || await context.Sizes.CountAsync(x => sizeIds.Contains(x.SizeId)) != sizeIds.Distinct().Count()) ModelState.AddModelError(nameof(input.Sizes), "Danh sách kích cỡ không hợp lệ.");
        for (var index = 0; index < input.Sizes.Count; index++)
        {
            var row = input.Sizes[index];
            if (row.IsSelected && (!row.Price.HasValue || row.Price <= 0))
            {
                ModelState.AddModelError($"Input.Sizes[{index}].Price", "Nhập giá lớn hơn 0 cho kích cỡ đã chọn.");
            }
        }
        await SetValid(input.SupportedToppingIds, context.Toppings.Select(x => x.ToppingId), nameof(input.SupportedToppingIds), "topping");
        await SetValid(input.SupportedSugarLevelIds, context.SugarLevels.Select(x => x.SugarLevelId), nameof(input.SupportedSugarLevelIds), "mức đường");
        await SetValid(input.SupportedIceLevelIds, context.IceLevels.Select(x => x.IceLevelId), nameof(input.SupportedIceLevelIds), "mức đá");
        var assignedInactive = await context.ProductToppings.Where(x => x.ProductId == productId && !x.Topping.IsActive).Select(x => x.ToppingId).ToListAsync();
        var inactive = await context.Toppings.Where(x => input.SupportedToppingIds.Contains(x.ToppingId) && !x.IsActive).Select(x => x.ToppingId).ToListAsync();
        if (inactive.Except(assignedInactive).Any()) ModelState.AddModelError(nameof(input.SupportedToppingIds), "Chỉ có thể thêm topping đang hoạt động.");
    }
    private async Task SetValid(List<int> ids, IQueryable<int> lookup, string key, string label)
    { if (ids.Count != ids.Distinct().Count() || await lookup.CountAsync(x => ids.Contains(x)) != ids.Distinct().Count()) ModelState.AddModelError(key, $"Danh sách {label} không hợp lệ."); }
    private async Task Sync(int productId, ProductConfigurationInputModel input)
    {
        var toppings = await context.ProductToppings.Where(x => x.ProductId == productId).ToListAsync(); context.ProductToppings.RemoveRange(toppings.Where(x => !input.SupportedToppingIds.Contains(x.ToppingId))); context.ProductToppings.AddRange(input.SupportedToppingIds.Except(toppings.Select(x => x.ToppingId)).Select(x => new ProductTopping { ProductId = productId, ToppingId = x }));
        var sugars = await context.ProductSugarLevels.Where(x => x.ProductId == productId).ToListAsync(); context.ProductSugarLevels.RemoveRange(sugars.Where(x => !input.SupportedSugarLevelIds.Contains(x.SugarLevelId))); context.ProductSugarLevels.AddRange(input.SupportedSugarLevelIds.Except(sugars.Select(x => x.SugarLevelId)).Select(x => new ProductSugarLevel { ProductId = productId, SugarLevelId = x }));
        var ices = await context.ProductIceLevels.Where(x => x.ProductId == productId).ToListAsync(); context.ProductIceLevels.RemoveRange(ices.Where(x => !input.SupportedIceLevelIds.Contains(x.IceLevelId))); context.ProductIceLevels.AddRange(input.SupportedIceLevelIds.Except(ices.Select(x => x.IceLevelId)).Select(x => new ProductIceLevel { ProductId = productId, IceLevelId = x }));
    }
    private async Task<ProductConfigurationViewModel> ConfigurationModel(Product p, ProductConfigurationInputModel? input = null)
    {
        var sizes = await context.Sizes.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => new ProductSizeOptionViewModel { SizeId = x.SizeId, Name = x.Name, DisplayOrder = x.DisplayOrder }).ToListAsync();
        if (input is null) { var relations = await context.ProductSizes.Where(x => x.ProductId == p.ProductId).ToDictionaryAsync(x => x.SizeId, x => x.Price); input = new ProductConfigurationInputModel { Sizes = sizes.Select(x => new ProductSizeInputModel { SizeId = x.SizeId, IsSelected = relations.TryGetValue(x.SizeId, out var price), Price = relations.TryGetValue(x.SizeId, out price) ? price : null }).ToList(), SupportedToppingIds = await context.ProductToppings.Where(x => x.ProductId == p.ProductId).Select(x => x.ToppingId).ToListAsync(), SupportedSugarLevelIds = await context.ProductSugarLevels.Where(x => x.ProductId == p.ProductId).Select(x => x.SugarLevelId).ToListAsync(), SupportedIceLevelIds = await context.ProductIceLevels.Where(x => x.ProductId == p.ProductId).Select(x => x.IceLevelId).ToListAsync() }; }
        var toppings = await context.Toppings.AsNoTracking().OrderBy(x => x.Name).Select(x => new ProductToppingOptionViewModel { ToppingId = x.ToppingId, Name = x.Name, Price = x.Price, IsActive = x.IsActive, IsAssigned = input.SupportedToppingIds.Contains(x.ToppingId) }).ToListAsync();
        var sugar = await context.SugarLevels.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => new ProductLevelOptionViewModel { Id = x.SugarLevelId, Percentage = x.Percentage, DisplayOrder = x.DisplayOrder }).ToListAsync();
        var ice = await context.IceLevels.AsNoTracking().OrderBy(x => x.DisplayOrder).Select(x => new ProductLevelOptionViewModel { Id = x.IceLevelId, Percentage = x.Percentage, DisplayOrder = x.DisplayOrder }).ToListAsync();
        var hasValidSize = input.Sizes.Any(x => x.IsSelected && x.Price is > 0);
        return new ProductConfigurationViewModel { ProductId = p.ProductId, ProductName = p.Name, CategoryName = p.Category.Name, IsSellable = p.IsAvailable && p.Category.IsActive && hasValidSize, SellabilityReason = Sellability(p.IsAvailable, p.Category.IsActive, hasValidSize), Input = input, SizeOptions = sizes, ToppingOptions = toppings, SugarLevelOptions = sugar, IceLevelOptions = ice };
    }
    private async Task<ProductImagesViewModel> ImagesModel(Product p, UploadProductImageInputModel? upload = null) => new() { ProductId = p.ProductId, ProductName = p.Name, Upload = upload ?? new(), Images = await context.ProductImages.AsNoTracking().Where(x => x.ProductId == p.ProductId).OrderBy(x => x.DisplayOrder).ThenBy(x => x.ProductImageId).Select(x => new ProductImageItemViewModel { ProductImageId = x.ProductImageId, ImagePath = x.ImagePath, DisplayOrder = x.DisplayOrder }).ToListAsync() };
    private Task<Product?> ProductWithCategory(int id) => context.Products.Include(x => x.Category).FirstOrDefaultAsync(x => x.ProductId == id);
    private async Task ValidateCategory(AdminProductInputModel model) { if (model.CategoryId > 0 && !await context.Categories.AnyAsync(x => x.CategoryId == model.CategoryId)) ModelState.AddModelError(nameof(model.CategoryId), "Danh mục được chọn không tồn tại."); }
    private async Task Categories(AdminProductInputModel model) => model.CategoryOptions = await context.Categories.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem { Value = x.CategoryId.ToString(), Text = x.IsActive ? x.Name : $"{x.Name} (ngừng hoạt động)" }).ToListAsync();
    private static string Sellability(bool available, bool categoryActive, bool hasSize) => !available ? "Sản phẩm đang ngừng bán." : !categoryActive ? "Danh mục đang ngừng hoạt động." : hasSize ? "Có thể bán." : "Chưa có kích cỡ và giá bán.";
    private static void Normalize(AdminProductInputModel model) { model.Name = (model.Name ?? "").Trim(); model.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(); }
}
