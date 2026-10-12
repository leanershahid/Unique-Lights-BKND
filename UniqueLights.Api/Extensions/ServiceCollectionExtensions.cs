using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.Data;
using UniqueLights.Api.DTOs.Common;
using UniqueLights.Api.Repositories;
using UniqueLights.Api.Services;
using UniqueLights.Api.Services.Admin;
using UniqueLights.Api.Validators;

namespace UniqueLights.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicy = "Frontend";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Fails fast with a clear message when mandatory secrets are missing.</summary>
    public static void ValidateRequiredConfiguration(this IConfiguration configuration)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
        {
            problems.Add("ConnectionStrings:DefaultConnection is empty. Set the env var ConnectionStrings__DefaultConnection (or use user-secrets / .env).");
        }

        var secret = configuration[$"{JwtOptions.SectionName}:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < JwtOptions.MinimumSecretLength)
        {
            problems.Add($"Jwt:Secret is missing or shorter than {JwtOptions.MinimumSecretLength} characters. Set the env var Jwt__Secret to a long random value.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid configuration:" + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", problems));
        }
    }

    public static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.Secret.Length >= JwtOptions.MinimumSecretLength, "Jwt:Secret must be at least 32 characters")
            .Validate(o => o.AccessTokenMinutes is > 0 and <= 1440, "Jwt:AccessTokenMinutes must be between 1 and 1440")
            .Validate(o => o.RefreshTokenDays is > 0 and <= 365, "Jwt:RefreshTokenDays must be between 1 and 365")
            .ValidateOnStart();
        services.AddOptions<SupabaseOptions>().Bind(configuration.GetSection(SupabaseOptions.SectionName));
        services.AddOptions<CommerceOptions>().Bind(configuration.GetSection(CommerceOptions.SectionName))
            .Validate(o => o.TaxRate is >= 0 and < 1, "Commerce:TaxRate must be between 0 and 1")
            .Validate(o => o.StandardShipping >= 0 && o.ExpressShipping >= 0 && o.FreeShippingThreshold >= 0, "Commerce shipping values must be >= 0")
            .ValidateOnStart();
        services.AddOptions<StoreOptions>().Bind(configuration.GetSection(StoreOptions.SectionName))
            .Validate(o => o.ReturnDays >= 0, "Store:ReturnDays must be >= 0")
            .ValidateOnStart();
        services.AddOptions<RefreshCookieOptions>().Bind(configuration.GetSection(RefreshCookieOptions.SectionName))
            .Validate(o => o.SameSite != SameSiteMode.None || o.Secure, "RefreshCookie: SameSite=None requires Secure=true")
            .ValidateOnStart();
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var csb = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (environment.IsDevelopment()) csb.IncludeErrorDetail = true;
        var connectionString = csb.ConnectionString;

        services.AddSingleton<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                    npgsql.MigrationsHistoryTable(MigrationsHistoryTable);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditInterceptor>())
                .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        });

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<DbSeeder>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.Secret),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaimTypes.Name,
                    RoleClaimType = AppClaimTypes.Role
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IPasswordHashingService, PasswordHashingService>();
        return services;
    }

    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IOrderService, OrderService>();

        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAdminProductService, AdminProductService>();
        services.AddScoped<IAdminCategoryService, AdminCategoryService>();
        services.AddScoped<IAdminCustomerService, AdminCustomerService>();
        services.AddScoped<IInventoryService, InventoryService>();

        // DEVELOPMENT STUB: swap for a real gateway implementation in production.
        services.AddSingleton<IPaymentGateway, DevelopmentPaymentGateway>();

        services.AddHttpClient<IStorageService, SupabaseStorageService>(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(ServiceLifetime.Singleton);
        return services;
    }

    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services
            .AddControllers(options =>
            {
                options.Filters.Add<FluentValidationActionFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                // Body-less client errors (404/415...) are wrapped in the envelope by ExceptionHandlingMiddleware.
                options.SuppressMapClientErrors = true;
                options.InvalidModelStateResponseFactory = context =>
                {
                    var traceId = System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
                    var body = ApiResponse.Fail("Validation failed", traceId, context.ModelState.ToCamelCaseDictionary());
                    return new BadRequestObjectResult(body);
                };
            });
        return services;
    }

    public static IServiceCollection AddAppCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>()?.AllowedOrigins
                          .Where(o => !string.IsNullOrWhiteSpace(o))
                          .Select(o => o.TrimEnd('/'))
                          .ToArray() ?? [];

        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders("Retry-After")
                .SetPreflightMaxAge(TimeSpan.FromHours(1));
        }));
        return services;
    }

    public static IServiceCollection AddAppSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Unique Lights API",
                Version = "v1",
                Description = "Lighting e-commerce API. Every response uses the ApiResponse envelope (see docs/API.md)."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the accessToken returned by /api/auth/login."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });

            options.CustomSchemaIds(type => SchemaId(type));
        });
        return services;
    }

    private static string SchemaId(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        return name + "Of" + string.Join("And", type.GetGenericArguments().Select(SchemaId));
    }
}
