using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Catalog;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Repositories;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetTreeAsync(CancellationToken ct);
    Task<CategoryDto> GetBySlugAsync(string slug, CancellationToken ct);

    /// <summary>Active category id for the slug plus all active descendants; null when the slug is unknown.</summary>
    Task<IReadOnlyCollection<Guid>?> ResolveCategoryIdsAsync(string slug, CancellationToken ct);
}

public sealed class CategoryService(AppDbContext db) : ICategoryService
{
    private sealed record CategoryRow(Guid Id, string Name, string Slug, string? Description, string? ImageUrl, Guid? ParentId, int SortOrder);

    private async Task<(List<CategoryRow> Rows, Dictionary<Guid, int> DirectCounts)> LoadActiveAsync(CancellationToken ct)
    {
        var rows = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.ParentId, c.SortOrder))
            .ToListAsync(ct);

        var counts = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return (rows, counts);
    }

    private static CategoryDto Build(CategoryRow row, ILookup<Guid?, CategoryRow> byParent, Dictionary<Guid, int> counts, int depth = 0)
    {
        var children = depth > 10
            ? []
            : byParent[row.Id].Select(c => Build(c, byParent, counts, depth + 1)).ToList();

        return new CategoryDto
        {
            Id = row.Id,
            Name = row.Name,
            Slug = row.Slug,
            Description = row.Description,
            ImageUrl = row.ImageUrl,
            ParentId = row.ParentId,
            SortOrder = row.SortOrder,
            // Count includes products of child categories (matches the ?category= filter behaviour).
            ProductCount = counts.GetValueOrDefault(row.Id) + children.Sum(c => c.ProductCount),
            Children = children
        };
    }

    public async Task<IReadOnlyList<CategoryDto>> GetTreeAsync(CancellationToken ct)
    {
        var (rows, counts) = await LoadActiveAsync(ct);
        var ids = rows.Select(r => r.Id).ToHashSet();
        var byParent = rows.ToLookup(r => r.ParentId);
        // Top level = no parent, or parent not active (orphans are promoted so they remain reachable).
        return rows.Where(r => r.ParentId is null || !ids.Contains(r.ParentId.Value))
            .Select(r => Build(r, byParent, counts))
            .ToList();
    }

    public async Task<CategoryDto> GetBySlugAsync(string slug, CancellationToken ct)
    {
        var (rows, counts) = await LoadActiveAsync(ct);
        var row = rows.FirstOrDefault(r => r.Slug == slug.Trim().ToLowerInvariant())
                  ?? throw new NotFoundException("Category not found");
        return Build(row, rows.ToLookup(r => r.ParentId), counts);
    }

    public async Task<IReadOnlyCollection<Guid>?> ResolveCategoryIdsAsync(string slug, CancellationToken ct)
    {
        var all = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Slug, c.ParentId })
            .ToListAsync(ct);

        var normalized = slug.Trim().ToLowerInvariant();
        var root = all.FirstOrDefault(c => c.Slug == normalized);
        if (root is null) return null;

        var byParent = all.ToLookup(c => c.ParentId);
        var result = new HashSet<Guid> { root.Id };
        var queue = new Queue<Guid>([root.Id]);
        while (queue.Count > 0)
        {
            foreach (var child in byParent[queue.Dequeue()])
            {
                if (result.Add(child.Id)) queue.Enqueue(child.Id);
            }
        }
        return result;
    }
}

public interface IProductService
{
    Task<PagedResult<ProductListItemDto>> SearchAsync(ProductQueryParameters query, CancellationToken ct);
    Task<ProductFiltersDto> GetFiltersAsync(string? categorySlug, CancellationToken ct);
    Task<SearchSuggestionsDto> SuggestAsync(string? q, CancellationToken ct);
    Task<ProductDetailDto> GetBySlugAsync(string slug, CancellationToken ct);
    Task<IReadOnlyList<ProductListItemDto>> GetRelatedAsync(string slug, CancellationToken ct);
}

public sealed class ProductService(AppDbContext db, IProductRepository products, ICategoryService categories) : IProductService
{
    public async Task<PagedResult<ProductListItemDto>> SearchAsync(ProductQueryParameters q, CancellationToken ct)
    {
        IReadOnlyCollection<Guid>? categoryIds = null;
        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            categoryIds = await categories.ResolveCategoryIdsAsync(q.Category, ct);
            if (categoryIds is null)
            {
                return PagedResult<ProductListItemDto>.Create([], q.Page, q.PageSize, 0);
            }
        }

        var criteria = new ProductSearchCriteria
        {
            Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
            CategoryIds = categoryIds,
            MinPrice = q.MinPrice,
            MaxPrice = q.MaxPrice,
            Brands = SearchPattern.SplitList(q.Brands),
            Finishes = SearchPattern.SplitList(q.Finishes),
            ColorTemperatures = SearchPattern.SplitList(q.ColorTemperatures),
            InStock = q.InStock,
            Featured = q.Featured,
            IsNew = q.IsNew,
            Sort = string.IsNullOrWhiteSpace(q.Sort) ? ProductSorts.Featured : q.Sort.Trim().ToLowerInvariant()
        };

        return await products.SearchAsync(criteria, q.Page, q.PageSize, ct);
    }

    public async Task<ProductFiltersDto> GetFiltersAsync(string? categorySlug, CancellationToken ct)
    {
        IReadOnlyCollection<Guid>? categoryIds = null;
        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            categoryIds = await categories.ResolveCategoryIdsAsync(categorySlug, ct) ?? [];
        }
        return await products.GetFiltersAsync(categoryIds, ct);
    }

    public async Task<SearchSuggestionsDto> SuggestAsync(string? q, CancellationToken ct)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < 2) return new SearchSuggestionsDto();
        if (term.Length > 100) term = term[..100];

        var productItems = await products.ListAsync(
            new ProductSearchCriteria { Search = term, Sort = ProductSorts.Featured }, 6, ct);

        var pattern = SearchPattern.Contains(term);
        var categoryItems = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && EF.Functions.ILike(c.Name, pattern, SearchPattern.EscapeCharacter))
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Take(4)
            .Select(c => new IdNameSlugDto(c.Id, c.Name, c.Slug))
            .ToListAsync(ct);

        return new SearchSuggestionsDto { Products = productItems, Categories = categoryItems };
    }

    public async Task<ProductDetailDto> GetBySlugAsync(string slug, CancellationToken ct)
    {
        var product = await products.GetPublicDetailAsync(slug.Trim().ToLowerInvariant(), ct)
                      ?? throw new NotFoundException("Product not found");
        return product.ToDetailDto();
    }

    public async Task<IReadOnlyList<ProductListItemDto>> GetRelatedAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var product = await products.QueryPublic()
            .Where(p => p.Slug == normalized)
            .Select(p => new { p.Id, p.CategoryId })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Product not found");

        return await products.ListAsync(new ProductSearchCriteria
        {
            CategoryIds = [product.CategoryId],
            ExcludeProductId = product.Id,
            Sort = ProductSorts.Featured
        }, 8, ct);
    }
}
