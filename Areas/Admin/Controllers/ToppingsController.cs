using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Admin.Toppings;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ToppingsController(
    ApplicationDbContext context,
    ILogger<ToppingsController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var toppings = await context.Toppings
            .AsNoTracking()
            .OrderBy(topping => topping.Name)
            .Select(topping => new ToppingListItemViewModel
            {
                ToppingId = topping.ToppingId,
                Name = topping.Name,
                Price = topping.Price,
                IsActive = topping.IsActive
            })
            .ToListAsync();

        return View(toppings);
    }

    [HttpGet]
    public IActionResult Create() => View(new ToppingInputModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ToppingInputModel model)
    {
        Normalize(model);

        if (await HasDuplicateNameAsync(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Tên topping đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        context.Toppings.Add(new Topping
        {
            Name = model.Name,
            Price = model.Price,
            IsActive = true
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Topping creation failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu topping lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã tạo topping.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var topping = await context.Toppings.AsNoTracking().FirstOrDefaultAsync(item => item.ToppingId == id);
        if (topping is null)
        {
            return NotFound();
        }

        return View(new ToppingInputModel
        {
            Name = topping.Name,
            Price = topping.Price
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ToppingInputModel model)
    {
        Normalize(model);

        if (await HasDuplicateNameAsync(model.Name, id))
        {
            ModelState.AddModelError(nameof(model.Name), "Tên topping đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var topping = await context.Toppings.FindAsync(id);
        if (topping is null)
        {
            return NotFound();
        }

        topping.Name = model.Name;
        topping.Price = model.Price;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Topping update failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu topping lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã cập nhật topping.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id)
    {
        var topping = await context.Toppings.FindAsync(id);
        if (topping is null)
        {
            return NotFound();
        }

        topping.IsActive = !topping.IsActive;

        try
        {
            await context.SaveChangesAsync();
            TempData["Success"] = topping.IsActive
                ? "Topping đã được kích hoạt."
                : "Topping đã được ngừng hoạt động.";
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Topping status update failed.");
            TempData["Error"] = "Không thể cập nhật trạng thái topping lúc này.";
        }

        return RedirectToAction(nameof(Index));
    }

    private Task<bool> HasDuplicateNameAsync(string name, int? excludedToppingId = null) =>
        context.Toppings.AnyAsync(topping => topping.Name == name
            && (!excludedToppingId.HasValue || topping.ToppingId != excludedToppingId.Value));

    private static void Normalize(ToppingInputModel model) =>
        model.Name = (model.Name ?? string.Empty).Trim();
}
