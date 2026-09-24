using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Models.Entities;

namespace MilkTeaWeb.Data;

/// <summary>
/// Adds the approved local demo product images for Development.
/// The files are part of the source tree, so the seed does not depend on network access at runtime.
/// </summary>
public sealed class DemoProductImageSeeder(
    ApplicationDbContext context,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DemoProductImageSeeder> logger)
{
    private const string DemoDataEnabledKey = "DemoData:Enabled";

    private static readonly IReadOnlyList<DemoCategoryImageSet> ImageSets =
    [
        new(
            "Cà phê",
            "coffee",
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Bạc xỉu"] = ["bac-xiu-1.jpg", "bac-xiu-2.jpg", "bac-xiu-3.jpg", "bac-xiu-4.jpg"],
                ["Phin sữa đá"] = ["phin-sua-da-1.jpg", "phin-sua-da-2.jpg", "phin-sua-da-3.jpg", "phin-sua-da-4.jpg"],
                ["Phin đen đá"] = ["phin-den-da-1.jpg", "phin-den-da-2.jpg", "phin-den-da-3.jpg", "phin-den-da-4.jpg"],
                ["Cold Brew sữa"] = ["cold-brew-sua-2.jpg", "cold-brew-sua-1.jpg", "cold-brew-sua-3.jpg", "cold-brew-sua-4.jpg"]
            }),
        new(
            "Đồ uống đặc biệt",
            "special",
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Matcha latte"] = ["matcha-1.jpg", "matcha-2.jpg", "matcha-3.jpg", "matcha-4.jpg"],
                ["Sữa ca cao"] = ["cacao-1.jpg", "cacao-2.jpg", "cacao-3.jpg", "cacao-4.jpg"],
                ["Sữa chua dâu"] = ["yogurt-1.jpg", "yogurt-2.jpg", "yogurt-3.jpg", "yogurt-4.jpg"],
                ["Sữa chua xoài"] = ["mango-1.jpg", "mango-2.jpg", "mango-3.jpg", "mango-4.jpg"],
                ["Sữa tươi trân châu đường đen"] = ["milk-tea-1.jpg", "milk-tea-2.jpg", "milk-tea-3.jpg", "milk-tea-4.jpg"]
            }),
        new(
            "Trà nguyên vị",
            "tea",
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Hồng trà nguyên vị"] = ["black-new-1.jpg", "black-new-2.jpg", "black-new-3.jpg", "black-new-4.jpg"],
                ["Trà xanh nhài"] = ["jasmine-new-1.jpg", "jasmine-new-2.jpg", "jasmine-new-3.jpg", "jasmine-new-4.jpg"],
                ["Ô long rang"] = ["roasted-new-1.jpg", "roasted-new-2.jpg", "roasted-new-3.jpg", "roasted-new-4.jpg"],
                ["Ô long kem cheese"] = ["cream-new-1.jpg", "cream-new-2.jpg", "cream-new-3.jpg", "cream-new-4.jpg"]
            }),
        new(
            "Trà sữa",
            "milk-tea",
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Trà sữa truyền thống"] = ["traditional-new-1.jpg", "traditional-new-2.jpg", "traditional-new-3.jpg", "traditional-new-4.jpg"],
                ["Trà sữa trân châu đường đen"] = ["brown-sugar-new-1.jpg", "brown-sugar-new-2.jpg", "brown-sugar-new-3.jpg", "brown-sugar-new-4.jpg"],
                ["Trà sữa ô long"] = ["oolong-new-1.jpg", "oolong-new-2.jpg", "oolong-new-3.jpg", "oolong-new-4.jpg"],
                ["Trà sữa matcha"] = ["matcha-new-1.jpg", "matcha-new-2.jpg", "matcha-new-3.jpg", "matcha-new-4.jpg"],
                ["Trà sữa khoai môn"] = ["taro-new-1.jpg", "taro-new-2.jpg", "taro-new-3.png", "taro-new-4.jpg"],
                ["Trà sữa socola"] = ["choco-new-1.jpg", "choco-new-2.jpg", "choco-new-3.jpg", "choco-new-4.jpg"]
            }),
        new(
            "Trà trái cây",
            "fruit-tea",
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["Trà đào cam sả"] = ["peach-lemongrass-new-1.jpg", "peach-lemongrass-new-2.jpg", "peach-lemongrass-new-3.jpg", "peach-lemongrass-new-4.jpg"],
                ["Trà vải"] = ["lychee-new-1.jpg", "lychee-new-2.jpg", "lychee-new-3.jpg", "lychee-new-4.jpg"],
                ["Trà chanh mật ong"] = ["lemon-honey-new-1.jpg", "lemon-honey-new-2.jpg", "lemon-honey-new-3.jpg", "lemon-honey-new-4.jpg"],
                ["Trà dâu hibiscus"] = ["strawberry-hibiscus-new-1.jpg", "strawberry-hibiscus-new-2.jpg", "strawberry-hibiscus-new-3.jpg", "strawberry-hibiscus-new-4.jpg"],
                ["Trà xoài nhiệt đới"] = ["mango-1.jpg", "mango-2.jpg", "mango-3.jpg", "mango-4.jpg"],
                ["Trà chanh dây"] = ["passionfruit-new-1.jpg", "passionfruit-new-2.jpg", "passionfruit-new-3.jpg", "passionfruit-new-4.jpg"]
            })
    ];

    public async Task SeedIfEligibleAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            logger.LogInformation("Demo product image seeding was skipped because the environment is not Development.");
            return;
        }

        if (!configuration.GetValue<bool>(DemoDataEnabledKey))
        {
            logger.LogInformation("Demo product image seeding was skipped because DemoData:Enabled is false.");
            return;
        }

        var webRootPath = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            throw new InvalidOperationException("The web root path is required for demo product image seeding.");
        }

        var insertedCount = 0;
        var seededProductCount = 0;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var imageSet in ImageSets)
            {
                var products = await context.Products
                    .AsNoTracking()
                    .Include(product => product.Category)
                    .Where(product => product.Category.Name == imageSet.CategoryName
                        && imageSet.ProductImages.Keys.Contains(product.Name))
                    .ToDictionaryAsync(product => product.Name, StringComparer.Ordinal, cancellationToken);

                if (products.Count == 0)
                {
                    logger.LogInformation(
                        "Demo images for category {CategoryName} were skipped because no approved products were found.",
                        imageSet.CategoryName);
                    continue;
                }

                var missingProducts = imageSet.ProductImages.Keys
                    .Where(name => !products.ContainsKey(name))
                    .ToArray();
                if (missingProducts.Length > 0)
                {
                    logger.LogWarning(
                        "Demo images for category {CategoryName} were skipped because approved products are missing: {ProductNames}.",
                        imageSet.CategoryName,
                        string.Join(", ", missingProducts));
                    continue;
                }

                var missingFiles = imageSet.ProductImages.Values
                    .SelectMany(paths => paths)
                    .Distinct(StringComparer.Ordinal)
                    .Where(fileName => !File.Exists(Path.Combine(webRootPath, "images", "demo-products", imageSet.DirectoryName, fileName)))
                    .ToArray();
                if (missingFiles.Length > 0)
                {
                    throw new InvalidOperationException(
                        $"Demo image assets for {imageSet.CategoryName} are missing from wwwroot: {string.Join(", ", missingFiles)}.");
                }

                foreach (var (productName, imageNames) in imageSet.ProductImages)
                {
                    var product = products[productName];
                    var hasImages = await context.ProductImages
                        .AnyAsync(image => image.ProductId == product.ProductId, cancellationToken);
                    if (hasImages)
                    {
                        continue;
                    }

                    context.ProductImages.AddRange(imageNames.Select((fileName, index) => new ProductImage
                    {
                        ProductId = product.ProductId,
                        ImagePath = $"/images/demo-products/{imageSet.DirectoryName}/{fileName}",
                        DisplayOrder = index
                    }));
                    insertedCount += imageNames.Length;
                    seededProductCount++;
                }
            }

            if (insertedCount == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Demo product image seeding was skipped because all approved products already have images.");
                return;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Demo product images seeded successfully: {ImageCount} images for {ProductCount} products.",
                insertedCount,
                seededProductCount);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Demo product image seeding failed and was rolled back.");
            throw;
        }
    }

    private sealed record DemoCategoryImageSet(
        string CategoryName,
        string DirectoryName,
        IReadOnlyDictionary<string, string[]> ProductImages);
}
