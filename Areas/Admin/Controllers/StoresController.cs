using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Security;
using MilkTeaWeb.ViewModels.Admin.Stores;

namespace MilkTeaWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class StoresController(
    ApplicationDbContext context,
    ILogger<StoresController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var stores = await context.Stores
            .AsNoTracking()
            .OrderBy(store => store.Name)
            .Select(store => new StoreListItemViewModel
            {
                StoreId = store.StoreId,
                Name = store.Name,
                Address = store.Address,
                Phone = store.Phone,
                IsActive = store.IsActive
            })
            .ToListAsync();

        return View(stores);
    }

    [HttpGet]
    public IActionResult Create() => View(new StoreInputModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(StoreInputModel model)
    {
        Normalize(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        context.Stores.Add(new Store
        {
            Name = model.Name,
            Address = model.Address,
            Phone = model.Phone,
            IsActive = true
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Store creation failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu cửa hàng lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã tạo cửa hàng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var store = await context.Stores.AsNoTracking().FirstOrDefaultAsync(item => item.StoreId == id);
        if (store is null)
        {
            return NotFound();
        }

        return View(new StoreInputModel
        {
            Name = store.Name,
            Address = store.Address,
            Phone = store.Phone
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, StoreInputModel model)
    {
        Normalize(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var store = await context.Stores.FindAsync(id);
        if (store is null)
        {
            return NotFound();
        }

        store.Name = model.Name;
        store.Address = model.Address;
        store.Phone = model.Phone;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Store update failed.");
            ModelState.AddModelError(string.Empty, "Không thể lưu cửa hàng lúc này. Vui lòng thử lại.");
            return View(model);
        }

        TempData["Success"] = "Đã cập nhật cửa hàng.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id)
    {
        var store = await context.Stores.FindAsync(id);
        if (store is null)
        {
            return NotFound();
        }

        store.IsActive = !store.IsActive;

        try
        {
            await context.SaveChangesAsync();
            TempData["Success"] = store.IsActive
                ? "Cửa hàng đã được kích hoạt."
                : "Cửa hàng đã được ngừng hoạt động.";
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Store status update failed.");
            TempData["Error"] = "Không thể cập nhật trạng thái cửa hàng lúc này.";
        }

        return RedirectToAction(nameof(Index));
    }

    private static void Normalize(StoreInputModel model)
    {
        model.Name = (model.Name ?? string.Empty).Trim();
        model.Address = (model.Address ?? string.Empty).Trim();
        model.Phone = (model.Phone ?? string.Empty).Trim();
    }
}
