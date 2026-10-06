using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services.Admin;

public interface IInventoryService
{
    Task<PagedResult<InventoryItemDto>> SearchAsync(InventoryQuery query, CancellationToken ct);
    Task<IReadOnlyList<InventoryItemDto>> GetLowStockAsync(int take, CancellationToken ct);
    Task<InventoryItemDto> UpdateAsync(Guid variantId, UpdateInventoryRequest request, CancellationToken ct);
}

public sealed class InventoryService(AppDbContext db, ILogger<InventoryService> logger) : IInventoryService
{
    /// <summary>Non-deleted variants of non-deleted products (query filters handle IsDeleted).</summary>
    private IQueryable<ProductVariant> Variants() => db.ProductVariants.AsNoTracking();

    private static IQueryable<InventoryItemDto> Project(IQueryable<ProductVariant> query) =>
        query.Select(v => new InventoryItemDto
        {
            VariantId = v.Id,
            ProductId = v.ProductId,
            ProductName = v.Product.Name,
            VariantName = v.Name,
            Sku = v.Sku,
            ImageUrl = v.Product.Images
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
                .Select(i => i.Url).FirstOrDefault(),
            QuantityOnHand = v.Inventory != null ? v.Inventory.QuantityOnHand : 0,
            ReservedQuantity = v.Inventory != null ? v.Inventory.ReservedQuantity : 0,
            Available = v.Inventory != null && v.Inventory.QuantityOnHand > v.Inventory.ReservedQuantity
                ? v.Inventory.QuantityOnHand - v.Inventory.ReservedQuantity
                : 0,
            LowStockThreshold = v.Inventory != null ? v.Inventory.LowStockThreshold : 5,
            IsLowStock = v.Inventory == null ||
                         v.Inventory.QuantityOnHand - v.Inventory.ReservedQuantity <= v.Inventory.LowStockThreshold
        });

    private static IQueryable<ProductVariant> WhereLowStock(IQueryable<ProductVariant> query) =>
        query.Where(v => v.Inventory == null ||
                         v.Inventory.QuantityOnHand - v.Inventory.ReservedQuantity <= v.Inventory.LowStockThreshold);

    public async Task<PagedResult<InventoryItemDto>> SearchAsync(InventoryQuery q, CancellationToken ct)
    {
        var query = Variants();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = SearchPattern.Contains(q.Search);
            const string esc = SearchPattern.EscapeCharacter;
            query = query.Where(v =>
                EF.Functions.ILike(v.Sku, pattern, esc) ||
                EF.Functions.ILike(v.Name, pattern, esc) ||
                EF.Functions.ILike(v.Product.Name, pattern, esc));
        }

        if (q.LowStockOnly == true) query = WhereLowStock(query);

        var total = await query.CountAsync(ct);
        var items = await Project(query
                .OrderBy(v => v.Product.Name).ThenBy(v => v.Name).ThenBy(v => v.Id)
                .Skip(q.Skip).Take(q.PageSize))
            .ToListAsync(ct);
        return PagedResult<InventoryItemDto>.Create(items, q.Page, q.PageSize, total);
    }

    public async Task<IReadOnlyList<InventoryItemDto>> GetLowStockAsync(int take, CancellationToken ct) =>
        await Project(WhereLowStock(Variants().Where(v => v.IsActive && v.Product.IsActive))
                .OrderBy(v => v.Inventory != null ? v.Inventory.QuantityOnHand - v.Inventory.ReservedQuantity : 0)
                .ThenBy(v => v.Product.Name)
                .Take(take))
            .ToListAsync(ct);

    public async Task<InventoryItemDto> UpdateAsync(Guid variantId, UpdateInventoryRequest request, CancellationToken ct)
    {
        var variant = await db.ProductVariants.Include(v => v.Inventory).FirstOrDefaultAsync(v => v.Id == variantId, ct)
                      ?? throw new NotFoundException("Variant not found");

        var inventory = variant.Inventory;
        if (inventory is null)
        {
            inventory = new Inventory { VariantId = variant.Id };
            db.Inventory.Add(inventory);
        }

        var before = inventory.QuantityOnHand;
        if (request.QuantityOnHand.HasValue)
        {
            inventory.QuantityOnHand = request.QuantityOnHand.Value;
        }
        else if (request.Adjustment.HasValue)
        {
            var next = inventory.QuantityOnHand + request.Adjustment.Value;
            if (next < 0)
            {
                throw new BusinessRuleException($"Adjustment would make stock negative (on hand: {inventory.QuantityOnHand})");
            }
            inventory.QuantityOnHand = next;
        }

        if (request.LowStockThreshold.HasValue) inventory.LowStockThreshold = request.LowStockThreshold.Value;
        if (inventory.QuantityOnHand < inventory.ReservedQuantity)
        {
            throw new BusinessRuleException($"Quantity on hand cannot be lower than the reserved quantity ({inventory.ReservedQuantity})");
        }

        // xmin concurrency token: a concurrent checkout changing this row results in 409 (handled by middleware).
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Inventory for variant {VariantId} changed from {Before} to {After}", variantId, before, inventory.QuantityOnHand);

        return await Project(Variants().Where(v => v.Id == variantId)).FirstAsync(ct);
    }
}
