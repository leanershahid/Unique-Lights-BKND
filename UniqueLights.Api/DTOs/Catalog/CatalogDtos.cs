using UniqueLights.Api.DTOs.Common;

namespace UniqueLights.Api.DTOs.Catalog;

public class CategoryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public Guid? ParentId { get; init; }
    public int SortOrder { get; init; }
    public int ProductCount { get; init; }
    public List<CategoryDto> Children { get; init; } = [];
}

public static class ProductSorts
{
    public const string Featured = "featured";
    public const string Newest = "newest";
    public const string PriceAsc = "price_asc";
    public const string PriceDesc = "price_desc";
    public const string Rating = "rating";
    public const string Name = "name";

    public static readonly string[] All = [Featured, Newest, PriceAsc, PriceDesc, Rating, Name];
}

public sealed class ProductImageDto
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int SortOrder { get; init; }
    public bool IsPrimary { get; init; }
}

public class ProductListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? ShortDescription { get; init; }
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public int? DiscountPercent { get; init; }
    public ProductImageDto? PrimaryImage { get; init; }
    public ProductImageDto? SecondaryImage { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string CategorySlug { get; init; } = string.Empty;
    public decimal AverageRating { get; init; }
    public int ReviewCount { get; init; }
    public bool InStock { get; init; }
    public bool IsNew { get; init; }
    public bool IsFeatured { get; init; }
    public Guid? DefaultVariantId { get; init; }
}

public class ProductVariantDto
{
    public Guid Id { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Color { get; init; }
    public string? Finish { get; init; }
    public string? ColorTemperature { get; init; }
    public int? Wattage { get; init; }
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public int Stock { get; init; }
    public bool InStock { get; init; }
    public bool IsDefault { get; init; }
}

public sealed class ProductDetailDto : ProductListItemDto
{
    public string Sku { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Dictionary<string, string> Specifications { get; init; } = new();
    public List<ProductImageDto> Images { get; init; } = [];
    public List<ProductVariantDto> Variants { get; init; } = [];
    public IdNameSlugDto Category { get; init; } = null!;
}

public sealed class ProductFiltersDto
{
    public IReadOnlyList<string> Brands { get; init; } = [];
    public IReadOnlyList<string> Finishes { get; init; } = [];
    public IReadOnlyList<string> ColorTemperatures { get; init; } = [];
    public decimal MinPrice { get; init; }
    public decimal MaxPrice { get; init; }
}

public sealed class SearchSuggestionsDto
{
    public IReadOnlyList<ProductListItemDto> Products { get; init; } = [];
    public IReadOnlyList<IdNameSlugDto> Categories { get; init; } = [];
}

/// <summary>GET /api/products query string.</summary>
public sealed class ProductQueryParameters : PagingQuery
{
    public string? Search { get; set; }
    public string? Category { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public string? Brands { get; set; }
    public string? Finishes { get; set; }
    public string? ColorTemperatures { get; set; }
    public bool? InStock { get; set; }
    public bool? Featured { get; set; }
    public bool? IsNew { get; set; }
    public string? Sort { get; set; }
}
