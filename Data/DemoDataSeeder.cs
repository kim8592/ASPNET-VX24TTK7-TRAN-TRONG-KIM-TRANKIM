using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Models.Entities;

namespace MilkTeaWeb.Data;

/// <summary>
/// Creates the approved catalog sample only for an explicitly enabled Development environment.
/// </summary>
public sealed class DemoDataSeeder(
    ApplicationDbContext context,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DemoDataSeeder> logger)
{
    private const string DemoDataEnabledKey = "DemoData:Enabled";
    private static readonly int[] AllLevels = [0, 30, 50, 70, 100];
    private static readonly int[] TeaSugarLevels = [0, 30, 50, 70];
    private static readonly int[] ColdIceLevels = [30, 50, 70, 100];

    public async Task SeedIfEligibleAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            logger.LogInformation("Demo catalog seeding was skipped because the environment is not Development.");
            return;
        }

        if (!configuration.GetValue<bool>(DemoDataEnabledKey))
        {
            logger.LogInformation("Demo catalog seeding was skipped because DemoData:Enabled is false.");
            return;
        }

        if (await HasExistingCatalogDataAsync(cancellationToken))
        {
            logger.LogInformation("Demo catalog seeding was skipped because business catalog data already exists.");
            return;
        }

        var sugarLevels = await GetSugarLevelsAsync(cancellationToken);
        var iceLevels = await GetIceLevelsAsync(cancellationToken);
        var catalog = BuildApprovedCatalog(sugarLevels, iceLevels);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            context.Categories.AddRange(catalog.Categories.Values);
            context.Sizes.AddRange(catalog.Sizes.Values);
            context.Toppings.AddRange(catalog.Toppings.Values);
            context.Stores.AddRange(catalog.Stores);
            context.Products.AddRange(catalog.Products);
            context.ProductSizes.AddRange(catalog.ProductSizes);
            context.ProductToppings.AddRange(catalog.ProductToppings);
            context.ProductSugarLevels.AddRange(catalog.ProductSugarLevels);
            context.ProductIceLevels.AddRange(catalog.ProductIceLevels);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation(
                "Demo catalog seeded successfully: {CategoryCount} categories, {ProductCount} products, {SizeCount} sizes, {ToppingCount} toppings, {StoreCount} stores, {ProductSizeCount} product sizes, {ProductToppingCount} product toppings, {ProductSugarLevelCount} product sugar levels, {ProductIceLevelCount} product ice levels.",
                catalog.Categories.Count,
                catalog.Products.Count,
                catalog.Sizes.Count,
                catalog.Toppings.Count,
                catalog.Stores.Count,
                catalog.ProductSizes.Count,
                catalog.ProductToppings.Count,
                catalog.ProductSugarLevels.Count,
                catalog.ProductIceLevels.Count);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Demo catalog seeding failed and was rolled back.");
            throw;
        }
    }

    private async Task<bool> HasExistingCatalogDataAsync(CancellationToken cancellationToken) =>
        await context.Categories.AnyAsync(cancellationToken)
        || await context.Products.AnyAsync(cancellationToken)
        || await context.Sizes.AnyAsync(cancellationToken)
        || await context.Toppings.AnyAsync(cancellationToken)
        || await context.Stores.AnyAsync(cancellationToken)
        || await context.ProductSizes.AnyAsync(cancellationToken)
        || await context.ProductToppings.AnyAsync(cancellationToken)
        || await context.ProductSugarLevels.AnyAsync(cancellationToken)
        || await context.ProductIceLevels.AnyAsync(cancellationToken);

    private async Task<Dictionary<int, SugarLevel>> GetSugarLevelsAsync(CancellationToken cancellationToken)
    {
        var levels = await context.SugarLevels
            .Where(level => AllLevels.Contains(level.Percentage))
            .ToDictionaryAsync(level => level.Percentage, cancellationToken);

        if (levels.Count != AllLevels.Length || AllLevels.Any(value => !levels.ContainsKey(value)))
        {
            throw new InvalidOperationException("Required SugarLevel reference values are missing.");
        }

        return levels;
    }

    private async Task<Dictionary<int, IceLevel>> GetIceLevelsAsync(CancellationToken cancellationToken)
    {
        var levels = await context.IceLevels
            .Where(level => AllLevels.Contains(level.Percentage))
            .ToDictionaryAsync(level => level.Percentage, cancellationToken);

        if (levels.Count != AllLevels.Length || AllLevels.Any(value => !levels.ContainsKey(value)))
        {
            throw new InvalidOperationException("Required IceLevel reference values are missing.");
        }

        return levels;
    }

    private static ApprovedCatalog BuildApprovedCatalog(
        IReadOnlyDictionary<int, SugarLevel> sugarLevels,
        IReadOnlyDictionary<int, IceLevel> iceLevels)
    {
        var categories = new Dictionary<string, Category>(StringComparer.Ordinal)
        {
            ["Trà sữa"] = new() { Name = "Trà sữa", IsActive = true },
            ["Trà trái cây"] = new() { Name = "Trà trái cây", IsActive = true },
            ["Trà nguyên vị"] = new() { Name = "Trà nguyên vị", IsActive = true },
            ["Cà phê"] = new() { Name = "Cà phê", IsActive = true },
            ["Đồ uống đặc biệt"] = new() { Name = "Đồ uống đặc biệt", IsActive = true }
        };

        var sizes = new Dictionary<string, Size>(StringComparer.Ordinal)
        {
            ["M"] = new() { Name = "M", DisplayOrder = 0 },
            ["L"] = new() { Name = "L", DisplayOrder = 1 },
            ["XL"] = new() { Name = "XL", DisplayOrder = 2 }
        };

        var toppings = new Dictionary<string, Topping>(StringComparer.Ordinal)
        {
            ["Trân châu đen"] = new() { Name = "Trân châu đen", Price = 7_000m, IsActive = true },
            ["Trân châu trắng"] = new() { Name = "Trân châu trắng", Price = 8_000m, IsActive = true },
            ["Pudding trứng"] = new() { Name = "Pudding trứng", Price = 8_000m, IsActive = true },
            ["Thạch cỏ"] = new() { Name = "Thạch cỏ", Price = 7_000m, IsActive = true },
            ["Nha đam"] = new() { Name = "Nha đam", Price = 7_000m, IsActive = true },
            ["Thạch trái cây"] = new() { Name = "Thạch trái cây", Price = 8_000m, IsActive = true },
            ["Kem cheese"] = new() { Name = "Kem cheese", Price = 10_000m, IsActive = true },
            ["Đậu đỏ"] = new() { Name = "Đậu đỏ", Price = 8_000m, IsActive = true },
            ["Thạch cà phê"] = new() { Name = "Thạch cà phê", Price = 8_000m, IsActive = true },
            ["Hạt chia"] = new() { Name = "Hạt chia", Price = 5_000m, IsActive = false }
        };

        var stores = new List<Store>
        {
            new() { Name = "Chi nhánh Trung Tâm", Address = "12 Đường Hoa Trà, TP. Hồ Chí Minh", Phone = "028 7300 1001", IsActive = true },
            new() { Name = "Chi nhánh Khu Đại Học", Address = "28 Đường Lá Trà, TP. Hồ Chí Minh", Phone = "028 7300 1002", IsActive = true },
            new() { Name = "Chi nhánh Riverside", Address = "46 Đường Hương Trà, TP. Hồ Chí Minh", Phone = "028 7300 1003", IsActive = false }
        };

        var products = new List<Product>();
        var productSizes = new List<ProductSize>();
        var productToppings = new List<ProductTopping>();
        var productSugarLevels = new List<ProductSugarLevel>();
        var productIceLevels = new List<ProductIceLevel>();

        void AddProduct(
            string category,
            string name,
            string description,
            (string Size, decimal Price)[] prices,
            int[] sugar,
            int[] ice,
            string[] productToppingNames)
        {
            var product = new Product
            {
                Category = categories[category],
                Name = name,
                Description = description,
                IsAvailable = true
            };
            products.Add(product);

            productSizes.AddRange(prices.Select(price => new ProductSize
            {
                Product = product,
                Size = sizes[price.Size],
                Price = price.Price
            }));
            productSugarLevels.AddRange(sugar.Select(percentage => new ProductSugarLevel
            {
                Product = product,
                SugarLevel = sugarLevels[percentage]
            }));
            productIceLevels.AddRange(ice.Select(percentage => new ProductIceLevel
            {
                Product = product,
                IceLevel = iceLevels[percentage]
            }));
            productToppings.AddRange(productToppingNames.Select(toppingName => new ProductTopping
            {
                Product = product,
                Topping = toppings[toppingName]
            }));
        }

        AddProduct("Trà sữa", "Trà sữa truyền thống", "Vị trà và sữa cân bằng, dễ uống.", [("M", 35_000m), ("L", 42_000m)], AllLevels, AllLevels, ["Trân châu đen", "Trân châu trắng", "Pudding trứng", "Thạch cỏ", "Kem cheese"]);
        AddProduct("Trà sữa", "Trà sữa trân châu đường đen", "Hương đường đen đậm và vị sữa béo.", [("M", 42_000m), ("L", 49_000m)], [], AllLevels, ["Trân châu trắng", "Pudding trứng", "Kem cheese"]);
        AddProduct("Trà sữa", "Trà sữa ô long", "Ô long thơm nhẹ kết hợp sữa.", [("M", 39_000m), ("L", 46_000m)], AllLevels, AllLevels, ["Trân châu đen", "Trân châu trắng", "Pudding trứng", "Kem cheese"]);
        AddProduct("Trà sữa", "Trà sữa matcha", "Matcha thanh nhẹ và vị sữa mềm.", [("M", 42_000m), ("L", 49_000m)], AllLevels, AllLevels, ["Trân châu đen", "Pudding trứng", "Đậu đỏ", "Kem cheese"]);
        AddProduct("Trà sữa", "Trà sữa khoai môn", "Vị khoai môn béo dịu.", [("M", 41_000m), ("L", 48_000m)], AllLevels, AllLevels, ["Trân châu đen", "Pudding trứng", "Đậu đỏ"]);
        AddProduct("Trà sữa", "Trà sữa socola", "Socola đậm vừa, hòa cùng trà sữa.", [("M", 41_000m), ("L", 48_000m)], AllLevels, AllLevels, ["Trân châu đen", "Pudding trứng", "Kem cheese"]);

        AddProduct("Trà trái cây", "Trà đào cam sả", "Đào, cam và sả với hậu vị tươi mát.", [("M", 39_000m), ("L", 46_000m), ("XL", 53_000m)], AllLevels, AllLevels, ["Trân châu trắng", "Nha đam", "Thạch trái cây"]);
        AddProduct("Trà trái cây", "Trà vải", "Trà thanh kết hợp hương vải nhẹ.", [("M", 38_000m), ("L", 45_000m)], AllLevels, AllLevels, ["Trân châu trắng", "Nha đam", "Thạch trái cây"]);
        AddProduct("Trà trái cây", "Trà chanh mật ong", "Chanh chua nhẹ và mật ong dịu.", [("M", 35_000m), ("L", 42_000m)], AllLevels, AllLevels, ["Trân châu trắng", "Nha đam"]);
        AddProduct("Trà trái cây", "Trà dâu hibiscus", "Dâu và hibiscus có vị chua ngọt cân bằng.", [("M", 40_000m), ("L", 47_000m)], AllLevels, AllLevels, ["Trân châu trắng", "Thạch trái cây"]);
        AddProduct("Trà trái cây", "Trà xoài nhiệt đới", "Hương xoài tươi và vị trà mát.", [("M", 42_000m), ("L", 49_000m), ("XL", 56_000m)], AllLevels, AllLevels, ["Trân châu trắng", "Nha đam", "Thạch trái cây"]);
        AddProduct("Trà trái cây", "Trà chanh dây", "Chanh dây thơm, vị chua ngọt rõ.", [("M", 39_000m), ("L", 46_000m), ("XL", 53_000m)], AllLevels, AllLevels, ["Trân châu trắng", "Nha đam", "Thạch trái cây"]);

        AddProduct("Trà nguyên vị", "Hồng trà nguyên vị", "Hồng trà thơm đậm, hậu vị thanh.", [("M", 30_000m), ("L", 36_000m), ("XL", 42_000m)], TeaSugarLevels, AllLevels, ["Trân châu trắng", "Kem cheese"]);
        AddProduct("Trà nguyên vị", "Trà xanh nhài", "Trà xanh nhẹ với hương nhài.", [("M", 30_000m), ("L", 36_000m)], TeaSugarLevels, AllLevels, ["Trân châu trắng", "Nha đam", "Kem cheese"]);
        AddProduct("Trà nguyên vị", "Ô long rang", "Ô long rang thơm và hậu vị sâu.", [("M", 32_000m), ("L", 38_000m)], TeaSugarLevels, AllLevels, ["Trân châu trắng", "Kem cheese"]);
        AddProduct("Trà nguyên vị", "Ô long kem cheese", "Ô long kết hợp lớp kem cheese.", [("M", 40_000m), ("L", 47_000m)], AllLevels, AllLevels, []);

        AddProduct("Cà phê", "Bạc xỉu", "Cà phê nhẹ, sữa béo và dễ uống.", [("M", 35_000m), ("L", 42_000m)], ColdIceLevels, ColdIceLevels, ["Pudding trứng", "Thạch cà phê", "Kem cheese"]);
        AddProduct("Cà phê", "Phin sữa đá", "Cà phê phin và sữa đặc truyền thống.", [("M", 32_000m), ("L", 38_000m)], ColdIceLevels, ColdIceLevels, ["Thạch cà phê", "Kem cheese"]);
        AddProduct("Cà phê", "Phin đen đá", "Cà phê phin đậm và gọn vị.", [("M", 28_000m), ("L", 34_000m)], [0, 30, 50], ColdIceLevels, ["Thạch cà phê"]);
        AddProduct("Cà phê", "Cold Brew sữa", "Cold brew êm dịu kết hợp sữa.", [("M", 39_000m), ("L", 46_000m), ("XL", 53_000m)], TeaSugarLevels, ColdIceLevels, ["Thạch cà phê", "Kem cheese"]);

        AddProduct("Đồ uống đặc biệt", "Sữa tươi trân châu đường đen", "Sữa tươi và sốt đường đen thơm béo.", [("M", 45_000m), ("L", 52_000m)], [], AllLevels, ["Trân châu trắng", "Pudding trứng", "Kem cheese"]);
        AddProduct("Đồ uống đặc biệt", "Matcha latte", "Matcha và sữa với vị thanh béo.", [("M", 42_000m), ("L", 49_000m)], AllLevels, AllLevels, ["Trân châu đen", "Pudding trứng", "Đậu đỏ"]);
        AddProduct("Đồ uống đặc biệt", "Sữa ca cao", "Ca cao đậm vừa kết hợp sữa.", [("M", 40_000m), ("L", 47_000m)], AllLevels, AllLevels, ["Trân châu đen", "Pudding trứng", "Kem cheese"]);
        AddProduct("Đồ uống đặc biệt", "Sữa chua dâu", "Sữa chua kết hợp dâu chua ngọt.", [("M", 42_000m), ("L", 49_000m)], [], [], ["Thạch trái cây"]);
        AddProduct("Đồ uống đặc biệt", "Sữa chua xoài", "Sữa chua và xoài thơm dịu.", [("M", 42_000m), ("L", 49_000m)], [], [], []);

        var catalog = new ApprovedCatalog(categories, sizes, toppings, stores, products, productSizes, productToppings, productSugarLevels, productIceLevels);
        catalog.AssertExpectedCounts();
        return catalog;
    }

    private sealed record ApprovedCatalog(
        Dictionary<string, Category> Categories,
        Dictionary<string, Size> Sizes,
        Dictionary<string, Topping> Toppings,
        List<Store> Stores,
        List<Product> Products,
        List<ProductSize> ProductSizes,
        List<ProductTopping> ProductToppings,
        List<ProductSugarLevel> ProductSugarLevels,
        List<ProductIceLevel> ProductIceLevels)
    {
        public void AssertExpectedCounts()
        {
            if (Categories.Count != 5 || Products.Count != 25 || Sizes.Count != 3 || Toppings.Count != 10 || Stores.Count != 3
                || ProductSizes.Count != 55 || ProductToppings.Count != 63 || ProductSugarLevels.Count != 97 || ProductIceLevels.Count != 111)
            {
                throw new InvalidOperationException("The approved demo catalog definition does not match its required counts.");
            }
        }
    }
}
