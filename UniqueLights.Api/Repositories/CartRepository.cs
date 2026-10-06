using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Shopping;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Repositories;

public interface ICartRepository
{
    /// <summary>Returns the user's cart id, creating an empty cart if none exists (race-safe).</summary>
    Task<Guid> GetOrCreateCartIdAsync(Guid userId, CancellationToken ct);

    Task<CartDto> GetCartDtoAsync(Guid cartId, CancellationToken ct);

    /// <summary>Tracked cart items with variant, product, images and inventory (for checkout).</summary>
    Task<List<CartItem>> GetItemsForCheckoutAsync(Guid userId, CancellationToken ct);
}

public sealed class CartRepository(AppDbContext db) : ICartRepository
{
    public async Task<Guid> GetOrCreateCartIdAsync(Guid userId, CancellationToken ct)
    {
        var existing = await db.Carts.AsNoTracking().Where(c => c.UserId == userId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        var cart = new Cart { UserId = userId };
        db.Carts.Add(cart);
        try
        {
            await db.SaveChangesAsync(ct);
            return cart.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request created the cart first.
            db.Entry(cart).State = EntityState.Detached;
            return await db.Carts.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.Id).FirstAsync(ct);
        }
    }

    public async Task<CartDto> GetCartDtoAsync(Guid cartId, CancellationToken ct)
    {
        var rows = await db.CartItems.AsNoTracking()
            .Where(ci => ci.CartId == cartId)
            .OrderBy(ci => ci.CreatedAt).ThenBy(ci => ci.Id)
            .Select(ci => new
            {
                ci.Id,
                ci.VariantId,
                ci.Variant.ProductId,
                ProductName = ci.Variant.Product.Name,
                ProductSlug = ci.Variant.Product.Slug,
                VariantName = ci.Variant.Name,
                ci.Variant.Sku,
                ImageUrl = ci.Variant.Product.Images
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
                    .Select(i => i.Url).FirstOrDefault(),
                ci.Variant.Price,
                ci.Variant.CompareAtPrice,
                ci.Quantity,
                Available = ci.Variant.Inventory != null
                    ? ci.Variant.Inventory.QuantityOnHand - ci.Variant.Inventory.ReservedQuantity
                    : 0,
                Purchasable = ci.Variant.IsActive && ci.Variant.Product.IsActive && ci.Variant.Product.Category.IsActive
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new CartItemDto
        {
            Id = r.Id,
            VariantId = r.VariantId,
            ProductId = r.ProductId,
            ProductName = r.ProductName,
            ProductSlug = r.ProductSlug,
            VariantName = r.VariantName,
            Sku = r.Sku,
            ImageUrl = r.ImageUrl,
            UnitPrice = r.Price,
            CompareAtPrice = Pricing.EffectiveCompareAt(r.Price, r.CompareAtPrice),
            Quantity = r.Quantity,
            LineTotal = Pricing.Round(r.Price * r.Quantity),
            AvailableStock = r.Purchasable ? Math.Max(0, r.Available) : 0
        }).ToList();

        return new CartDto
        {
            Id = cartId,
            Items = items,
            ItemCount = items.Sum(i => i.Quantity),
            Subtotal = Pricing.Round(items.Sum(i => i.LineTotal))
        };
    }

    public Task<List<CartItem>> GetItemsForCheckoutAsync(Guid userId, CancellationToken ct) =>
        db.CartItems
            .Where(ci => ci.Cart.UserId == userId)
            .Include(ci => ci.Variant).ThenInclude(v => v.Inventory)
            .Include(ci => ci.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .Include(ci => ci.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Category)
            .OrderBy(ci => ci.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);
}
