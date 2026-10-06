using UniqueLights.Api.DTOs.Catalog;

namespace UniqueLights.Api.DTOs.Shopping;

public sealed class CartItemDto
{
    public Guid Id { get; init; }
    public Guid VariantId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductSlug { get; init; } = string.Empty;
    public string VariantName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public int Quantity { get; init; }
    public decimal LineTotal { get; init; }
    public int AvailableStock { get; init; }
}

public sealed class CartDto
{
    public Guid Id { get; init; }
    public IReadOnlyList<CartItemDto> Items { get; init; } = [];
    public int ItemCount { get; init; }
    public decimal Subtotal { get; init; }
}

public sealed class AddCartItemRequest
{
    public Guid VariantId { get; init; }
    public int Quantity { get; init; }
}

public sealed class UpdateCartItemRequest
{
    public int Quantity { get; init; }
}

public sealed class MergeCartRequest
{
    public List<AddCartItemRequest> Items { get; init; } = [];
}

public sealed class WishlistItemDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public DateTime AddedAt { get; init; }
    public ProductListItemDto Product { get; init; } = null!;
}

public sealed class WishlistDto
{
    public IReadOnlyList<WishlistItemDto> Items { get; init; } = [];
}

public sealed class AddWishlistItemRequest
{
    public Guid ProductId { get; init; }
}

public sealed class ValidateCouponRequest
{
    public string Code { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
}

public sealed class CouponValidationDto
{
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal DiscountAmount { get; init; }
}
