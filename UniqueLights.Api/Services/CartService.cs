using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Shopping;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Repositories;

namespace UniqueLights.Api.Services;

public interface ICartService
{
    Task<CartDto> GetAsync(Guid userId, CancellationToken ct);
    Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request, CancellationToken ct);
    Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request, CancellationToken ct);
    Task<CartDto> RemoveItemAsync(Guid userId, Guid itemId, CancellationToken ct);
    Task<CartDto> ClearAsync(Guid userId, CancellationToken ct);
    Task<CartDto> MergeAsync(Guid userId, MergeCartRequest request, CancellationToken ct);
}

public sealed class CartService(AppDbContext db, ICartRepository carts) : ICartService
{
    public const int MaxQuantityPerLine = 99;

    private sealed record VariantInfo(Guid Id, string ProductName, string VariantName, bool Purchasable, int Available);

    public async Task<CartDto> GetAsync(Guid userId, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);
        return await carts.GetCartDtoAsync(cartId, ct);
    }

    public async Task<CartDto> AddItemAsync(Guid userId, AddCartItemRequest request, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);
        var variant = await GetVariantInfoAsync(request.VariantId, ct) ?? throw new NotFoundException("Product variant not found");
        EnsurePurchasable(variant);

        var item = await db.CartItems.FirstOrDefaultAsync(ci => ci.CartId == cartId && ci.VariantId == variant.Id, ct);
        var newQuantity = (item?.Quantity ?? 0) + request.Quantity;

        if (newQuantity > MaxQuantityPerLine)
        {
            throw new BusinessRuleException($"You can add at most {MaxQuantityPerLine} units of an item to your cart.");
        }
        EnsureStock(variant, newQuantity);

        if (item is null)
        {
            db.CartItems.Add(new CartItem { CartId = cartId, VariantId = variant.Id, Quantity = newQuantity });
        }
        else
        {
            item.Quantity = newQuantity;
        }

        await db.SaveChangesAsync(ct);
        return await carts.GetCartDtoAsync(cartId, ct);
    }

    public async Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, UpdateCartItemRequest request, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);
        var item = await db.CartItems.FirstOrDefaultAsync(ci => ci.Id == itemId && ci.CartId == cartId, ct)
                   ?? throw new NotFoundException("Cart item not found");

        var variant = await GetVariantInfoAsync(item.VariantId, ct) ?? throw new NotFoundException("Product variant not found");
        EnsurePurchasable(variant);
        EnsureStock(variant, request.Quantity);

        item.Quantity = request.Quantity;
        await db.SaveChangesAsync(ct);
        return await carts.GetCartDtoAsync(cartId, ct);
    }

    public async Task<CartDto> RemoveItemAsync(Guid userId, Guid itemId, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);
        var deleted = await db.CartItems.Where(ci => ci.Id == itemId && ci.CartId == cartId).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new NotFoundException("Cart item not found");
        return await carts.GetCartDtoAsync(cartId, ct);
    }

    public async Task<CartDto> ClearAsync(Guid userId, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);
        await db.CartItems.Where(ci => ci.CartId == cartId).ExecuteDeleteAsync(ct);
        return await carts.GetCartDtoAsync(cartId, ct);
    }

    public async Task<CartDto> MergeAsync(Guid userId, MergeCartRequest request, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);

        var requested = request.Items
            .GroupBy(i => i.VariantId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        if (requested.Count == 0) return await carts.GetCartDtoAsync(cartId, ct);

        var variantIds = requested.Keys.ToList();
        var variants = await QueryVariantInfo(db.ProductVariants.Where(v => variantIds.Contains(v.Id))).ToListAsync(ct);
        var existing = await db.CartItems.Where(ci => ci.CartId == cartId && variantIds.Contains(ci.VariantId)).ToListAsync(ct);

        foreach (var variant in variants)
        {
            if (!variant.Purchasable || variant.Available <= 0) continue; // silently skip unavailable lines

            var line = existing.FirstOrDefault(ci => ci.VariantId == variant.Id);
            var desired = (line?.Quantity ?? 0) + requested[variant.Id];
            var clamped = Math.Min(Math.Min(desired, variant.Available), MaxQuantityPerLine);
            if (clamped < 1) continue;

            if (line is null)
            {
                db.CartItems.Add(new CartItem { CartId = cartId, VariantId = variant.Id, Quantity = clamped });
            }
            else
            {
                line.Quantity = clamped;
            }
        }

        await db.SaveChangesAsync(ct);
        return await carts.GetCartDtoAsync(cartId, ct);
    }

    private static IQueryable<VariantInfo> QueryVariantInfo(IQueryable<ProductVariant> query) =>
        query.Select(v => new VariantInfo(
            v.Id,
            v.Product.Name,
            v.Name,
            v.IsActive && v.Product.IsActive && v.Product.Category.IsActive,
            v.Inventory != null ? v.Inventory.QuantityOnHand - v.Inventory.ReservedQuantity : 0));

    private Task<VariantInfo?> GetVariantInfoAsync(Guid variantId, CancellationToken ct) =>
        QueryVariantInfo(db.ProductVariants.AsNoTracking().Where(v => v.Id == variantId)).FirstOrDefaultAsync(ct);

    private static void EnsurePurchasable(VariantInfo variant)
    {
        if (!variant.Purchasable)
        {
            throw new BusinessRuleException($"{variant.ProductName} is no longer available.");
        }
    }

    private static void EnsureStock(VariantInfo variant, int quantity)
    {
        var available = Math.Max(0, variant.Available);
        if (quantity <= available) return;

        var name = $"{variant.ProductName} ({variant.VariantName})";
        throw new ConflictException(available == 0
            ? $"{name} is out of stock."
            : $"Only {available} unit{(available == 1 ? "" : "s")} of {name} available.");
    }
}
