using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Auth;
using UniqueLights.Api.DTOs.Catalog;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Customers;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Services.Common;

/// <summary>In-memory entity -> DTO mapping for single aggregates (list endpoints project in SQL instead).</summary>
public static class Mappers
{
    public static UserDto ToDto(this User user, IEnumerable<string> roles) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Phone = user.Phone,
        Roles = roles.OrderBy(r => r).ToList(),
        CreatedAt = user.CreatedAt
    };

    public static AddressDto ToDto(this Address a) => new()
    {
        Id = a.Id,
        Label = a.Label,
        FullName = a.FullName,
        Phone = a.Phone,
        Line1 = a.Line1,
        Line2 = a.Line2,
        City = a.City,
        State = a.State,
        PostalCode = a.PostalCode,
        Country = a.Country,
        IsDefault = a.IsDefault
    };

    public static ProductImageDto ToDto(this ProductImage i) => new()
    {
        Id = i.Id,
        Url = i.Url,
        AltText = i.AltText,
        SortOrder = i.SortOrder,
        IsPrimary = i.IsPrimary
    };

    public static IEnumerable<ProductImage> OrderedImages(this Product p) =>
        p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt);

    public static IEnumerable<ProductVariant> OrderedVariants(this IEnumerable<ProductVariant> variants) =>
        variants.OrderByDescending(v => v.IsDefault).ThenBy(v => v.Price).ThenBy(v => v.Name);

    public static int AvailableStock(this ProductVariant v) => v.Inventory?.Available ?? 0;

    public static ProductVariantDto ToDto(this ProductVariant v) => new()
    {
        Id = v.Id,
        Sku = v.Sku,
        Name = v.Name,
        Color = v.Color,
        Finish = v.Finish,
        ColorTemperature = v.ColorTemperature,
        Wattage = v.Wattage,
        Price = v.Price,
        CompareAtPrice = Pricing.EffectiveCompareAt(v.Price, v.CompareAtPrice),
        Stock = v.AvailableStock(),
        InStock = v.IsActive && v.AvailableStock() > 0,
        IsDefault = v.IsDefault
    };

    public static AdminVariantDto ToAdminDto(this ProductVariant v) => new()
    {
        Id = v.Id,
        Sku = v.Sku,
        Name = v.Name,
        Color = v.Color,
        Finish = v.Finish,
        ColorTemperature = v.ColorTemperature,
        Wattage = v.Wattage,
        Price = v.Price,
        CompareAtPrice = v.CompareAtPrice,
        Stock = v.AvailableStock(),
        InStock = v.IsActive && v.AvailableStock() > 0,
        IsDefault = v.IsDefault,
        IsActive = v.IsActive,
        QuantityOnHand = v.Inventory?.QuantityOnHand ?? 0,
        ReservedQuantity = v.Inventory?.ReservedQuantity ?? 0,
        LowStockThreshold = v.Inventory?.LowStockThreshold ?? 5
    };

    /// <summary>Requires Category, Images, Variants (+Inventory) to be loaded.</summary>
    public static ProductDetailDto ToDetailDto(this Product p)
    {
        var activeVariants = p.Variants.Where(v => v.IsActive && !v.IsDeleted).OrderedVariants().ToList();
        var defaultVariant = activeVariants.FirstOrDefault();
        var price = defaultVariant?.Price ?? p.BasePrice;
        var compare = Pricing.EffectiveCompareAt(price, defaultVariant is null ? p.CompareAtPrice : defaultVariant.CompareAtPrice);
        var images = p.OrderedImages().Select(i => i.ToDto()).ToList();

        return new ProductDetailDto
        {
            Id = p.Id,
            Name = p.Name,
            Slug = p.Slug,
            Brand = p.Brand,
            ShortDescription = p.ShortDescription,
            Price = price,
            CompareAtPrice = compare,
            DiscountPercent = Pricing.DiscountPercent(price, compare),
            PrimaryImage = images.ElementAtOrDefault(0),
            SecondaryImage = images.ElementAtOrDefault(1),
            CategoryName = p.Category.Name,
            CategorySlug = p.Category.Slug,
            AverageRating = p.AverageRating,
            ReviewCount = p.ReviewCount,
            InStock = activeVariants.Any(v => v.AvailableStock() > 0),
            IsNew = p.IsNew,
            IsFeatured = p.IsFeatured,
            DefaultVariantId = defaultVariant?.Id,
            Sku = p.Sku,
            Description = p.Description,
            Specifications = new Dictionary<string, string>(p.Specifications),
            Images = images,
            Variants = activeVariants.Select(v => v.ToDto()).ToList(),
            Category = new IdNameSlugDto(p.Category.Id, p.Category.Name, p.Category.Slug)
        };
    }

    /// <summary>Requires Images and Variants (+Inventory) to be loaded.</summary>
    public static AdminProductDetailDto ToAdminDetailDto(this Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Slug = p.Slug,
        Sku = p.Sku,
        ShortDescription = p.ShortDescription,
        Description = p.Description,
        Brand = p.Brand,
        BasePrice = p.BasePrice,
        CompareAtPrice = p.CompareAtPrice,
        CategoryId = p.CategoryId,
        IsActive = p.IsActive,
        IsFeatured = p.IsFeatured,
        IsNew = p.IsNew,
        Specifications = new Dictionary<string, string>(p.Specifications),
        Images = p.OrderedImages().Select(i => i.ToDto()).ToList(),
        Variants = p.Variants.Where(v => !v.IsDeleted).OrderedVariants().Select(v => v.ToAdminDto()).ToList(),
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };

    public static CouponDto ToDto(this Coupon c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Description = c.Description,
        DiscountType = c.DiscountType,
        Value = c.Value,
        MinOrderAmount = c.MinOrderAmount,
        MaxDiscountAmount = c.MaxDiscountAmount,
        StartsAt = c.StartsAt,
        EndsAt = c.EndsAt,
        UsageLimit = c.UsageLimit,
        UsedCount = c.UsedCount,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt
    };

    /// <summary>Requires Items and Payment loaded; User loaded when includeCustomer is true.</summary>
    public static OrderDto ToDto(this Order o, bool includeCustomer)
    {
        var payment = o.Payment;
        return new OrderDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            Status = o.Status,
            Subtotal = o.Subtotal,
            Discount = o.Discount,
            Shipping = o.Shipping,
            Tax = o.Tax,
            Total = o.Total,
            CouponCode = o.CouponCode,
            DeliveryMethod = o.DeliveryMethod,
            Notes = o.Notes,
            TrackingNumber = o.TrackingNumber,
            PlacedAt = o.PlacedAt,
            EstimatedDelivery = o.EstimatedDelivery,
            ShippingAddress = new OrderAddressDto
            {
                FullName = o.ShippingAddress.FullName,
                Phone = o.ShippingAddress.Phone,
                Line1 = o.ShippingAddress.Line1,
                Line2 = o.ShippingAddress.Line2,
                City = o.ShippingAddress.City,
                State = o.ShippingAddress.State,
                PostalCode = o.ShippingAddress.PostalCode,
                Country = o.ShippingAddress.Country
            },
            Payment = payment is null
                ? new PaymentDto { Method = PaymentMethod.CashOnDelivery, Status = PaymentStatus.Pending, Amount = o.Total }
                : new PaymentDto
                {
                    Method = payment.Method,
                    Status = payment.Status,
                    Amount = payment.Amount,
                    TransactionReference = payment.TransactionReference,
                    PaidAt = payment.PaidAt
                },
            Items = o.Items.OrderBy(i => i.ProductName).ThenBy(i => i.VariantName).Select(i => new OrderItemDto
            {
                Id = i.Id,
                ProductId = i.ProductId,
                VariantId = i.VariantId,
                ProductName = i.ProductName,
                ProductSlug = i.ProductSlug,
                VariantName = i.VariantName,
                Sku = i.Sku,
                ImageUrl = i.ImageUrl,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal
            }).ToList(),
            Timeline = BuildTimeline(o),
            CustomerName = includeCustomer && o.User is not null ? $"{o.User.FirstName} {o.User.LastName}".Trim() : null,
            CustomerEmail = includeCustomer ? o.User?.Email : null
        };
    }

    /// <summary>
    /// Pending ("placed") → Confirmed → Shipped → OutForDelivery → Delivered. A cancelled order shows the steps
    /// that actually happened followed by Cancelled.
    /// </summary>
    public static IReadOnlyList<OrderTimelineEntryDto> BuildTimeline(Order o)
    {
        var steps = new (OrderStatus Status, DateTime? At)[]
        {
            (OrderStatus.Pending, o.PlacedAt),
            (OrderStatus.Confirmed, o.ConfirmedAt),
            (OrderStatus.Shipped, o.ShippedAt),
            (OrderStatus.OutForDelivery, o.OutForDeliveryAt),
            (OrderStatus.Delivered, o.DeliveredAt)
        };

        if (o.Status == OrderStatus.Cancelled)
        {
            var happened = steps.Where(s => s.At.HasValue)
                .Select(s => new OrderTimelineEntryDto { Status = s.Status, At = s.At, Completed = true })
                .ToList();
            happened.Add(new OrderTimelineEntryDto { Status = OrderStatus.Cancelled, At = o.CancelledAt, Completed = true });
            return happened;
        }

        return steps.Select(s => new OrderTimelineEntryDto { Status = s.Status, At = s.At, Completed = s.At.HasValue }).ToList();
    }

    public static string AuthorName(string firstName, string lastName)
    {
        var first = firstName.Trim();
        var last = lastName.Trim();
        return last.Length == 0 ? first : $"{first} {char.ToUpperInvariant(last[0])}.";
    }
}
