using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Catalog;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Repositories;

public sealed class ProductSearchCriteria
{
    public string? Search { get; init; }
    public IReadOnlyCollection<Guid>? CategoryIds { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public IReadOnlyCollection<string> Brands { get; init; } = [];
    public IReadOnlyCollection<string> Finishes { get; init; } = [];
    public IReadOnlyCollection<string> ColorTemperatures { get; init; } = [];
    public bool? InStock { get; init; }
    public bool? Featured { get; init; }
    public bool? IsNew { get; init; }
    public string Sort { get; init; } = ProductSorts.Featured;
    public Guid? ExcludeProductId { get; init; }
}

public interface IProductRepository
{
    /// <summary>Active, non-deleted products in active categories (no tracking).</summary>
    IQueryable<Product> QueryPublic();

    Task<PagedResult<ProductListItemDto>> SearchAsync(ProductSearchCriteria criteria, int page, int pageSize, CancellationToken ct);

    Task<List<ProductListItemDto>> ListAsync(ProductSearchCriteria criteria, int take, CancellationToken ct);

    Task<List<ProductListItemDto>> GetListItemsByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    Task<ProductFiltersDto> GetFiltersAsync(IReadOnlyCollection<Guid>? categoryIds, CancellationToken ct);

    Task<Product?> GetPublicDetailAsync(string slug, CancellationToken ct);

    /// <summary>Tracked product (not deleted, any active state) with images, variants and inventory.</summary>
    Task<Product?> GetForAdminAsync(Guid id, CancellationToken ct);

    Task<PagedResult<AdminProductListItemDto>> SearchAdminAsync(AdminProductQuery query, CancellationToken ct);

    Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken ct);

    Task<bool> ProductSkuExistsAsync(string sku, Guid? excludeId, CancellationToken ct);

    Task<bool> VariantSkuExistsAsync(string sku, Guid? excludeVariantId, CancellationToken ct);
}

public sealed class ProductRepository(AppDbContext db) : IProductRepository
{
    /// <summary>Intermediate row: the product plus SQL-computed "default variant" price data.</summary>
    private sealed class PricedProduct
    {
        public Product P { get; init; } = null!;
        public decimal Price { get; init; }
        public decimal? CompareAt { get; init; }
        public Guid? DefaultVariantId { get; init; }
        public bool InStock { get; init; }
    }

    public IQueryable<Product> QueryPublic() =>
        db.Products.AsNoTracking().Where(p => p.IsActive && p.Category.IsActive);

    private static IQueryable<PricedProduct> WithPricing(IQueryable<Product> query) =>
        query.Select(p => new PricedProduct
        {
            P = p,
            Price = p.Variants
                        .Where(v => v.IsActive)
                        .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Price).ThenBy(v => v.Id)
                        .Select(v => (decimal?)v.Price)
                        .FirstOrDefault() ?? p.BasePrice,
            CompareAt = p.Variants.Any(v => v.IsActive)
                ? p.Variants
                    .Where(v => v.IsActive)
                    .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Price).ThenBy(v => v.Id)
                    .Select(v => v.CompareAtPrice)
                    .FirstOrDefault()
                : p.CompareAtPrice,
            DefaultVariantId = p.Variants
                .Where(v => v.IsActive)
                .OrderByDescending(v => v.IsDefault).ThenBy(v => v.Price).ThenBy(v => v.Id)
                .Select(v => (Guid?)v.Id)
                .FirstOrDefault(),
            InStock = p.Variants.Any(v => v.IsActive && v.Inventory != null &&
                                          v.Inventory.QuantityOnHand - v.Inventory.ReservedQuantity > 0)
        });

    private static IQueryable<ProductListItemDto> ToListItems(IQueryable<PricedProduct> query) =>
        query.Select(x => new ProductListItemDto
        {
            Id = x.P.Id,
            Name = x.P.Name,
            Slug = x.P.Slug,
            Brand = x.P.Brand,
            ShortDescription = x.P.ShortDescription,
            Price = x.Price,
            CompareAtPrice = Pricing.EffectiveCompareAt(x.Price, x.CompareAt),
            DiscountPercent = Pricing.DiscountPercent(x.Price, x.CompareAt),
            PrimaryImage = x.P.Images
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
                .Select(i => new ProductImageDto { Id = i.Id, Url = i.Url, AltText = i.AltText, SortOrder = i.SortOrder, IsPrimary = i.IsPrimary })
                .FirstOrDefault(),
            SecondaryImage = x.P.Images
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
                .Select(i => new ProductImageDto { Id = i.Id, Url = i.Url, AltText = i.AltText, SortOrder = i.SortOrder, IsPrimary = i.IsPrimary })
                .Skip(1)
                .FirstOrDefault(),
            CategoryName = x.P.Category.Name,
            CategorySlug = x.P.Category.Slug,
            AverageRating = x.P.AverageRating,
            ReviewCount = x.P.ReviewCount,
            InStock = x.InStock,
            IsNew = x.P.IsNew,
            IsFeatured = x.P.IsFeatured,
            DefaultVariantId = x.DefaultVariantId
        });

    private IQueryable<PricedProduct> BuildQuery(ProductSearchCriteria c)
    {
        var query = QueryPublic();

        if (c.ExcludeProductId.HasValue)
        {
            var excluded = c.ExcludeProductId.Value;
            query = query.Where(p => p.Id != excluded);
        }

        if (!string.IsNullOrWhiteSpace(c.Search))
        {
            var pattern = SearchPattern.Contains(c.Search);
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern, SearchPattern.EscapeCharacter) ||
                (p.Brand != null && EF.Functions.ILike(p.Brand, pattern, SearchPattern.EscapeCharacter)) ||
                EF.Functions.ILike(p.Sku, pattern, SearchPattern.EscapeCharacter) ||
                (p.ShortDescription != null && EF.Functions.ILike(p.ShortDescription, pattern, SearchPattern.EscapeCharacter)));
        }

        if (c.CategoryIds is not null)
        {
            var ids = c.CategoryIds.ToList();
            query = query.Where(p => ids.Contains(p.CategoryId));
        }

        if (c.Brands.Count > 0)
        {
            var brands = c.Brands.ToList();
            query = query.Where(p => p.Brand != null && brands.Contains(p.Brand));
        }

        if (c.Finishes.Count > 0)
        {
            var finishes = c.Finishes.ToList();
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.Finish != null && finishes.Contains(v.Finish)));
        }

        if (c.ColorTemperatures.Count > 0)
        {
            var temps = c.ColorTemperatures.ToList();
            query = query.Where(p => p.Variants.Any(v => v.IsActive && v.ColorTemperature != null && temps.Contains(v.ColorTemperature)));
        }

        if (c.Featured.HasValue)
        {
            var featured = c.Featured.Value;
            query = query.Where(p => p.IsFeatured == featured);
        }

        if (c.IsNew.HasValue)
        {
            var isNew = c.IsNew.Value;
            query = query.Where(p => p.IsNew == isNew);
        }

        var priced = WithPricing(query);

        if (c.InStock.HasValue)
        {
            var inStock = c.InStock.Value;
            priced = priced.Where(x => x.InStock == inStock);
        }

        if (c.MinPrice.HasValue)
        {
            var min = c.MinPrice.Value;
            priced = priced.Where(x => x.Price >= min);
        }

        if (c.MaxPrice.HasValue)
        {
            var max = c.MaxPrice.Value;
            priced = priced.Where(x => x.Price <= max);
        }

        return c.Sort switch
        {
            ProductSorts.Newest => priced.OrderByDescending(x => x.P.IsNew).ThenByDescending(x => x.P.CreatedAt).ThenBy(x => x.P.Id),
            ProductSorts.PriceAsc => priced.OrderBy(x => x.Price).ThenBy(x => x.P.Name).ThenBy(x => x.P.Id),
            ProductSorts.PriceDesc => priced.OrderByDescending(x => x.Price).ThenBy(x => x.P.Name).ThenBy(x => x.P.Id),
            ProductSorts.Rating => priced.OrderByDescending(x => x.P.AverageRating).ThenByDescending(x => x.P.ReviewCount).ThenBy(x => x.P.Id),
            ProductSorts.Name => priced.OrderBy(x => x.P.Name).ThenBy(x => x.P.Id),
            _ => priced.OrderByDescending(x => x.P.IsFeatured).ThenByDescending(x => x.P.AverageRating)
                .ThenByDescending(x => x.P.CreatedAt).ThenBy(x => x.P.Id)
        };
    }

    public async Task<PagedResult<ProductListItemDto>> SearchAsync(ProductSearchCriteria criteria, int page, int pageSize, CancellationToken ct)
    {
        var query = BuildQuery(criteria);
        var total = await query.CountAsync(ct);
        var items = total == 0
            ? []
            : await ToListItems(query.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return PagedResult<ProductListItemDto>.Create(items, page, pageSize, total);
    }

    public Task<List<ProductListItemDto>> ListAsync(ProductSearchCriteria criteria, int take, CancellationToken ct) =>
        ToListItems(BuildQuery(criteria).Take(take)).ToListAsync(ct);

    public async Task<List<ProductListItemDto>> GetListItemsByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return [];
        var ids = productIds.ToList();
        return await ToListItems(WithPricing(QueryPublic().Where(p => ids.Contains(p.Id)))).ToListAsync(ct);
    }

    public async Task<ProductFiltersDto> GetFiltersAsync(IReadOnlyCollection<Guid>? categoryIds, CancellationToken ct)
    {
        var products = QueryPublic();
        if (categoryIds is not null)
        {
            var ids = categoryIds.ToList();
            products = products.Where(p => ids.Contains(p.CategoryId));
        }

        var brands = await products.Where(p => p.Brand != null && p.Brand != "")
            .Select(p => p.Brand!).Distinct().OrderBy(b => b).ToListAsync(ct);

        var variants = products.SelectMany(p => p.Variants).Where(v => v.IsActive);
        var finishes = await variants.Where(v => v.Finish != null && v.Finish != "")
            .Select(v => v.Finish!).Distinct().OrderBy(f => f).ToListAsync(ct);
        var temperatures = await variants.Where(v => v.ColorTemperature != null && v.ColorTemperature != "")
            .Select(v => v.ColorTemperature!).Distinct().OrderBy(t => t).ToListAsync(ct);

        var prices = WithPricing(products).Select(x => x.Price);
        var minPrice = await prices.Select(p => (decimal?)p).MinAsync(ct) ?? 0m;
        var maxPrice = await prices.Select(p => (decimal?)p).MaxAsync(ct) ?? 0m;

        return new ProductFiltersDto
        {
            Brands = brands,
            Finishes = finishes,
            ColorTemperatures = temperatures,
            MinPrice = minPrice,
            MaxPrice = maxPrice
        };
    }

    public Task<Product?> GetPublicDetailAsync(string slug, CancellationToken ct) =>
        QueryPublic()
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Inventory)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<Product?> GetForAdminAsync(Guid id, CancellationToken ct) =>
        db.Products
            .Include(p => p.Images)
            .Include(p => p.Variants).ThenInclude(v => v.Inventory)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<PagedResult<AdminProductListItemDto>> SearchAdminAsync(AdminProductQuery q, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = SearchPattern.Contains(q.Search);
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.ILike(p.Sku, pattern, SearchPattern.EscapeCharacter) ||
                (p.Brand != null && EF.Functions.ILike(p.Brand, pattern, SearchPattern.EscapeCharacter)) ||
                p.Variants.Any(v => EF.Functions.ILike(v.Sku, pattern, SearchPattern.EscapeCharacter)));
        }

        if (q.CategoryId.HasValue)
        {
            var categoryId = q.CategoryId.Value;
            query = query.Where(p => p.CategoryId == categoryId);
        }

        switch (q.Status?.Trim().ToLowerInvariant())
        {
            case "active":
                query = query.Where(p => p.IsActive);
                break;
            case "inactive":
                query = query.Where(p => !p.IsActive);
                break;
        }

        var total = await query.CountAsync(ct);
        var rows = await WithPricing(query)
            .OrderByDescending(x => x.P.UpdatedAt).ThenBy(x => x.P.Id)
            .Skip(q.Skip).Take(q.PageSize)
            .Select(x => new AdminProductListItemDto
            {
                Id = x.P.Id,
                Name = x.P.Name,
                Slug = x.P.Slug,
                Sku = x.P.Sku,
                CategoryName = x.P.Category.Name,
                Price = x.Price,
                CompareAtPrice = Pricing.EffectiveCompareAt(x.Price, x.CompareAt),
                TotalStock = x.P.Variants.Sum(v => v.Inventory != null ? v.Inventory.QuantityOnHand : 0),
                VariantCount = x.P.Variants.Count(),
                IsActive = x.P.IsActive,
                IsFeatured = x.P.IsFeatured,
                IsNew = x.P.IsNew,
                PrimaryImageUrl = x.P.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
                    .Select(i => i.Url).FirstOrDefault(),
                UpdatedAt = x.P.UpdatedAt
            })
            .ToListAsync(ct);

        return PagedResult<AdminProductListItemDto>.Create(rows, q.Page, q.PageSize, total);
    }

    public Task<bool> SlugExistsAsync(string slug, Guid? excludeId, CancellationToken ct) =>
        db.Products.AnyAsync(p => p.Slug == slug && (excludeId == null || p.Id != excludeId), ct);

    public Task<bool> ProductSkuExistsAsync(string sku, Guid? excludeId, CancellationToken ct) =>
        db.Products.AnyAsync(p => p.Sku == sku && (excludeId == null || p.Id != excludeId), ct);

    public Task<bool> VariantSkuExistsAsync(string sku, Guid? excludeVariantId, CancellationToken ct) =>
        db.ProductVariants.AnyAsync(v => v.Sku == sku && (excludeVariantId == null || v.Id != excludeVariantId), ct);
}
