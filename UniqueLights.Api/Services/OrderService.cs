using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Repositories;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services;

public interface IOrderService
{
    Task<CheckoutQuoteDto> QuoteAsync(Guid userId, CheckoutQuoteRequest request, CancellationToken ct);
    Task<OrderDto> CreateAsync(Guid userId, CreateOrderRequest request, CancellationToken ct);
    Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(Guid userId, PagingQuery paging, CancellationToken ct);
    Task<OrderDto> GetMyOrderAsync(Guid userId, string orderNumber, CancellationToken ct);
    Task<OrderDto> CancelMyOrderAsync(Guid userId, string orderNumber, CancellationToken ct);

    // Admin
    Task<PagedResult<OrderSummaryDto>> SearchAsync(AdminOrderQuery query, CancellationToken ct);
    Task<OrderDto> GetAsync(string orderNumber, CancellationToken ct);
    Task<OrderDto> UpdateStatusAsync(string orderNumber, UpdateOrderStatusRequest request, CancellationToken ct);
}

public sealed record OrderTotals(decimal Subtotal, decimal Discount, decimal Shipping, decimal Tax, decimal Total);

public sealed class OrderService(
    AppDbContext db,
    ICartRepository carts,
    IOrderRepository orders,
    ICouponService coupons,
    IPaymentGateway paymentGateway,
    IOptions<CommerceOptions> commerceOptions,
    TimeProvider time,
    ILogger<OrderService> logger) : IOrderService
{
    private const int MaxConcurrencyAttempts = 3;

    /// <summary>Forward progression. Processing is an internal step (not shown in the customer timeline).</summary>
    private static readonly OrderStatus[] Flow =
    [
        OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing,
        OrderStatus.Shipped, OrderStatus.OutForDelivery, OrderStatus.Delivered
    ];

    private static readonly OrderStatus[] AdminCancellable = [OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Processing];
    private static readonly OrderStatus[] CustomerCancellable = [OrderStatus.Pending, OrderStatus.Confirmed];

    private CommerceOptions Commerce => commerceOptions.Value;
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ---------- Pricing ----------

    public OrderTotals CalculateTotals(decimal subtotal, decimal discount, DeliveryMethod method)
    {
        subtotal = Pricing.Round(subtotal);
        discount = Pricing.Round(Math.Clamp(discount, 0, subtotal));
        var shipping = method == DeliveryMethod.Express
            ? Commerce.ExpressShipping
            : subtotal >= Commerce.FreeShippingThreshold ? 0m : Commerce.StandardShipping;
        shipping = Pricing.Round(shipping);
        var taxable = subtotal - discount;
        var tax = Pricing.Round(taxable * Commerce.TaxRate);
        var total = Pricing.Round(taxable + shipping + tax);
        return new OrderTotals(subtotal, discount, shipping, tax, total);
    }

    private DateTime EstimateDelivery(DeliveryMethod method, DateTime from) =>
        from.AddDays(method == DeliveryMethod.Express ? Commerce.ExpressDeliveryDays : Commerce.StandardDeliveryDays);

    // ---------- Customer ----------

    public async Task<CheckoutQuoteDto> QuoteAsync(Guid userId, CheckoutQuoteRequest request, CancellationToken ct)
    {
        var cartId = await carts.GetOrCreateCartIdAsync(userId, ct);
        var cart = await carts.GetCartDtoAsync(cartId, ct);
        if (cart.Items.Count == 0) throw new BusinessRuleException("Your cart is empty");

        decimal discount = 0;
        string? appliedCode = null;
        string? couponMessage = null;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var evaluation = await coupons.EvaluateAsync(request.CouponCode, cart.Subtotal, ct);
            if (evaluation.IsValid)
            {
                discount = evaluation.Discount;
                appliedCode = evaluation.Coupon!.Code;
                couponMessage = $"Coupon {appliedCode} applied";
            }
            else
            {
                couponMessage = evaluation.Error;
            }
        }

        var totals = CalculateTotals(cart.Subtotal, discount, request.DeliveryMethod);
        return new CheckoutQuoteDto
        {
            Subtotal = totals.Subtotal,
            Discount = totals.Discount,
            Shipping = totals.Shipping,
            Tax = totals.Tax,
            Total = totals.Total,
            CouponCode = appliedCode,
            CouponMessage = couponMessage,
            EstimatedDelivery = EstimateDelivery(request.DeliveryMethod, Now)
        };
    }

    public async Task<OrderDto> CreateAsync(Guid userId, CreateOrderRequest request, CancellationToken ct)
    {
        var orderNumber = await RunInTransactionWithRetryAsync(() => PlaceOrderAsync(userId, request, ct), ct);
        logger.LogInformation("Order {OrderNumber} placed by user {UserId}", orderNumber, userId);
        return await GetMyOrderAsync(userId, orderNumber, ct);
    }

    private async Task<string> PlaceOrderAsync(Guid userId, CreateOrderRequest request, CancellationToken ct)
    {
        var address = await db.Addresses.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId, ct)
                      ?? throw new NotFoundException("Address not found");

        var cartItems = await carts.GetItemsForCheckoutAsync(userId, ct);
        if (cartItems.Count == 0) throw new BusinessRuleException("Your cart is empty");

        var now = Now;
        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Pending,
            DeliveryMethod = request.DeliveryMethod,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            PlacedAt = now,
            EstimatedDelivery = EstimateDelivery(request.DeliveryMethod, now),
            ShippingAddress = new ShippingAddress
            {
                FullName = address.FullName,
                Phone = address.Phone,
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                State = address.State,
                PostalCode = address.PostalCode,
                Country = address.Country
            }
        };

        foreach (var item in cartItems)
        {
            var variant = item.Variant;
            var product = variant.Product;
            var displayName = $"{product.Name} ({variant.Name})";

            if (!variant.IsActive || !product.IsActive || !product.Category.IsActive)
            {
                throw new BusinessRuleException($"{displayName} is no longer available. Please remove it from your cart.");
            }

            var inventory = variant.Inventory;
            var available = inventory?.Available ?? 0;
            if (inventory is null || item.Quantity > available)
            {
                throw new ConflictException(available == 0
                    ? $"{displayName} is out of stock."
                    : $"Only {available} unit{(available == 1 ? "" : "s")} of {displayName} available.");
            }

            inventory.QuantityOnHand -= item.Quantity;

            var lineTotal = Pricing.Round(variant.Price * item.Quantity);
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                VariantId = variant.Id,
                ProductName = product.Name,
                ProductSlug = product.Slug,
                VariantName = variant.Name,
                Sku = variant.Sku,
                ImageUrl = product.OrderedImages().Select(i => i.Url).FirstOrDefault(),
                UnitPrice = variant.Price,
                Quantity = item.Quantity,
                LineTotal = lineTotal
            });
        }

        var subtotal = order.Items.Sum(i => i.LineTotal);
        decimal discount = 0;

        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var evaluation = await coupons.EvaluateAsync(request.CouponCode, subtotal, ct);
            if (!evaluation.IsValid) throw new BusinessRuleException(evaluation.Error!);

            var coupon = evaluation.Coupon!;
            // Atomic usage increment guarded by the usage limit (part of the surrounding transaction).
            var updated = await db.Coupons
                .Where(c => c.Id == coupon.Id && c.IsActive && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount + 1), ct);
            if (updated == 0) throw new BusinessRuleException("This coupon has reached its usage limit");

            discount = evaluation.Discount;
            order.CouponId = coupon.Id;
            order.CouponCode = coupon.Code;
        }

        var totals = CalculateTotals(subtotal, discount, request.DeliveryMethod);
        order.Subtotal = totals.Subtotal;
        order.Discount = totals.Discount;
        order.Shipping = totals.Shipping;
        order.Tax = totals.Tax;
        order.Total = totals.Total;

        var payment = await paymentGateway.ChargeAsync(
            new PaymentRequest(order.Id, request.PaymentMethod, order.Total, Commerce.Currency), ct);
        if (payment.Status == PaymentStatus.Failed)
        {
            throw new BusinessRuleException($"Payment failed: {payment.FailureReason ?? "the payment was declined"}");
        }

        order.Payment = new Payment
        {
            OrderId = order.Id,
            Method = request.PaymentMethod,
            Status = payment.Status,
            Amount = order.Total,
            TransactionReference = payment.TransactionReference,
            PaidAt = payment.PaidAt,
            FailureReason = payment.FailureReason
        };

        if (payment.Status == PaymentStatus.Paid)
        {
            // Prepaid orders are confirmed immediately; COD orders wait for admin confirmation.
            order.Status = OrderStatus.Confirmed;
            order.ConfirmedAt = now;
        }

        db.Orders.Add(order);
        db.CartItems.RemoveRange(cartItems);
        await db.SaveChangesAsync(ct);

        return order.OrderNumber; // generated by the database sequence and read back via RETURNING
    }

    public Task<PagedResult<OrderSummaryDto>> GetMyOrdersAsync(Guid userId, PagingQuery paging, CancellationToken ct) =>
        orders.GetSummariesAsync(orders.Query().Where(o => o.UserId == userId), paging.Page, paging.PageSize, includeCustomer: false, ct);

    public async Task<OrderDto> GetMyOrderAsync(Guid userId, string orderNumber, CancellationToken ct)
    {
        var order = await orders.GetByNumberAsync(orderNumber, userId, tracked: false, ct)
                    ?? throw new NotFoundException("Order not found");
        return order.ToDto(includeCustomer: false);
    }

    public async Task<OrderDto> CancelMyOrderAsync(Guid userId, string orderNumber, CancellationToken ct)
    {
        await RunInTransactionWithRetryAsync(async () =>
        {
            var order = await orders.GetByNumberAsync(orderNumber, userId, tracked: true, ct)
                        ?? throw new NotFoundException("Order not found");
            if (!CustomerCancellable.Contains(order.Status))
            {
                throw new BusinessRuleException("This order can no longer be cancelled");
            }

            await CancelCoreAsync(order, ct);
            await db.SaveChangesAsync(ct);
            return order.OrderNumber;
        }, ct);

        logger.LogInformation("Order {OrderNumber} cancelled by customer {UserId}", orderNumber, userId);
        return await GetMyOrderAsync(userId, orderNumber, ct);
    }

    // ---------- Admin ----------

    public Task<PagedResult<OrderSummaryDto>> SearchAsync(AdminOrderQuery q, CancellationToken ct)
    {
        var query = orders.Query();
        if (q.Status.HasValue)
        {
            var status = q.Status.Value;
            query = query.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = SearchPattern.Contains(q.Search);
            const string esc = SearchPattern.EscapeCharacter;
            query = query.Where(o =>
                EF.Functions.ILike(o.OrderNumber, pattern, esc) ||
                EF.Functions.ILike(o.User.Email, pattern, esc) ||
                EF.Functions.ILike(o.User.FirstName + " " + o.User.LastName, pattern, esc) ||
                EF.Functions.ILike(o.ShippingAddress.FullName, pattern, esc));
        }

        return orders.GetSummariesAsync(query, q.Page, q.PageSize, includeCustomer: true, ct);
    }

    public async Task<OrderDto> GetAsync(string orderNumber, CancellationToken ct)
    {
        var order = await orders.GetByNumberAsync(orderNumber, null, tracked: false, ct)
                    ?? throw new NotFoundException("Order not found");
        return order.ToDto(includeCustomer: true);
    }

    public async Task<OrderDto> UpdateStatusAsync(string orderNumber, UpdateOrderStatusRequest request, CancellationToken ct)
    {
        await RunInTransactionWithRetryAsync(async () =>
        {
            var order = await orders.GetByNumberAsync(orderNumber, null, tracked: true, ct)
                        ?? throw new NotFoundException("Order not found");

            var tracking = string.IsNullOrWhiteSpace(request.TrackingNumber) ? null : request.TrackingNumber.Trim();
            var target = request.Status;
            var now = Now;

            if (target == order.Status)
            {
                if (tracking is null) throw new BusinessRuleException($"Order is already {order.Status}");
            }
            else if (target == OrderStatus.Cancelled)
            {
                if (!AdminCancellable.Contains(order.Status))
                {
                    throw new BusinessRuleException($"An order that is {order.Status} cannot be cancelled");
                }
                await CancelCoreAsync(order, ct);
            }
            else
            {
                var currentIndex = Array.IndexOf(Flow, order.Status);
                var targetIndex = Array.IndexOf(Flow, target);
                if (currentIndex < 0 || targetIndex <= currentIndex)
                {
                    throw new BusinessRuleException($"Cannot change order status from {order.Status} to {target}");
                }

                // Forward move: stamp every step reached (including skipped intermediate steps).
                for (var i = currentIndex + 1; i <= targetIndex; i++) StampStatus(order, Flow[i], now);
                order.Status = target;

                if (target == OrderStatus.Delivered && order.Payment is { Method: PaymentMethod.CashOnDelivery, Status: PaymentStatus.Pending } cod)
                {
                    cod.Status = PaymentStatus.Paid;
                    cod.PaidAt = now;
                    cod.TransactionReference ??= $"COD-{order.OrderNumber}";
                }
            }

            if (tracking is not null) order.TrackingNumber = tracking;
            await db.SaveChangesAsync(ct);
            return order.OrderNumber;
        }, ct);

        logger.LogInformation("Order {OrderNumber} status changed to {Status}", orderNumber, request.Status);
        return await GetAsync(orderNumber, ct);
    }

    // ---------- Internals ----------

    private static void StampStatus(Order order, OrderStatus status, DateTime now)
    {
        switch (status)
        {
            case OrderStatus.Confirmed: order.ConfirmedAt ??= now; break;
            case OrderStatus.Processing: order.ProcessingAt ??= now; break;
            case OrderStatus.Shipped: order.ShippedAt ??= now; break;
            case OrderStatus.OutForDelivery: order.OutForDeliveryAt ??= now; break;
            case OrderStatus.Delivered: order.DeliveredAt ??= now; break;
        }
    }

    /// <summary>Marks the order cancelled, restocks inventory, releases coupon usage and refunds prepaid payments.</summary>
    private async Task CancelCoreAsync(Order order, CancellationToken ct)
    {
        var quantities = order.Items.GroupBy(i => i.VariantId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var variantIds = quantities.Keys.ToList();
        var inventories = await db.Inventory.Where(i => variantIds.Contains(i.VariantId)).ToListAsync(ct);
        foreach (var inventory in inventories)
        {
            inventory.QuantityOnHand += quantities[inventory.VariantId];
        }

        if (order.CouponId.HasValue)
        {
            var couponId = order.CouponId.Value;
            await db.Coupons.IgnoreQueryFilters()
                .Where(c => c.Id == couponId && c.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount - 1), ct);
        }

        if (order.Payment is { Status: PaymentStatus.Paid } payment)
        {
            var refund = await paymentGateway.RefundAsync(payment, ct);
            payment.Status = refund.Status;
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = Now;
    }

    /// <summary>
    /// Runs the operation in a DB transaction inside the Npgsql execution strategy (retry-on-failure requires this),
    /// retrying up to 3 times when an optimistic concurrency conflict (inventory xmin) is detected.
    /// </summary>
    private async Task<T> RunInTransactionWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            for (var attempt = 1; ; attempt++)
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    var result = await operation();
                    await transaction.CommitAsync(ct);
                    return result;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    if (attempt >= MaxConcurrencyAttempts)
                    {
                        logger.LogWarning(ex, "Giving up after {Attempts} concurrency conflicts", attempt);
                        throw new ConflictException("Stock levels changed while placing your order. Please review your cart and try again.");
                    }
                    logger.LogInformation("Concurrency conflict (attempt {Attempt}); retrying", attempt);
                }
            }
        });
    }
}
