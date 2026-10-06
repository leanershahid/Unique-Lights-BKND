using Microsoft.AspNetCore.Http;

namespace UniqueLights.Api.Configurations;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumSecretLength = 32;

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "UniqueLights";
    public string Audience { get; set; } = "UniqueLights.Web";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    /// <summary>Project URL, e.g. https://abcd1234.supabase.co</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Service role key (server-side only, never expose to the browser).</summary>
    public string ServiceKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "product-images";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ServiceKey) && !string.IsNullOrWhiteSpace(Bucket);
}

public sealed class CommerceOptions
{
    public const string SectionName = "Commerce";

    public string Currency { get; set; } = "INR";
    public decimal TaxRate { get; set; } = 0.18m;
    public decimal FreeShippingThreshold { get; set; } = 2000m;
    public decimal StandardShipping { get; set; } = 149m;
    public decimal ExpressShipping { get; set; } = 349m;
    public int StandardDeliveryDays { get; set; } = 5;
    public int ExpressDeliveryDays { get; set; } = 2;
}

/// <summary>
/// Settings for the refresh-token cookie. For local dev (same-site, http) use SameSite=Lax, Secure=false.
/// In production where the SPA is served from a different site than the API use SameSite=None and Secure=true (HTTPS required).
/// </summary>
public sealed class RefreshCookieOptions
{
    public const string SectionName = "RefreshCookie";
    public const string CookieName = "ul_refresh";
    public const string CookiePath = "/api/auth";

    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;
    public bool Secure { get; set; }
}

public sealed class CorsSettings
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; } = true;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>When true, pending EF Core migrations are applied at startup. Defaults to true in Development only.</summary>
    public bool? MigrateOnStartup { get; set; }
}
