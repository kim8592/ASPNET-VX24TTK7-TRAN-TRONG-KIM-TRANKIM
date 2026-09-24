using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Entities;

namespace MilkTeaWeb.Services;

public sealed class ProductImageService(
    ApplicationDbContext context,
    IWebHostEnvironment environment,
    ILogger<ProductImageService> logger)
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp"
    };

    public async Task<ProductImage> UploadAsync(int productId, IFormFile file, CancellationToken cancellationToken = default)
    {
        var extension = Validate(file);
        var relativePath = $"/uploads/products/{productId}/{Guid.NewGuid():N}{extension}";
        var physicalPath = GetManagedPhysicalPath(relativePath, productId)
            ?? throw new InvalidOperationException("Unable to resolve managed product image path.");
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        try
        {
            await using (var destination = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(destination, cancellationToken);
            }

            var displayOrder = (await context.ProductImages
                .Where(image => image.ProductId == productId)
                .Select(image => (int?)image.DisplayOrder)
                .MaxAsync(cancellationToken) ?? -1) + 1;
            var image = new ProductImage { ProductId = productId, ImagePath = relativePath, DisplayOrder = displayOrder };
            context.ProductImages.Add(image);
            await context.SaveChangesAsync(cancellationToken);
            return image;
        }
        catch
        {
            TryDeletePhysicalFile(physicalPath);
            throw;
        }
    }

    public async Task<bool> DeleteManagedFileAsync(ProductImage image, CancellationToken cancellationToken = default)
    {
        var physicalPath = GetManagedPhysicalPath(image.ImagePath, image.ProductId, false);
        if (physicalPath is null || !File.Exists(physicalPath)) return true;
        try { await Task.Run(() => File.Delete(physicalPath), cancellationToken); return true; }
        catch (Exception exception) { logger.LogWarning(exception, "Unable to delete managed product image file after database deletion."); return false; }
    }

    private static string Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0) throw new ProductImageValidationException("Tệp ảnh không hợp lệ hoặc đang trống.");
        if (file.Length > MaxFileSizeBytes) throw new ProductImageValidationException("Ảnh không được lớn hơn 5 MB.");
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedTypes.TryGetValue(extension, out var expectedType) || !string.Equals(file.ContentType, expectedType, StringComparison.OrdinalIgnoreCase))
            throw new ProductImageValidationException("Chỉ chấp nhận ảnh JPG, JPEG, PNG hoặc WEBP.");
        using var stream = file.OpenReadStream();
        Span<byte> header = stackalloc byte[12];
        var count = stream.Read(header);
        var valid = extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? count >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            : extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                ? count >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)
                : count >= 3 && header[..3].SequenceEqual(new byte[] { 255, 216, 255 });
        if (!valid) throw new ProductImageValidationException("Nội dung tệp không khớp với định dạng ảnh được chọn.");
        return extension.ToLowerInvariant();
    }

    private string? GetManagedPhysicalPath(string relativePath, int productId, bool throwWhenInvalid = true)
    {
        var expectedPrefix = $"/uploads/products/{productId}/";
        if (!relativePath.StartsWith(expectedPrefix, StringComparison.Ordinal) || relativePath.Contains("..", StringComparison.Ordinal))
        {
            if (throwWhenInvalid) throw new InvalidOperationException("Invalid managed product image path.");
            return null;
        }
        var webRoot = environment.WebRootPath ?? throw new InvalidOperationException("Web root path is unavailable.");
        var root = Path.GetFullPath(Path.Combine(webRoot, "uploads", "products", productId.ToString()));
        var file = Path.GetFullPath(Path.Combine(webRoot, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? file : null;
    }

    private void TryDeletePhysicalFile(string physicalPath)
    {
        try { if (File.Exists(physicalPath)) File.Delete(physicalPath); }
        catch (Exception exception) { logger.LogWarning(exception, "Unable to clean up product image after failed persistence."); }
    }
}

public sealed class ProductImageValidationException(string message) : Exception(message);
