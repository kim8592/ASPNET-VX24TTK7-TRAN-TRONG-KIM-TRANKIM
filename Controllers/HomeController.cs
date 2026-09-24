using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models;
using MilkTeaWeb.ViewModels.Blog;
using MilkTeaWeb.ViewModels.Home;

namespace MilkTeaWeb.Controllers;

public sealed class HomeController(ApplicationDbContext context) : Controller
{
    private const string DefaultHeroImagePath = "/uploads/products/4/491cbb656d33466b9bd0fcf09d594f53.webp";

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var products = await context.Products.AsNoTracking()
            .Where(product => product.IsAvailable
                && product.Category.IsActive
                && product.ProductSizes.Any())
            .OrderBy(product => product.Name)
            .Select(product => new HomeProductViewModel
            {
                ProductId = product.ProductId,
                Name = product.Name,
                CategoryName = product.Category.Name,
                Description = product.Description,
                StartingPrice = product.ProductSizes.Min(size => size.Price),
                ImagePath = product.ProductImages
                    .OrderBy(image => image.DisplayOrder)
                    .ThenBy(image => image.ProductImageId)
                    .Select(image => image.ImagePath)
                    .FirstOrDefault()
            })
            .Take(4)
            .ToListAsync(cancellationToken);

        var categories = await context.Categories.AsNoTracking()
            .Where(category => category.IsActive && category.Products.Any(product => product.IsAvailable && product.ProductSizes.Any()))
            .OrderBy(category => category.Name)
            .Select(category => new HomeCategoryViewModel
            {
                CategoryId = category.CategoryId,
                Name = category.Name,
                ProductCount = category.Products.Count(product => product.IsAvailable && product.ProductSizes.Any()),
                ImagePath = category.Name == "Cà phê"
                    ? "/images/category-coffee.jpg"
                    : category.Name == "Đồ uống đặc biệt"
                        ? "/images/category-special.jpg"
                        : category.Name == "Trà nguyên vị"
                            ? "/images/category-tea.jpg"
                            : category.Name == "Trà sữa"
                                ? "/uploads/products/4/491cbb656d33466b9bd0fcf09d594f53.webp"
                                : null
            })
            .Take(4)
            .ToListAsync(cancellationToken);

        var heroImage = products.Select(product => product.ImagePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        return View(new HomeViewModel
        {
            FeaturedProducts = products,
            Categories = categories,
            RecentPosts = BlogPosts.All,
            HeroImagePath = heroImage ?? DefaultHeroImagePath
        });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Status(int code)
    {
        if (code != StatusCodes.Status404NotFound)
        {
            return StatusCode(code);
        }

        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("StatusCode", code);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
