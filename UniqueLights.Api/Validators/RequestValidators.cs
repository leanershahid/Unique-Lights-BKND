using FluentValidation;
using UniqueLights.Api.DTOs.Admin;
using UniqueLights.Api.DTOs.Auth;
using UniqueLights.Api.DTOs.Catalog;
using UniqueLights.Api.DTOs.Customers;
using UniqueLights.Api.DTOs.Orders;
using UniqueLights.Api.DTOs.Reviews;
using UniqueLights.Api.DTOs.Shopping;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Validators;

internal static class Rules
{
    public const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";
    public const string PhonePattern = @"^\+?[0-9 ()\-]{7,20}$";

    public static IRuleBuilderOptions<T, string> Password<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128).WithMessage("Password must be at most 128 characters.")
            .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");

    public static IRuleBuilderOptions<T, string?> OptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => string.IsNullOrWhiteSpace(v) || System.Text.RegularExpressions.Regex.IsMatch(v, PhonePattern))
            .WithMessage("Phone number is not valid.");

    public static IRuleBuilderOptions<T, string?> ImageUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(1024)
            .Must(v => string.IsNullOrWhiteSpace(v) || BeValidImageUrl(v))
            .WithMessage("Must be an absolute http(s) URL or a site-relative path starting with '/'.");

    public static bool BeValidImageUrl(string url) =>
        (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)) ||
        (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

    public static IRuleBuilderOptions<T, decimal> Money<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0).LessThan(10_000_000_000m).PrecisionScale(12, 2, true);

    public static IRuleBuilderOptions<T, decimal?> Money<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.GreaterThanOrEqualTo(0).LessThan(10_000_000_000m).PrecisionScale(12, 2, true);
}

// ---------- Auth ----------

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(32).OptionalPhone();
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

// ---------- Customers ----------

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Phone).MaximumLength(32).OptionalPhone();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).Password()
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from the current password.");
    }
}

public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Label).MaximumLength(50);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(32).Matches(Rules.PhonePattern).WithMessage("Phone number is not valid.");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.PostalCode).Matches("^[1-9][0-9]{5}$").WithMessage("Enter a valid 6-digit PIN code.")
            .When(x => string.Equals(x.Country?.Trim(), "India", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
    }
}

// ---------- Catalog ----------

public sealed class ProductQueryParametersValidator : AbstractValidator<ProductQueryParameters>
{
    public ProductQueryParametersValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Category).MaximumLength(140);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(x => x.MinPrice)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithMessage("maxPrice must be greater than or equal to minPrice.");
        RuleFor(x => x.Brands).MaximumLength(1000);
        RuleFor(x => x.Finishes).MaximumLength(1000);
        RuleFor(x => x.ColorTemperatures).MaximumLength(1000);
        RuleFor(x => x.Sort)
            .Must(s => ProductSorts.All.Contains(s!.Trim().ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort))
            .WithMessage($"sort must be one of: {string.Join(", ", ProductSorts.All)}.");
    }
}

public sealed class CreateReviewRequestValidator : AbstractValidator<CreateReviewRequest>
{
    public CreateReviewRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Title).MaximumLength(120);
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

// ---------- Cart / wishlist / coupons ----------

public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(x => x.VariantId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 99);
    }
}

public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, 99);
    }
}

public sealed class MergeCartRequestValidator : AbstractValidator<MergeCartRequest>
{
    public MergeCartRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= 100).WithMessage("A cart can contain at most 100 lines.");
        RuleForEach(x => x.Items).SetValidator(new AddCartItemRequestValidator());
    }
}

public sealed class AddWishlistItemRequestValidator : AbstractValidator<AddWishlistItemRequest>
{
    public AddWishlistItemRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}

public sealed class ValidateCouponRequestValidator : AbstractValidator<ValidateCouponRequest>
{
    public ValidateCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.Subtotal).Money();
    }
}

// ---------- Orders ----------

public sealed class CheckoutQuoteRequestValidator : AbstractValidator<CheckoutQuoteRequest>
{
    public CheckoutQuoteRequestValidator()
    {
        RuleFor(x => x.DeliveryMethod).IsInEnum();
        RuleFor(x => x.CouponCode).MaximumLength(40);
    }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.AddressId).NotEmpty();
        RuleFor(x => x.DeliveryMethod).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.CouponCode).MaximumLength(40);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class UpdateOrderStatusRequestValidator : AbstractValidator<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum().NotEqual(OrderStatus.Pending).WithMessage("An order cannot be moved back to Pending.");
        RuleFor(x => x.TrackingNumber).MaximumLength(100);
    }
}

public sealed class AdminOrderQueryValidator : AbstractValidator<AdminOrderQuery>
{
    public AdminOrderQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status).IsInEnum();
    }
}

// ---------- Admin catalogue ----------

public sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).MaximumLength(220).Matches(Rules.SlugPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug may only contain lowercase letters, digits and single hyphens.");
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("SKU may only contain letters, digits, '.', '_' and '-'.");
        RuleFor(x => x.ShortDescription).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(10000);
        RuleFor(x => x.Brand).MaximumLength(100);
        RuleFor(x => x.BasePrice).Money().GreaterThan(0);
        RuleFor(x => x.CompareAtPrice).Money();
        RuleFor(x => x.CompareAtPrice).GreaterThan(x => x.BasePrice)
            .When(x => x.CompareAtPrice.HasValue)
            .WithMessage("compareAtPrice must be greater than basePrice.");
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Specifications)
            .Must(s => s is null || s.Count <= 40).WithMessage("At most 40 specifications are allowed.")
            .Must(s => s is null || s.All(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Key.Length <= 60))
            .WithMessage("Specification names must be 1-60 characters.")
            .Must(s => s is null || s.All(kv => kv.Value is not null && kv.Value.Length <= 500))
            .WithMessage("Specification values must be at most 500 characters.");
    }
}

public sealed class UpsertVariantRequestValidator : AbstractValidator<UpsertVariantRequest>
{
    public UpsertVariantRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("SKU may only contain letters, digits, '.', '_' and '-'.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Color).MaximumLength(50);
        RuleFor(x => x.Finish).MaximumLength(50);
        RuleFor(x => x.ColorTemperature).MaximumLength(20);
        RuleFor(x => x.Wattage).InclusiveBetween(1, 10000);
        RuleFor(x => x.Price).Money().GreaterThan(0);
        RuleFor(x => x.CompareAtPrice).Money();
        RuleFor(x => x.CompareAtPrice).GreaterThan(x => x.Price)
            .When(x => x.CompareAtPrice.HasValue)
            .WithMessage("compareAtPrice must be greater than price.");
        RuleFor(x => x.InitialStock).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 100_000);
    }
}

public sealed class UploadImageRequestValidator : AbstractValidator<UploadImageRequest>
{
    public UploadImageRequestValidator()
    {
        RuleFor(x => x.File).NotNull().WithMessage("An image file is required.");
        RuleFor(x => x.AltText).MaximumLength(250);
    }
}

public sealed class AddImageUrlRequestValidator : AbstractValidator<AddImageUrlRequest>
{
    public AddImageUrlRequestValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MaximumLength(1024)
            .Must(Rules.BeValidImageUrl).When(x => !string.IsNullOrWhiteSpace(x.Url), ApplyConditionTo.CurrentValidator)
            .WithMessage("Must be an absolute http(s) URL or a site-relative path starting with '/'.");
        RuleFor(x => x.AltText).MaximumLength(250);
    }
}

public sealed class UpdateImageRequestValidator : AbstractValidator<UpdateImageRequest>
{
    public UpdateImageRequestValidator()
    {
        RuleFor(x => x.AltText).MaximumLength(250);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }
}

public sealed class AdminProductQueryValidator : AbstractValidator<AdminProductQuery>
{
    private static readonly string[] Statuses = ["active", "inactive", "all"];

    public AdminProductQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Status)
            .Must(s => Statuses.Contains(s!.Trim().ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("status must be one of: active, inactive, all.");
    }
}

public sealed class UpsertCategoryRequestValidator : AbstractValidator<UpsertCategoryRequest>
{
    public UpsertCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).MaximumLength(140).Matches(Rules.SlugPattern)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug may only contain lowercase letters, digits and single hyphens.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ImageUrl).ImageUrl();
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }
}

// ---------- Admin customers / inventory / coupons ----------

public sealed class AdminCustomerQueryValidator : AbstractValidator<AdminCustomerQuery>
{
    public AdminCustomerQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class InventoryQueryValidator : AbstractValidator<InventoryQuery>
{
    public InventoryQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public sealed class UpdateInventoryRequestValidator : AbstractValidator<UpdateInventoryRequest>
{
    public UpdateInventoryRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !(x.QuantityOnHand.HasValue && x.Adjustment.HasValue))
            .WithMessage("Provide either quantityOnHand or adjustment, not both.")
            .Must(x => x.QuantityOnHand.HasValue || x.Adjustment.HasValue || x.LowStockThreshold.HasValue)
            .WithMessage("Provide quantityOnHand, adjustment or lowStockThreshold.")
            .OverridePropertyName("request");
        RuleFor(x => x.QuantityOnHand).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.Adjustment).InclusiveBetween(-1_000_000, 1_000_000).NotEqual(0);
        RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 100_000);
    }
}

public sealed class UpsertCouponRequestValidator : AbstractValidator<UpsertCouponRequest>
{
    public UpsertCouponRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Code may only contain letters, digits, '_' and '-'.");
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.DiscountType).IsInEnum();
        RuleFor(x => x.Value).Money().GreaterThan(0);
        RuleFor(x => x.Value).LessThanOrEqualTo(100)
            .When(x => x.DiscountType == DiscountType.Percentage)
            .WithMessage("A percentage discount cannot exceed 100.");
        RuleFor(x => x.MinOrderAmount).Money();
        RuleFor(x => x.MaxDiscountAmount).Money().GreaterThan(0);
        RuleFor(x => x.UsageLimit).GreaterThan(0);
        RuleFor(x => x.EndsAt).GreaterThan(x => x.StartsAt)
            .When(x => x.StartsAt.HasValue && x.EndsAt.HasValue)
            .WithMessage("endsAt must be after startsAt.");
    }
}

public sealed class UpdateCustomerStatusRequestValidator : AbstractValidator<UpdateCustomerStatusRequest>
{
    public UpdateCustomerStatusRequestValidator()
    {
        RuleFor(x => x.IsActive).NotNull();
    }
}
