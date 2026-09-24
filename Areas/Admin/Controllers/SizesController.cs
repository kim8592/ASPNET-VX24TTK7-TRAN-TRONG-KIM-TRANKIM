using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Admin.Sizes;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class SizesController(
    ApplicationDbContext context,
    ILogger<SizesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var sizes = await context.Sizes
            .AsNoTracking()
            .OrderBy(size => size.DisplayOrder)
            .ThenBy(size => size.Name)
            .Select(size => new SizeListItemViewModel
            {
                SizeId = size.SizeId,
                Name = size.Name,
                DisplayOrder = size.DisplayOrder
            })
            .ToListAsync();

        return View(sizes);
    }

    [HttpGet]
    public IActionResult Create() => View(new SizeInputModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SizeInputModel model)
    {
        Normalize(model);

        if (await HasDuplicateNameAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Tên kích cỡ đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        context.Sizes.Add(new Size
        {
            Name = model.Name,
            DisplayOrder = model.DisplayOrder
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Size creation failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu kích cỡ lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã tạo kích cỡ.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var size = await context.Sizes.AsNoTracking().FirstOrDefaultAsync(item => item.SizeId == id);
        if (size is null)
        {
            return NotFound();
        }

        return View(new SizeInputModel
        {
            Name = size.Name,
            DisplayOrder = size.DisplayOrder
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SizeInputModel model)
    {
        Normalize(model);

        if (await HasDuplicateNameAsync(model.Name, id))
        {
            ModelState.AddModelError(nameof(model.Name), "Tên kích cỡ đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var size = await context.Sizes.FindAsync(id);
        if (size is null)
        {
            return NotFound();
        }

        size.Name = model.Name;
        size.DisplayOrder = model.DisplayOrder;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Size update failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu kích cỡ lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã cập nhật kích cỡ.";
        return RedirectToAction(nameof(Index));
    }

    private Task<bool> HasDuplicateNameAsync(string name, int? excludedSizeId = null) =>
        context.Sizes.AnyAsync(size => size.Name == name
            && (!excludedSizeId.HasValue || size.SizeId != excludedSizeId.Value));

    private static void Normalize(SizeInputModel model) =>
        model.Name = (model.Name ?? string.Empty).Trim();
}
