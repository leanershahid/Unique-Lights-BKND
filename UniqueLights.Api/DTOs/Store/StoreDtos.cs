namespace UniqueLights.Api.DTOs.Store;

/// <summary>Public store settings the website needs: pricing rules, delivery promises and contact details.</summary>
public sealed record StoreSettingsDto(
    string Name,
    string? Tagline,
    string Currency,
    decimal TaxRate,
    decimal FreeShippingThreshold,
    decimal StandardShipping,
    decimal ExpressShipping,
    int StandardDeliveryDays,
    int ExpressDeliveryDays,
    int ReturnDays,
    string? SupportEmail,
    string? SupportPhone,
    string? Address,
    string? BusinessHours);
