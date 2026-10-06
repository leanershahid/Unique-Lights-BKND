using System.Globalization;
using Microsoft.EntityFrameworkCore;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Shopping;
using UniqueLights.Api.Entities;
using UniqueLights.Api.Exceptions;
using UniqueLights.Api.Services.Common;

namespace UniqueLights.Api.Services;

public sealed record CouponEvaluation(Coupon? Coupon, decimal Discount, string? Error)
{
    public bool IsValid => Coupon is not null && Error is null;
}

public interface ICouponService
{
    /// <summary>Evaluates a code against a subtotal without throwing.</summary>
    Task<CouponEvaluation> EvaluateAsync(string code, decimal subtotal, CancellationToken ct, bool track = false);

    Task<CouponValidationDto> ValidateAsync(ValidateCouponRequest request, CancellationToken ct);

    // Admin
    Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken ct);
    Task<CouponDto> CreateAsync(UpsertCouponRequest request, CancellationToken ct);
    Task<CouponDto> UpdateAsync(Guid id, UpsertCouponRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class CouponService(AppDbContext db, TimeProvider time) : ICouponService
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public async Task<CouponEvaluation> EvaluateAsync(string code, decimal subtotal, CancellationToken ct, bool track = false)
    {
        var normalized = Coupon.NormalizeCode(code);
        var query = track ? db.Coupons : db.Coupons.AsNoTracking();
        var coupon = await query.FirstOrDefaultAsync(c => c.Code == normalized, ct);
        if (coupon is null) return new CouponEvaluation(null, 0, "Coupon code is invalid");

        var error = Check(coupon, subtotal, time.GetUtcNow().UtcDateTime);
        return error is null
            ? new CouponEvaluation(coupon, CalculateDiscount(coupon, subtotal), null)
            : new CouponEvaluation(coupon, 0, error);
    }

    public async Task<CouponValidationDto> ValidateAsync(ValidateCouponRequest request, CancellationToken ct)
    {
        var result = await EvaluateAsync(request.Code, request.Subtotal, ct);
        if (!result.IsValid) throw new BusinessRuleException(result.Error!);

        return new CouponValidationDto
        {
            Code = result.Coupon!.Code,
            Description = result.Coupon.Description,
            DiscountAmount = result.Discount
        };
    }

    public static string? Check(Coupon coupon, decimal subtotal, DateTime now)
    {
        if (!coupon.IsActive) return "Coupon code is invalid";
        if (coupon.StartsAt.HasValue && now < coupon.StartsAt.Value) return "This coupon is not active yet";
        if (coupon.EndsAt.HasValue && now > coupon.EndsAt.Value) return "This coupon has expired";
        if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit.Value) return "This coupon has reached its usage limit";
        if (coupon.MinOrderAmount.HasValue && subtotal < coupon.MinOrderAmount.Value)
        {
            return $"A minimum order of {FormatInr(coupon.MinOrderAmount.Value)} is required for this coupon";
        }
        return null;
    }

    public static decimal CalculateDiscount(Coupon coupon, decimal subtotal)
    {
        if (subtotal <= 0) return 0;
        var discount = coupon.DiscountType == DiscountType.Percentage
            ? subtotal * coupon.Value / 100m
            : coupon.Value;

        if (coupon.MaxDiscountAmount.HasValue) discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
        return Pricing.Round(Math.Min(discount, subtotal));
    }

    private static string FormatInr(decimal amount) =>
        "₹" + amount.ToString(amount == decimal.Truncate(amount) ? "#,##0" : "#,##0.00", India);

    // ---------- Admin ----------

    public async Task<IReadOnlyList<CouponDto>> GetAllAsync(CancellationToken ct)
    {
        var coupons = await db.Coupons.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return coupons.Select(c => c.ToDto()).ToList();
    }

    public async Task<CouponDto> CreateAsync(UpsertCouponRequest request, CancellationToken ct)
    {
        var code = Coupon.NormalizeCode(request.Code);
        if (await db.Coupons.AnyAsync(c => c.Code == code, ct)) throw new ConflictException($"Coupon code {code} already exists");

        var coupon = new Coupon();
        Apply(coupon, request, code);
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(ct);
        return coupon.ToDto();
    }

    public async Task<CouponDto> UpdateAsync(Guid id, UpsertCouponRequest request, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Coupon not found");
        var code = Coupon.NormalizeCode(request.Code);
        if (code != coupon.Code && await db.Coupons.AnyAsync(c => c.Code == code && c.Id != id, ct))
        {
            throw new ConflictException($"Coupon code {code} already exists");
        }

        Apply(coupon, request, code);
        await db.SaveChangesAsync(ct);
        return coupon.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Coupon not found");
        coupon.MarkDeleted();
        coupon.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Coupon coupon, UpsertCouponRequest r, string code)
    {
        coupon.Code = code;
        coupon.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        coupon.DiscountType = r.DiscountType;
        coupon.Value = r.Value;
        coupon.MinOrderAmount = r.MinOrderAmount;
        coupon.MaxDiscountAmount = r.MaxDiscountAmount;
        coupon.StartsAt = r.StartsAt;
        coupon.EndsAt = r.EndsAt;
        coupon.UsageLimit = r.UsageLimit;
        coupon.IsActive = r.IsActive;
    }
}
