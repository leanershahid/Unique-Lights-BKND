using System.Security.Cryptography;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Services;

public sealed record PaymentRequest(Guid OrderId, PaymentMethod Method, decimal Amount, string Currency);

public sealed record PaymentResult(PaymentStatus Status, string? TransactionReference, DateTime? PaidAt, string? FailureReason);

/// <summary>
/// Abstraction over the payment provider. Replace <see cref="DevelopmentPaymentGateway"/> with a real
/// implementation (e.g. Razorpay / Stripe) by changing the DI registration in ServiceCollectionExtensions.
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
    Task<PaymentResult> RefundAsync(Payment payment, CancellationToken ct);
}

/// <summary>
/// DEVELOPMENT STUB - does not move any money.
/// CashOnDelivery → Pending (collected on delivery). Card / Upi → Paid immediately with a fake "DEV-xxxx" reference.
/// </summary>
public sealed class DevelopmentPaymentGateway(TimeProvider time, ILogger<DevelopmentPaymentGateway> logger) : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct)
    {
        if (request.Method == PaymentMethod.CashOnDelivery)
        {
            return Task.FromResult(new PaymentResult(PaymentStatus.Pending, null, null, null));
        }

        var reference = "DEV-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        logger.LogWarning("DevelopmentPaymentGateway approved {Method} payment of {Amount} {Currency} for order {OrderId} ({Reference}). No real charge was made.",
            request.Method, request.Amount, request.Currency, request.OrderId, reference);
        return Task.FromResult(new PaymentResult(PaymentStatus.Paid, reference, time.GetUtcNow().UtcDateTime, null));
    }

    public Task<PaymentResult> RefundAsync(Payment payment, CancellationToken ct)
    {
        logger.LogWarning("DevelopmentPaymentGateway refunded payment {Reference}. No real refund was made.", payment.TransactionReference);
        return Task.FromResult(new PaymentResult(PaymentStatus.Refunded, payment.TransactionReference, payment.PaidAt, null));
    }
}
