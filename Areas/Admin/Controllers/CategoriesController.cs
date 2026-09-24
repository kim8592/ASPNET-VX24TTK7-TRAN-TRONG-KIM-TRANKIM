using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Admin.Categories;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class CategoriesController(
    ApplicationDbContext context,
    ILogger<CategoriesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var categories = await context.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .Select(category => new CategoryListItemViewModel
            {
                CategoryId = category.CategoryId,
                Name = category.Name,
                IsActive = category.IsActive
            })
            .ToListAsync();

        return View(categories);
    }

    [HttpGet]
    public IActionResult Create() => View(new CategoryInputModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryInputModel model)
    {
        Normalize(model);

        if (await HasDuplicateNameAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Tên danh mục đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        context.Categories.Add(new Category
        {
            Name = model.Name,
            IsActive = true
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Category creation failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu danh mục lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã tạo danh mục.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await context.Categories.AsNoTracking().FirstOrDefaultAsync(item => item.CategoryId == id);
        if (category is null)
        {
            return NotFound();
        }

        return View(new CategoryInputModel { Name = category.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CategoryInputModel model)
    {
        Normalize(model);

        if (await HasDuplicateNameAsync(model.Name, id))
        {
            ModelState.AddModelError(nameof(model.Name), "Tên danh mục đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var category = await context.Categories.FindAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        category.Name = model.Name;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Category update failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu danh mục lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã cập nhật danh mục.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id)
    {
        var category = await context.Categories.FindAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = !category.IsActive;

        try
        {
            await context.SaveChangesAsync();
            TempData["Success"] = category.IsActive
                ? "Danh mục đã được kích hoạt."
                : "Danh mục đã được ngừng hoạt động.";
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Category status update failed.");
            TempData["Error"] = "Không thể cập nhật trạng thái danh mục lúc này.";
        }

        return RedirectToAction(nameof(Index));
    }

    private Task<bool> HasDuplicateNameAsync(string name, int? excludedCategoryId = null) =>
        context.Categories.AnyAsync(category => category.Name == name
            && (!excludedCategoryId.HasValue || category.CategoryId != excludedCategoryId.Value));

    private static void Normalize(CategoryInputModel model) =>
        model.Name = (model.Name ?? string.Empty).Trim();
}
