using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services.Admin;

public interface IAdminCategoryService
{
    Task<IReadOnlyList<AdminCategoryDto>> GetAllAsync(CancellationToken ct);
    Task<AdminCategoryDto> CreateAsync(UpsertCategoryRequest request, CancellationToken ct);
    Task<AdminCategoryDto> UpdateAsync(Guid id, UpsertCategoryRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class AdminCategoryService(AppDbContext db) : IAdminCategoryService
{
    public async Task<IReadOnlyList<AdminCategoryDto>> GetAllAsync(CancellationToken ct)
    {
        var counts = await ProductCountsAsync(ct);
        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return categories.Select(c => ToDto(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<AdminCategoryDto> CreateAsync(UpsertCategoryRequest request, CancellationToken ct)
    {
        var category = new Category();
        await ApplyAsync(category, request, ct);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return ToDto(category, 0);
    }

    public async Task<AdminCategoryDto> UpdateAsync(Guid id, UpsertCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category not found");
        await ApplyAsync(category, request, ct);
        await db.SaveChangesAsync(ct);
        var count = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        return ToDto(category, count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category not found");
        if (await db.Products.AnyAsync(p => p.CategoryId == id, ct))
        {
            throw new BusinessRuleException("This category still contains products. Move or delete them first.");
        }
        if (await db.Categories.AnyAsync(c => c.ParentId == id, ct))
        {
            throw new BusinessRuleException("This category has sub-categories. Move or delete them first.");
        }

        category.MarkDeleted();
        category.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Category category, UpsertCategoryRequest r, CancellationToken ct)
    {
        var slug = string.IsNullOrWhiteSpace(r.Slug) ? SlugHelper.Slugify(r.Name) : r.Slug.Trim().ToLowerInvariant();
        if (slug != category.Slug && await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != category.Id, ct))
        {
            throw new ConflictException($"A category with slug '{slug}' already exists");
        }

        if (r.ParentId.HasValue)
        {
            var parentId = r.ParentId.Value;
            if (parentId == category.Id) throw new ValidationException("parentId", "A category cannot be its own parent.");

            var all = await db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
            if (all.All(c => c.Id != parentId)) throw new ValidationException("parentId", "Parent category does not exist.");

            // Walk up from the proposed parent: reaching this category would create a cycle.
            var parents = all.ToDictionary(c => c.Id, c => c.ParentId);
            Guid? cursor = parentId;
            var guard = 0;
            while (cursor.HasValue && guard++ < 100)
            {
                if (cursor.Value == category.Id) throw new ValidationException("parentId", "A category cannot be moved under its own descendant.");
                cursor = parents.GetValueOrDefault(cursor.Value);
            }
        }

        category.Name = r.Name.Trim();
        category.Slug = slug;
        category.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        category.ImageUrl = string.IsNullOrWhiteSpace(r.ImageUrl) ? null : r.ImageUrl.Trim();
        category.ParentId = r.ParentId;
        category.SortOrder = r.SortOrder;
        category.IsActive = r.IsActive;
    }

    private Task<Dictionary<Guid, int>> ProductCountsAsync(CancellationToken ct) =>
        db.Products.AsNoTracking()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private static AdminCategoryDto ToDto(Category c, int productCount) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Slug = c.Slug,
        Description = c.Description,
        ImageUrl = c.ImageUrl,
        ParentId = c.ParentId,
        SortOrder = c.SortOrder,
        ProductCount = productCount,
        Children = [],
        IsActive = c.IsActive
    };
}
