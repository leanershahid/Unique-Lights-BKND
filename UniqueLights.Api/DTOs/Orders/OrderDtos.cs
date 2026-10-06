using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.DTOs.Orders;

public sealed class CheckoutQuoteRequest
{
    public DeliveryMethod DeliveryMethod { get; init; }
    public string? CouponCode { get; init; }
}

public sealed class CheckoutQuoteDto
{
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Shipping { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public string? CouponCode { get; init; }
    public string? CouponMessage { get; init; }
    public DateTime EstimatedDelivery { get; init; }
}

public sealed class CreateOrderRequest
{
    public Guid AddressId { get; init; }
    public DeliveryMethod DeliveryMethod { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public string? CouponCode { get; init; }
    public string? Notes { get; init; }
}

public sealed class OrderItemDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public Guid VariantId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductSlug { get; init; } = string.Empty;
    public string VariantName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed class OrderAddressDto
{
    public string FullName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Line1 { get; init; } = string.Empty;
    public string? Line2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}

public sealed class PaymentDto
{
    public PaymentMethod Method { get; init; }
    public PaymentStatus Status { get; init; }
    public decimal Amount { get; init; }
    public string? TransactionReference { get; init; }
    public DateTime? PaidAt { get; init; }
}

public sealed class OrderTimelineEntryDto
{
    public OrderStatus Status { get; init; }
    public DateTime? At { get; init; }
    public bool Completed { get; init; }
}

public sealed class OrderSummaryDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public decimal Total { get; init; }
    public int ItemCount { get; init; }
    public DateTime PlacedAt { get; init; }
    public IReadOnlyList<string> PreviewImages { get; init; } = [];
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
}

public sealed class OrderDto
{
    public Guid Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Shipping { get; init; }
    public decimal Tax { get; init; }
    public decimal Total { get; init; }
    public string? CouponCode { get; init; }
    public DeliveryMethod DeliveryMethod { get; init; }
    public string? Notes { get; init; }
    public string? TrackingNumber { get; init; }
    public DateTime PlacedAt { get; init; }
    public DateTime? EstimatedDelivery { get; init; }
    public OrderAddressDto ShippingAddress { get; init; } = null!;
    public PaymentDto Payment { get; init; } = null!;
    public IReadOnlyList<OrderItemDto> Items { get; init; } = [];
    public IReadOnlyList<OrderTimelineEntryDto> Timeline { get; init; } = [];
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
}

public sealed class UpdateOrderStatusRequest
{
    public OrderStatus Status { get; init; }
    public string? TrackingNumber { get; init; }
}

public sealed class AdminOrderQuery : PagingQuery
{
    public OrderStatus? Status { get; set; }
    public string? Search { get; set; }
}
