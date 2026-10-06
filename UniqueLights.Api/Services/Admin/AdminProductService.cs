using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Repositories;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services.Admin;

public interface IAdminProductService
{
    Task<PagedResult<AdminProductListItemDto>> SearchAsync(AdminProductQuery query, CancellationToken ct);
    Task<AdminProductDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<AdminProductDetailDto> CreateAsync(UpsertProductRequest request, CancellationToken ct);
    Task<AdminProductDetailDto> UpdateAsync(Guid id, UpsertProductRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);

    Task<AdminProductDetailDto> AddVariantAsync(Guid productId, UpsertVariantRequest request, CancellationToken ct);
    Task<AdminProductDetailDto> UpdateVariantAsync(Guid productId, Guid variantId, UpsertVariantRequest request, CancellationToken ct);
    Task<AdminProductDetailDto> DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct);

    Task<AdminProductDetailDto> UploadImageAsync(Guid productId, UploadImageRequest request, CancellationToken ct);
    Task<AdminProductDetailDto> AddImageUrlAsync(Guid productId, AddImageUrlRequest request, CancellationToken ct);
    Task<AdminProductDetailDto> UpdateImageAsync(Guid productId, Guid imageId, UpdateImageRequest request, CancellationToken ct);
    Task<AdminProductDetailDto> DeleteImageAsync(Guid productId, Guid imageId, CancellationToken ct);
}

public sealed class AdminProductService(
    AppDbContext db,
    IProductRepository products,
    IStorageService storage,
    ILogger<AdminProductService> logger) : IAdminProductService
{
    private const int MaxImagesPerProduct = 12;
    private const int MaxVariantsPerProduct = 50;

    public Task<PagedResult<AdminProductListItemDto>> SearchAsync(AdminProductQuery query, CancellationToken ct) =>
        products.SearchAdminAsync(query, ct);

    public async Task<AdminProductDetailDto> GetAsync(Guid id, CancellationToken ct) =>
        (await LoadAsync(id, ct)).ToAdminDetailDto();

    public async Task<AdminProductDetailDto> CreateAsync(UpsertProductRequest request, CancellationToken ct)
    {
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        var sku = request.Sku.Trim();
        var slug = await ResolveSlugAsync(request.Slug, request.Name, null, ct);
        if (await products.ProductSkuExistsAsync(sku, null, ct)) throw new ConflictException($"A product with SKU {sku} already exists");
        if (await products.VariantSkuExistsAsync(sku, null, ct)) throw new ConflictException($"A variant with SKU {sku} already exists");

        var product = new Product();
        Apply(product, request, slug, sku);

        // Every product needs at least one purchasable variant: create the default "Standard" variant.
        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = sku,
            Name = "Standard",
            Price = request.BasePrice,
            CompareAtPrice = request.CompareAtPrice,
            IsDefault = true,
            IsActive = true,
            Inventory = new Inventory { QuantityOnHand = 0, LowStockThreshold = 5 }
        };
        product.Variants.Add(variant);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Product {ProductId} ({Sku}) created", product.Id, product.Sku);
        return await GetAsync(product.Id, ct);
    }

    public async Task<AdminProductDetailDto> UpdateAsync(Guid id, UpsertProductRequest request, CancellationToken ct)
    {
        var product = await LoadAsync(id, ct);
        if (request.CategoryId != product.CategoryId) await EnsureCategoryExistsAsync(request.CategoryId, ct);

        var sku = request.Sku.Trim();
        if (!string.Equals(sku, product.Sku, StringComparison.Ordinal) && await products.ProductSkuExistsAsync(sku, id, ct))
        {
            throw new ConflictException($"A product with SKU {sku} already exists");
        }

        var slug = await ResolveSlugAsync(request.Slug, request.Name, id, ct, currentSlug: product.Slug);
        Apply(product, request, slug, sku);
        await db.SaveChangesAsync(ct);
        return product.ToAdminDetailDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await LoadAsync(id, ct);
        var now = DateTime.UtcNow;
        product.MarkDeleted();
        product.IsActive = false;
        foreach (var variant in product.Variants.Where(v => !v.IsDeleted))
        {
            variant.IsDeleted = true;
            variant.DeletedAt = now;
            variant.IsActive = false;
        }

        // Remove the product from carts and wishlists so customers don't see dead lines.
        var variantIds = product.Variants.Select(v => v.Id).ToList();
        await db.CartItems.Where(ci => variantIds.Contains(ci.VariantId)).ExecuteDeleteAsync(ct);
        await db.WishlistItems.Where(w => w.ProductId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Product {ProductId} soft deleted", id);
    }

    // ---------- Variants ----------

    public async Task<AdminProductDetailDto> AddVariantAsync(Guid productId, UpsertVariantRequest request, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        var live = product.Variants.Where(v => !v.IsDeleted).ToList();
        if (live.Count >= MaxVariantsPerProduct) throw new BusinessRuleException($"A product can have at most {MaxVariantsPerProduct} variants");

        var sku = request.Sku.Trim();
        if (await products.VariantSkuExistsAsync(sku, null, ct)) throw new ConflictException($"A variant with SKU {sku} already exists");

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Inventory = new Inventory
            {
                QuantityOnHand = request.InitialStock ?? 0,
                LowStockThreshold = request.LowStockThreshold ?? 5
            }
        };
        ApplyVariant(variant, request, sku);
        product.Variants.Add(variant);

        if (variant.IsDefault || live.All(v => !v.IsDefault))
        {
            SetDefault(product, variant);
        }

        await db.SaveChangesAsync(ct);
        return product.ToAdminDetailDto();
    }

    public async Task<AdminProductDetailDto> UpdateVariantAsync(Guid productId, Guid variantId, UpsertVariantRequest request, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        var variant = product.Variants.FirstOrDefault(v => v.Id == variantId && !v.IsDeleted)
                      ?? throw new NotFoundException("Variant not found");

        var sku = request.Sku.Trim();
        if (!string.Equals(sku, variant.Sku, StringComparison.Ordinal) && await products.VariantSkuExistsAsync(sku, variantId, ct))
        {
            throw new ConflictException($"A variant with SKU {sku} already exists");
        }

        ApplyVariant(variant, request, sku);
        variant.Inventory ??= new Inventory { VariantId = variant.Id };
        if (request.LowStockThreshold.HasValue) variant.Inventory.LowStockThreshold = request.LowStockThreshold.Value;
        // initialStock is only used on creation; stock changes go through PATCH /api/admin/inventory/{variantId}.

        if (request.IsDefault) SetDefault(product, variant);
        EnsureDefaultExists(product);

        await db.SaveChangesAsync(ct);
        return product.ToAdminDetailDto();
    }

    public async Task<AdminProductDetailDto> DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        var variant = product.Variants.FirstOrDefault(v => v.Id == variantId && !v.IsDeleted)
                      ?? throw new NotFoundException("Variant not found");

        if (product.Variants.Count(v => !v.IsDeleted) <= 1)
        {
            throw new BusinessRuleException("A product must have at least one variant");
        }

        variant.MarkDeleted();
        variant.IsActive = false;
        variant.IsDefault = false;
        EnsureDefaultExists(product);

        await db.CartItems.Where(ci => ci.VariantId == variantId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return product.ToAdminDetailDto();
    }

    // ---------- Images ----------

    public async Task<AdminProductDetailDto> UploadImageAsync(Guid productId, UploadImageRequest request, CancellationToken ct)
    {
        if (!storage.IsConfigured) throw new ServiceUnavailableException("Image storage is not configured");
        var file = request.File ?? throw new ValidationException("file", "An image file is required.");

        var product = await LoadAsync(productId, ct);
        EnsureImageCapacity(product);

        var (extension, contentType) = await ImageFileValidator.ValidateAsync(file, ct);
        var path = $"products/{product.Id}/{Guid.NewGuid():N}{extension}";

        StoredFile stored;
        await using (var stream = file.OpenReadStream())
        {
            stored = await storage.UploadAsync(path, stream, contentType, ct);
        }

        AddImage(product, stored.PublicUrl, stored.Path, request.AltText, request.IsPrimary);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Don't leave orphaned objects in the bucket if the DB write fails.
            await TryDeleteObjectAsync(stored.Path);
            throw;
        }

        return product.ToAdminDetailDto();
    }

    public async Task<AdminProductDetailDto> AddImageUrlAsync(Guid productId, AddImageUrlRequest request, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        EnsureImageCapacity(product);
        AddImage(product, request.Url.Trim(), null, request.AltText, request.IsPrimary);
        await db.SaveChangesAsync(ct);
        return product.ToAdminDetailDto();
    }

    public async Task<AdminProductDetailDto> UpdateImageAsync(Guid productId, Guid imageId, UpdateImageRequest request, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        var image = product.Images.FirstOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("Image not found");

        image.AltText = string.IsNullOrWhiteSpace(request.AltText) ? null : request.AltText.Trim();
        image.SortOrder = request.SortOrder;

        if (request.IsPrimary)
        {
            foreach (var i in product.Images) i.IsPrimary = i.Id == image.Id;
        }
        else if (image.IsPrimary)
        {
            image.IsPrimary = false;
            EnsurePrimaryImage(product);
        }

        await db.SaveChangesAsync(ct);
        return product.ToAdminDetailDto();
    }

    public async Task<AdminProductDetailDto> DeleteImageAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        var product = await LoadAsync(productId, ct);
        var image = product.Images.FirstOrDefault(i => i.Id == imageId) ?? throw new NotFoundException("Image not found");

        product.Images.Remove(image);
        db.ProductImages.Remove(image);
        if (image.IsPrimary) EnsurePrimaryImage(product);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(image.StoragePath))
        {
            await TryDeleteObjectAsync(image.StoragePath);
        }

        return product.ToAdminDetailDto();
    }

    // ---------- Helpers ----------

    private async Task<Product> LoadAsync(Guid id, CancellationToken ct) =>
        await products.GetForAdminAsync(id, ct) ?? throw new NotFoundException("Product not found");

    private async Task EnsureCategoryExistsAsync(Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new ValidationException("categoryId", "Category does not exist.");
        }
    }

    private async Task<string> ResolveSlugAsync(string? requested, string name, Guid? productId, CancellationToken ct, string? currentSlug = null)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var slug = requested.Trim().ToLowerInvariant();
            if (slug != currentSlug && await products.SlugExistsAsync(slug, productId, ct))
            {
                throw new ConflictException($"A product with slug '{slug}' already exists");
            }
            return slug;
        }

        // Keep the existing slug on update when none is supplied (stable URLs).
        if (currentSlug is not null) return currentSlug;

        var baseSlug = SlugHelper.Slugify(name);
        var candidate = baseSlug;
        for (var i = 2; await products.SlugExistsAsync(candidate, productId, ct); i++)
        {
            candidate = $"{baseSlug}-{i}";
        }
        return candidate;
    }

    private static void Apply(Product product, UpsertProductRequest r, string slug, string sku)
    {
        product.Name = r.Name.Trim();
        product.Slug = slug;
        product.Sku = sku;
        product.ShortDescription = string.IsNullOrWhiteSpace(r.ShortDescription) ? null : r.ShortDescription.Trim();
        product.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        product.Brand = string.IsNullOrWhiteSpace(r.Brand) ? null : r.Brand.Trim();
        product.BasePrice = r.BasePrice;
        product.CompareAtPrice = r.CompareAtPrice;
        product.CategoryId = r.CategoryId;
        product.IsActive = r.IsActive;
        product.IsFeatured = r.IsFeatured;
        product.IsNew = r.IsNew;
        product.Specifications = (r.Specifications ?? new Dictionary<string, string>())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value?.Trim() ?? string.Empty);
    }

    private static void ApplyVariant(ProductVariant v, UpsertVariantRequest r, string sku)
    {
        v.Sku = sku;
        v.Name = r.Name.Trim();
        v.Color = string.IsNullOrWhiteSpace(r.Color) ? null : r.Color.Trim();
        v.Finish = string.IsNullOrWhiteSpace(r.Finish) ? null : r.Finish.Trim();
        v.ColorTemperature = string.IsNullOrWhiteSpace(r.ColorTemperature) ? null : r.ColorTemperature.Trim();
        v.Wattage = r.Wattage;
        v.Price = r.Price;
        v.CompareAtPrice = r.CompareAtPrice;
        v.IsActive = r.IsActive;
        v.IsDefault = r.IsDefault;
    }

    private static void SetDefault(Product product, ProductVariant target)
    {
        foreach (var v in product.Variants.Where(v => !v.IsDeleted)) v.IsDefault = v == target;
    }

    private static void EnsureDefaultExists(Product product)
    {
        var live = product.Variants.Where(v => !v.IsDeleted).ToList();
        if (live.Count == 0 || live.Any(v => v.IsDefault)) return;
        var next = live.Where(v => v.IsActive).OrderBy(v => v.Price).FirstOrDefault() ?? live.OrderBy(v => v.Price).First();
        next.IsDefault = true;
    }

    private static void EnsureImageCapacity(Product product)
    {
        if (product.Images.Count >= MaxImagesPerProduct)
        {
            throw new BusinessRuleException($"A product can have at most {MaxImagesPerProduct} images");
        }
    }

    private void AddImage(Product product, string url, string? storagePath, string? altText, bool isPrimary)
    {
        var makePrimary = isPrimary || product.Images.Count == 0;
        if (makePrimary)
        {
            foreach (var i in product.Images) i.IsPrimary = false;
        }

        var image = new ProductImage
        {
            ProductId = product.Id,
            Url = url,
            StoragePath = storagePath,
            AltText = string.IsNullOrWhiteSpace(altText) ? product.Name : altText.Trim(),
            SortOrder = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1,
            IsPrimary = makePrimary
        };
        product.Images.Add(image);
        db.ProductImages.Add(image);
    }

    private static void EnsurePrimaryImage(Product product)
    {
        if (product.Images.Count == 0 || product.Images.Any(i => i.IsPrimary)) return;
        product.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAt).First().IsPrimary = true;
    }

    private async Task TryDeleteObjectAsync(string path)
    {
        if (!storage.IsConfigured) return;
        try
        {
            await storage.DeleteAsync(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete storage object {Path}", path);
        }
    }
}
