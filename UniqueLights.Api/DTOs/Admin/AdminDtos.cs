using UniqueLights.Api.DTOs.Catalog;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Customers;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.DTOs.Admin;

public sealed class DashboardDto
{
    public decimal TotalRevenue { get; init; }
    public int OrdersCount { get; init; }
    public int CustomersCount { get; init; }
    public int ProductsCount { get; init; }
    public decimal AverageOrderValue { get; init; }
    public decimal RevenueLast30Days { get; init; }
    public decimal RevenuePrevious30Days { get; init; }
    public IReadOnlyList<SalesByDayDto> SalesByDay { get; init; } = [];
    public IReadOnlyList<OrdersByStatusDto> OrdersByStatus { get; init; } = [];
    public IReadOnlyList<TopProductDto> TopProducts { get; init; } = [];
    public IReadOnlyList<OrderSummaryDto> RecentOrders { get; init; } = [];
    public IReadOnlyList<InventoryItemDto> LowStock { get; init; } = [];
}

public sealed record SalesByDayDto(string Date, decimal Revenue, int Orders);

public sealed record OrdersByStatusDto(OrderStatus Status, int Count);

public sealed record TopProductDto(Guid ProductId, string Name, string? ImageUrl, int UnitsSold, decimal Revenue);

public sealed class AdminProductListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public int TotalStock { get; init; }
    public int VariantCount { get; init; }
    public bool IsActive { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsNew { get; init; }
    public string? PrimaryImageUrl { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class AdminVariantDto : ProductVariantDto
{
    public bool IsActive { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReservedQuantity { get; init; }
    public int LowStockThreshold { get; init; }
}

public sealed class AdminProductDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? Brand { get; init; }
    public decimal BasePrice { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public Guid CategoryId { get; init; }
    public bool IsActive { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsNew { get; init; }
    public Dictionary<string, string> Specifications { get; init; } = new();
    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];
    public IReadOnlyList<AdminVariantDto> Variants { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class UpsertProductRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? Brand { get; init; }
    public decimal BasePrice { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public Guid CategoryId { get; init; }
    public bool IsActive { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsNew { get; init; }
    public Dictionary<string, string>? Specifications { get; init; }
}

public sealed class UpsertVariantRequest
{
    public string Sku { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Color { get; init; }
    public string? Finish { get; init; }
    public string? ColorTemperature { get; init; }
    public int? Wattage { get; init; }
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public int? InitialStock { get; init; }
    public int? LowStockThreshold { get; init; }
}

public sealed class UploadImageRequest
{
    public IFormFile? File { get; init; }
    public string? AltText { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class AddImageUrlRequest
{
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class UpdateImageRequest
{
    public string? AltText { get; init; }
    public int SortOrder { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class AdminProductQuery : PagingQuery
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }

    /// <summary>active | inactive | all (default all)</summary>
    public string? Status { get; set; }
}

public sealed class UpsertCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public Guid? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}

public sealed class AdminCategoryDto : CategoryDto
{
    public bool IsActive { get; init; }
}

public class AdminCustomerDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public int OrdersCount { get; init; }
    public decimal TotalSpent { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastOrderAt { get; init; }
}

public sealed class AdminCustomerDetailDto : AdminCustomerDto
{
    public IReadOnlyList<AddressDto> Addresses { get; init; } = [];
    public IReadOnlyList<OrderSummaryDto> RecentOrders { get; init; } = [];
}

public sealed class UpdateCustomerStatusRequest
{
    public bool IsActive { get; init; }
}

public sealed class AdminCustomerQuery : PagingQuery
{
    public string? Search { get; set; }
}

public sealed class InventoryItemDto
{
    public Guid VariantId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string VariantName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReservedQuantity { get; init; }
    public int Available { get; init; }
    public int LowStockThreshold { get; init; }
    public bool IsLowStock { get; init; }
}

public sealed class UpdateInventoryRequest
{
    public int? QuantityOnHand { get; init; }
    public int? Adjustment { get; init; }
    public int? LowStockThreshold { get; init; }
}

public sealed class InventoryQuery : PagingQuery
{
    public string? Search { get; set; }
    public bool? LowStockOnly { get; set; }
}

public sealed class CouponDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DiscountType DiscountType { get; init; }
    public decimal Value { get; init; }
    public decimal? MinOrderAmount { get; init; }
    public decimal? MaxDiscountAmount { get; init; }
    public DateTime? StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
    public int? UsageLimit { get; init; }
    public int UsedCount { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class UpsertCouponRequest
{
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DiscountType DiscountType { get; init; }
    public decimal Value { get; init; }
    public decimal? MinOrderAmount { get; init; }
    public decimal? MaxDiscountAmount { get; init; }
    public DateTime? StartsAt { get; init; }
    public DateTime? EndsAt { get; init; }
    public int? UsageLimit { get; init; }
    public bool IsActive { get; init; }
}
