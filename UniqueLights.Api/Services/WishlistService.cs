using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Shopping;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Repositories;

namespace UniqueLights.Api.Services;

public interface IWishlistService
{
    Task<WishlistDto> GetAsync(Guid userId, CancellationToken ct);
    Task<WishlistDto> AddAsync(Guid userId, Guid productId, CancellationToken ct);
    Task<WishlistDto> RemoveAsync(Guid userId, Guid productId, CancellationToken ct);
}

public sealed class WishlistService(AppDbContext db, IProductRepository products) : IWishlistService
{
    private const int MaxItems = 200;

    public async Task<WishlistDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var wishlistId = await GetOrCreateIdAsync(userId, ct);
        return await BuildAsync(wishlistId, ct);
    }

    public async Task<WishlistDto> AddAsync(Guid userId, Guid productId, CancellationToken ct)
    {
        var wishlistId = await GetOrCreateIdAsync(userId, ct);

        if (!await products.QueryPublic().AnyAsync(p => p.Id == productId, ct))
        {
            throw new NotFoundException("Product not found");
        }

        var exists = await db.WishlistItems.AnyAsync(w => w.WishlistId == wishlistId && w.ProductId == productId, ct);
        if (!exists)
        {
            if (await db.WishlistItems.CountAsync(w => w.WishlistId == wishlistId, ct) >= MaxItems)
            {
                throw new BusinessRuleException($"Your wishlist can contain at most {MaxItems} products.");
            }

            db.WishlistItems.Add(new WishlistItem { WishlistId = wishlistId, ProductId = productId });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Concurrent duplicate add: endpoint is idempotent.
                db.ChangeTracker.Clear();
            }
        }

        return await BuildAsync(wishlistId, ct);
    }

    public async Task<WishlistDto> RemoveAsync(Guid userId, Guid productId, CancellationToken ct)
    {
        var wishlistId = await GetOrCreateIdAsync(userId, ct);
        await db.WishlistItems.Where(w => w.WishlistId == wishlistId && w.ProductId == productId).ExecuteDeleteAsync(ct);
        return await BuildAsync(wishlistId, ct);
    }

    private async Task<Guid> GetOrCreateIdAsync(Guid userId, CancellationToken ct)
    {
        var existing = await db.Wishlists.AsNoTracking().Where(w => w.UserId == userId).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct);
        if (existing.HasValue) return existing.Value;

        var wishlist = new Wishlist { UserId = userId };
        db.Wishlists.Add(wishlist);
        try
        {
            await db.SaveChangesAsync(ct);
            return wishlist.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(wishlist).State = EntityState.Detached;
            return await db.Wishlists.AsNoTracking().Where(w => w.UserId == userId).Select(w => w.Id).FirstAsync(ct);
        }
    }

    private async Task<WishlistDto> BuildAsync(Guid wishlistId, CancellationToken ct)
    {
        var rows = await db.WishlistItems.AsNoTracking()
            .Where(w => w.WishlistId == wishlistId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new { w.Id, w.ProductId, w.CreatedAt })
            .ToListAsync(ct);

        var listItems = (await products.GetListItemsByIdsAsync(rows.Select(r => r.ProductId).ToList(), ct))
            .ToDictionary(p => p.Id);

        // Products that became unavailable (inactive / deleted) are hidden from the wishlist.
        return new WishlistDto
        {
            Items = rows
                .Where(r => listItems.ContainsKey(r.ProductId))
                .Select(r => new WishlistItemDto
                {
                    Id = r.Id,
                    ProductId = r.ProductId,
                    AddedAt = r.CreatedAt,
                    Product = listItems[r.ProductId]
                })
                .ToList()
        };
    }
}
