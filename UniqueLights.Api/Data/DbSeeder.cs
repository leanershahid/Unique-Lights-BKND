using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniqueLights.Api.Authentication;
using UniqueLights.Api.Configurations;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Data;

/// <summary>
/// Idempotent seeder. Roles are seeded by the migration (HasData) and re-checked here.
/// The admin user is only created when Seed:AdminEmail and Seed:AdminPassword are both configured.
/// The demo catalogue (categories, products, coupons) is only inserted when the products table is empty.
/// No customers, orders or reviews are ever seeded.
/// </summary>
public sealed class DbSeeder(
    AppDbContext db,
    IPasswordHashingService passwords,
    IOptions<SeedOptions> seedOptions,
    ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedRolesAsync(ct);
        await SeedAdminAsync(ct);
        await SeedCatalogueAsync(ct);
        await SeedCouponsAsync(ct);
    }

    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var existing = await db.Roles.Select(r => r.Id).ToListAsync(ct);
        if (!existing.Contains(Roles.CustomerId)) db.Roles.Add(new Role { Id = Roles.CustomerId, Name = Roles.Customer });
        if (!existing.Contains(Roles.AdminId)) db.Roles.Add(new Role { Id = Roles.AdminId, Name = Roles.Admin });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var options = seedOptions.Value;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogInformation("Seed:AdminEmail / Seed:AdminPassword not configured; skipping admin user seeding");
            return;
        }

        if (options.AdminPassword.Length < 8)
        {
            logger.LogWarning("Seed:AdminPassword must be at least 8 characters; skipping admin user seeding");
            return;
        }

        var normalized = User.NormalizeEmail(options.AdminEmail);
        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            user = new User
            {
                Email = options.AdminEmail.Trim(),
                NormalizedEmail = normalized,
                FirstName = "Store",
                LastName = "Admin",
                IsActive = true
            };
            user.PasswordHash = passwords.Hash(user, options.AdminPassword);
            db.Users.Add(user);
            logger.LogInformation("Seeding admin user {Email}", user.Email);
        }

        foreach (var roleId in new[] { Roles.AdminId, Roles.CustomerId })
        {
            if (user.UserRoles.All(r => r.RoleId != roleId))
            {
                user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
            }
        }

        // Never overwrite an existing admin's password from configuration.
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCouponsAsync(CancellationToken ct)
    {
        var codes = await db.Coupons.IgnoreQueryFilters().Select(c => c.Code).ToListAsync(ct);
        if (!codes.Contains("WELCOME10"))
        {
            db.Coupons.Add(new Coupon
            {
                Code = "WELCOME10",
                Description = "10% off your first order (up to ₹1,000) on orders above ₹1,500",
                DiscountType = DiscountType.Percentage,
                Value = 10,
                MinOrderAmount = 1500,
                MaxDiscountAmount = 1000,
                IsActive = true
            });
        }
        if (!codes.Contains("FESTIVE500"))
        {
            db.Coupons.Add(new Coupon
            {
                Code = "FESTIVE500",
                Description = "Flat ₹500 off on orders above ₹5,000",
                DiscountType = DiscountType.FixedAmount,
                Value = 500,
                MinOrderAmount = 5000,
                IsActive = true
            });
        }
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- catalogue

    private sealed record Img(string Url, string Alt);

    private sealed record Var(
        string Sku, string Name, decimal Price, decimal? Compare, int Stock,
        string? Finish = null, string? Color = null, string? Temperature = null, int? Wattage = null, int LowStock = 5);

    private sealed record Prod(
        string CategorySlug, string Name, string Slug, string Sku, string Brand, string Short, string Description,
        bool Featured, bool IsNew, Dictionary<string, string> Specs, Img[] Images, Var[] Variants);

    private static readonly (string Slug, string Name, string Description, string Image)[] CategorySeed =
    [
        ("pendant-lights", "Pendant Lights", "Statement pendants for dining tables, kitchen islands and stairwells.", "/images/pendant-paper.jpg"),
        ("chandeliers", "Chandeliers", "Crystal and brass chandeliers that anchor a room.", "/images/chandelier.jpg"),
        ("wall-lights", "Wall Lights", "Sconces and picture lights for layered, glare-free lighting.", "/images/sconce-warm.jpg"),
        ("floor-lamps", "Floor Lamps", "Reading, task and arc floor lamps.", "/images/floor-lamp.jpg"),
        ("table-lamps", "Table Lamps", "Bedside and console lamps with soft, diffused light.", "/images/hero-living.jpg"),
        ("ceiling-lights", "Ceiling Lights", "Flush and semi-flush LED fixtures for everyday rooms.", "/images/room-living.jpg"),
        ("outdoor-lights", "Outdoor Lights", "Weather-rated lights for balconies, gardens and entrances.", "/images/room-loft.jpg"),
        ("smart-bulbs", "Smart Bulbs", "App and voice controlled bulbs with tunable white and colour.", "/images/room-kitchen.jpg")
    ];

    private static Dictionary<string, string> Specs(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => i.Value);

    private static readonly Prod[] ProductSeed =
    [
        new("pendant-lights", "Kumo Woven Paper Pendant", "kumo-woven-paper-pendant", "UL-PND-KUMO", "Unique Lights Atelier",
            "Hand-woven paper shade that glows like a lantern.",
            "Kumo is hand-woven from washi-style paper cord over a lightweight steel frame. Switched on, the shade turns into a soft, even orb of light that flatters dining tables and bedrooms alike. Supplied with a 1.5 m adjustable fabric cable and ceiling canopy.",
            true, false,
            Specs(("Wattage", "Max 40 W (E27)"), ("Lumens", "Up to 806 lm with recommended bulb"), ("Colour temperature", "Bulb dependent (2700K recommended)"),
                  ("Material", "Paper cord, powder-coated steel"), ("Finish", "Natural white"), ("Dimensions", "Ø40 cm / Ø60 cm"),
                  ("Bulb base", "E27"), ("Dimmable", "Yes, with dimmable bulb"), ("Warranty", "2 years")),
            [new("/images/pendant-paper.jpg", "Glowing white woven paper pendant light"),
             new("/images/room-living.jpg", "Bright living room lit by a drum pendant")],
            [new("UL-PND-KUMO-40", "Small Ø40 cm", 3499m, null, 24, Finish: "Natural White", Color: "White"),
             new("UL-PND-KUMO-60", "Large Ø60 cm", 4999m, 5999m, 4, Finish: "Natural White", Color: "White")]),

        new("pendant-lights", "Hana Laser-Cut Pendant", "hana-laser-cut-pendant", "UL-PND-HANA", "Unique Lights Atelier",
            "Laser-cut flower pendant that throws patterned light.",
            "Hana's petals are laser-cut from 1 mm steel and layered to form a sculptural bloom. The perforations scatter a delicate pattern across walls and ceiling, making it a centrepiece for entryways and dining rooms.",
            true, true,
            Specs(("Wattage", "Max 60 W (E27)"), ("Lumens", "Up to 806 lm with recommended bulb"), ("Colour temperature", "Bulb dependent"),
                  ("Material", "Laser-cut steel"), ("Finish", "Brass or Matte Black"), ("Dimensions", "Ø45 × H38 cm"),
                  ("Bulb base", "E27"), ("Dimmable", "Yes, with dimmable bulb"), ("Warranty", "3 years")),
            [new("/images/pendant-hex.jpg", "Gold laser-cut flower pendant light"),
             new("/images/room-loft.jpg", "Open loft with staircase and pendant lighting")],
            [new("UL-PND-HANA-BR", "Brass", 8999m, 10999m, 12, Finish: "Brass", Color: "Gold"),
             new("UL-PND-HANA-MB", "Matte Black", 8499m, null, 3, Finish: "Matte Black", Color: "Black")]),

        new("pendant-lights", "Lumio Glass Dome Pendant", "lumio-glass-dome-pendant", "LM-PND-DOME", "Lumio",
            "Mouth-blown glass dome, made for kitchen islands.",
            "A mouth-blown glass dome on a slim metal gallery. Hang two or three in a row over a kitchen island for bright, even task light with a soft halo. Cable can be shortened on installation.",
            false, true,
            Specs(("Wattage", "Max 40 W (E27)"), ("Lumens", "Up to 806 lm with recommended bulb"), ("Colour temperature", "Bulb dependent (3000K recommended)"),
                  ("CRI", "Bulb dependent"), ("Material", "Mouth-blown glass, steel"), ("Dimensions", "Ø25 × H28 cm"),
                  ("Bulb base", "E27"), ("Dimmable", "Yes, with dimmable bulb"), ("Warranty", "2 years")),
            [new("/images/room-kitchen.jpg", "White kitchen with glass pendants over the island"),
             new("/images/room-living.jpg", "Bright living room with a drum pendant")],
            [new("LM-PND-DOME-CL", "Clear Glass", 4299m, null, 18, Finish: "Chrome", Color: "Clear"),
             new("LM-PND-DOME-SM", "Smoked Glass", 4599m, null, 9, Finish: "Matte Black", Color: "Smoke")]),

        new("pendant-lights", "Nordlys Linen Drum Pendant", "nordlys-linen-drum-pendant", "NL-PND-DRUM", "Nordlys",
            "Wide linen drum for soft, room-filling light.",
            "A generous 55 cm linen drum with a diffuser disc that hides the bulb from below. Ideal as the main light in living rooms and bedrooms where you want brightness without glare.",
            false, false,
            Specs(("Wattage", "Max 3 × 40 W (E27)"), ("Lumens", "Up to 2400 lm"), ("Colour temperature", "Bulb dependent"),
                  ("Material", "Linen, steel, acrylic diffuser"), ("Finish", "Linen White or Charcoal"), ("Dimensions", "Ø55 × H25 cm"),
                  ("Bulb base", "3 × E27"), ("Dimmable", "Yes, with dimmable bulbs"), ("Warranty", "2 years")),
            [new("/images/room-living.jpg", "Bright living room with a linen drum pendant"),
             new("/images/pendant-paper.jpg", "Soft glowing fabric pendant shade")],
            [new("NL-PND-DRUM-WH", "Linen White", 6499m, 7499m, 15, Finish: "Linen", Color: "White"),
             new("NL-PND-DRUM-CH", "Charcoal", 6499m, 7499m, 7, Finish: "Linen", Color: "Charcoal")]),

        new("chandeliers", "Aurelia Crystal Chandelier 8-Light", "aurelia-crystal-chandelier-8", "UL-CHD-AUR8", "Unique Lights Atelier",
            "Hand-strung crystal drops on a sculpted brass frame.",
            "Aurelia pairs hand-strung K9 crystal drops with a solid brass frame for a chandelier that sparkles by day and glows by night. Eight candle lamps, adjustable chain drop and a matching ceiling rose.",
            true, false,
            Specs(("Wattage", "8 × max 25 W (E14)"), ("Lumens", "Up to 3600 lm"), ("Colour temperature", "Bulb dependent (2700K recommended)"),
                  ("Material", "K9 crystal, brass"), ("Finish", "Polished Brass or Chrome"), ("Dimensions", "Ø72 × H65 cm (+ 60 cm chain)"),
                  ("Bulb base", "8 × E14"), ("Dimmable", "Yes, with dimmable bulbs"), ("Warranty", "5 years")),
            [new("/images/chandelier.jpg", "Crystal drops on a brass chandelier"),
             new("/images/hero-living.jpg", "Dark blue living room with brass lighting and a daybed")],
            [new("UL-CHD-AUR8-BR", "Polished Brass", 38000m, 44000m, 6, Finish: "Brass", Color: "Gold"),
             new("UL-CHD-AUR8-CR", "Chrome", 36500m, null, 4, Finish: "Chrome", Color: "Silver")]),

        new("chandeliers", "Grand Aurelia 12-Light Chandelier", "grand-aurelia-12-light-chandelier", "UL-CHD-AUR12", "Unique Lights Atelier",
            "Our largest crystal chandelier for double-height spaces.",
            "The Grand Aurelia scales the Aurelia design up to twelve lights and two tiers of crystal for foyers, double-height living rooms and stairwells. Professional installation recommended.",
            false, false,
            Specs(("Wattage", "12 × max 25 W (E14)"), ("Lumens", "Up to 5400 lm"), ("Colour temperature", "Bulb dependent (2700K recommended)"),
                  ("Material", "K9 crystal, brass"), ("Finish", "Polished Brass"), ("Dimensions", "Ø95 × H90 cm (+ 100 cm chain)"),
                  ("Bulb base", "12 × E14"), ("Dimmable", "Yes, with dimmable bulbs"), ("Warranty", "5 years")),
            [new("/images/chandelier.jpg", "Brass chandelier with cascading crystal drops"),
             new("/images/room-loft.jpg", "Open loft with a staircase and high ceiling")],
            [new("UL-CHD-AUR12-BR", "Polished Brass", 48000m, null, 2, Finish: "Brass", Color: "Gold", LowStock: 3)]),

        new("wall-lights", "Ember Fabric Wall Sconce", "ember-fabric-wall-sconce", "NL-WL-EMBER", "Nordlys",
            "Warm fabric sconce with an integrated dimmable LED.",
            "Ember wraps an integrated LED in a pleated fabric shade, washing the wall with warm light. Hard-wired with a slim backplate; works beautifully in pairs beside a bed or along a hallway.",
            true, true,
            Specs(("Wattage", "8 W integrated LED"), ("Lumens", "650 lm"), ("Colour temperature", "2700K or 3000K"),
                  ("CRI", "90+"), ("Material", "Pleated fabric, steel"), ("Finish", "Antique Brass"), ("Dimensions", "W18 × H30 × D20 cm"),
                  ("Bulb base", "Integrated LED"), ("Dimmable", "Yes (trailing-edge dimmer)"), ("Warranty", "3 years")),
            [new("/images/sconce-warm.jpg", "Fabric wall sconce glowing on a dark wall"),
             new("/images/hero-living.jpg", "Dark blue living room with warm accent lighting")],
            [new("NL-WL-EMBER-27", "Warm White 2700K", 2999m, 3499m, 30, Finish: "Brass", Temperature: "2700K", Wattage: 8),
             new("NL-WL-EMBER-30", "Soft White 3000K", 2999m, 3499m, 5, Finish: "Brass", Temperature: "3000K", Wattage: 8)]),

        new("wall-lights", "Lumio Linen Picture Sconce", "lumio-linen-picture-sconce", "LM-WL-LINEN", "Lumio",
            "Linen-shaded sconce sized for artwork and shelving.",
            "A compact linen shade on a swing arm that lets you aim light at a painting, a reading nook or open shelving. Includes an inline switch and a plug-in cable option.",
            false, false,
            Specs(("Wattage", "Max 25 W (E14)"), ("Lumens", "Up to 470 lm with recommended bulb"), ("Colour temperature", "Bulb dependent"),
                  ("Material", "Linen, metal"), ("Finish", "Brass or Chrome"), ("Dimensions", "W15 × H25 × D28 cm"),
                  ("Bulb base", "E14"), ("Dimmable", "Yes, with dimmable bulb"), ("Warranty", "2 years")),
            [new("/images/sconce-beige.jpg", "Linen shade wall sconce under a painting"),
             new("/images/room-living.jpg", "Bright living room with layered lighting")],
            [new("LM-WL-LINEN-BR", "Brass", 3499m, null, 11, Finish: "Brass", Color: "Gold"),
             new("LM-WL-LINEN-CR", "Chrome", 3499m, null, 0, Finish: "Chrome", Color: "Silver")]),

        new("wall-lights", "Halo Plug-in Wall Light", "halo-plug-in-wall-light", "LM-WL-HALO", "Lumio",
            "Renter-friendly plug-in wall light, no electrician needed.",
            "Halo mounts with two screws and plugs into any socket, with a fabric cord and inline dimmer. A simple way to add reading light beside a bed or sofa.",
            false, true,
            Specs(("Wattage", "6 W integrated LED"), ("Lumens", "480 lm"), ("Colour temperature", "3000K"),
                  ("CRI", "90+"), ("Material", "Aluminium, fabric cord"), ("Dimensions", "Ø16 × D12 cm"),
                  ("Bulb base", "Integrated LED"), ("Dimmable", "Yes (inline dimmer)"), ("Warranty", "2 years")),
            [new("/images/sconce-warm.jpg", "Wall light glowing warmly on a dark wall"),
             new("/images/sconce-beige.jpg", "Wall sconce lighting a painting")],
            [new("LM-WL-HALO-MB", "Matte Black", 1999m, 2499m, 40, Finish: "Matte Black", Color: "Black", Temperature: "3000K", Wattage: 6),
             new("LM-WL-HALO-WH", "White", 1999m, 2499m, 22, Finish: "White", Color: "White", Temperature: "3000K", Wattage: 6)]),

        new("floor-lamps", "Nordlys Task Floor Lamp", "nordlys-task-floor-lamp", "NL-FL-TASK", "Nordlys",
            "Adjustable reading lamp with a focused beam.",
            "A slender floor lamp with an adjustable arm and pivoting head, so you can direct light exactly where you read or work. Weighted base and foot switch.",
            true, false,
            Specs(("Wattage", "9 W integrated LED"), ("Lumens", "800 lm"), ("Colour temperature", "3000K"),
                  ("CRI", "90+"), ("Material", "Steel, cast-iron base"), ("Finish", "Matte Black or White"), ("Dimensions", "H140 cm, base Ø26 cm"),
                  ("Bulb base", "Integrated LED"), ("Dimmable", "Yes (3-step touch dimmer)"), ("Warranty", "3 years")),
            [new("/images/floor-lamp.jpg", "Black task floor lamp against a sage wall"),
             new("/images/room-loft.jpg", "Open loft living space")],
            [new("NL-FL-TASK-MB", "Matte Black", 7999m, null, 14, Finish: "Matte Black", Color: "Black", Temperature: "3000K", Wattage: 9),
             new("NL-FL-TASK-WH", "White", 7999m, null, 5, Finish: "White", Color: "White", Temperature: "3000K", Wattage: 9)]),

        new("floor-lamps", "Soirée Brass Floor Lamp", "soiree-brass-floor-lamp", "UL-FL-SOIREE", "Unique Lights Atelier",
            "Elegant brass floor lamp with a pleated shade.",
            "Soirée stands on a weighted marble base with a slim brass stem and a pleated shade that diffuses light softly across a living room. Pairs perfectly with a reading chair or daybed.",
            true, true,
            Specs(("Wattage", "Max 60 W (E27)"), ("Lumens", "Up to 806 lm with recommended bulb"), ("Colour temperature", "Bulb dependent (2700K recommended)"),
                  ("Material", "Brass, marble, cotton shade"), ("Finish", "Brushed Brass"), ("Dimensions", "H160 cm, shade Ø45 cm"),
                  ("Bulb base", "E27"), ("Dimmable", "Yes, with dimmable bulb"), ("Warranty", "3 years")),
            [new("/images/hero-living.jpg", "Brass floor lamp beside a daybed in a dark blue living room"),
             new("/images/floor-lamp.jpg", "Floor lamp in a living space")],
            [new("UL-FL-SOIREE-BR", "Brushed Brass", 14999m, 17999m, 8, Finish: "Brass", Color: "Gold")]),

        new("table-lamps", "Kumo Mini Paper Table Lamp", "kumo-mini-paper-table-lamp", "UL-TL-KUMO", "Unique Lights Atelier",
            "The Kumo woven shade, scaled down for bedsides.",
            "All the softness of our Kumo pendant in a compact table lamp. The woven paper shade sits on a solid oak base with an inline switch.",
            false, true,
            Specs(("Wattage", "Max 25 W (E14)"), ("Lumens", "Up to 470 lm with recommended bulb"), ("Colour temperature", "Bulb dependent (2700K recommended)"),
                  ("Material", "Paper cord, oak"), ("Finish", "Natural"), ("Dimensions", "Ø25 × H35 cm"),
                  ("Bulb base", "E14"), ("Dimmable", "No"), ("Warranty", "2 years")),
            [new("/images/pendant-paper.jpg", "Glowing woven paper lamp shade"),
             new("/images/sconce-beige.jpg", "Soft linen-shaded lamp light in a living room")],
            [new("UL-TL-KUMO-NT", "Natural", 2499m, null, 16, Finish: "Natural Oak", Color: "White")]),

        new("ceiling-lights", "Lumio Slim LED Flush Mount 18W", "lumio-slim-led-flush-mount-18w", "LM-CL-SLIM18", "Lumio",
            "Ultra-slim LED ceiling light for low ceilings.",
            "Just 3 cm deep, the Slim flush mount gives bright, uniform light for bedrooms, corridors and kitchens. Choose your colour temperature at purchase.",
            false, false,
            Specs(("Wattage", "18 W integrated LED"), ("Lumens", "1800 lm"), ("Colour temperature", "2700K / 3000K / 4000K"),
                  ("CRI", "85+"), ("Material", "Aluminium, opal PMMA diffuser"), ("Finish", "White"), ("Dimensions", "Ø30 × H3 cm"),
                  ("Bulb base", "Integrated LED"), ("Dimmable", "No"), ("Warranty", "2 years")),
            [new("/images/room-living.jpg", "Bright living room with ceiling lighting"),
             new("/images/room-kitchen.jpg", "Bright white kitchen")],
            [new("LM-CL-SLIM18-27", "Warm White 2700K", 1499m, 1999m, 50, Finish: "White", Temperature: "2700K", Wattage: 18),
             new("LM-CL-SLIM18-30", "Soft White 3000K", 1499m, 1999m, 35, Finish: "White", Temperature: "3000K", Wattage: 18),
             new("LM-CL-SLIM18-40", "Cool White 4000K", 1499m, 1999m, 3, Finish: "White", Temperature: "4000K", Wattage: 18)]),

        new("ceiling-lights", "Orbit LED Semi-Flush 24W", "orbit-led-semi-flush-24w", "LM-CL-ORBIT24", "Lumio",
            "Opal globe semi-flush light with a brass gallery.",
            "Orbit's opal glass globe sits just below the ceiling on a brass gallery, spreading soft 360° light. A dressier alternative to a plain flush mount for living and dining rooms.",
            false, false,
            Specs(("Wattage", "24 W integrated LED"), ("Lumens", "2200 lm"), ("Colour temperature", "3000K / 4000K"),
                  ("CRI", "90+"), ("Material", "Opal glass, brass"), ("Finish", "Brass"), ("Dimensions", "Ø35 × H28 cm"),
                  ("Bulb base", "Integrated LED"), ("Dimmable", "Yes (trailing-edge dimmer)"), ("Warranty", "3 years")),
            [new("/images/room-loft.jpg", "Open loft with staircase and ceiling lighting"),
             new("/images/room-living.jpg", "Bright living room")],
            [new("LM-CL-ORBIT24-30", "Soft White 3000K", 2799m, null, 20, Finish: "Brass", Temperature: "3000K", Wattage: 24),
             new("LM-CL-ORBIT24-40", "Cool White 4000K", 2799m, null, 9, Finish: "Brass", Temperature: "4000K", Wattage: 24)])
    ];

    private async Task SeedCatalogueAsync(CancellationToken ct)
    {
        // Categories are upserted by slug (cheap, idempotent).
        var categories = await db.Categories.IgnoreQueryFilters().ToListAsync(ct);
        for (var i = 0; i < CategorySeed.Length; i++)
        {
            var seed = CategorySeed[i];
            if (categories.Any(c => c.Slug == seed.Slug)) continue;
            var category = new Category
            {
                Name = seed.Name,
                Slug = seed.Slug,
                Description = seed.Description,
                ImageUrl = seed.Image,
                SortOrder = (i + 1) * 10,
                IsActive = true
            };
            db.Categories.Add(category);
            categories.Add(category);
        }
        await db.SaveChangesAsync(ct);

        if (await db.Products.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Products already exist; skipping catalogue seeding");
            return;
        }

        var bySlug = categories.Where(c => !c.IsDeleted).ToDictionary(c => c.Slug);
        // Staggered creation times so "newest" sorting is meaningful.
        var createdAt = DateTime.UtcNow.AddDays(-ProductSeed.Length);

        foreach (var seed in ProductSeed)
        {
            createdAt = createdAt.AddDays(1);
            var defaultVariant = seed.Variants[0];
            var product = new Product
            {
                Name = seed.Name,
                Slug = seed.Slug,
                Sku = seed.Sku,
                Brand = seed.Brand,
                ShortDescription = seed.Short,
                Description = seed.Description,
                BasePrice = defaultVariant.Price,
                CompareAtPrice = defaultVariant.Compare,
                CategoryId = bySlug[seed.CategorySlug].Id,
                IsActive = true,
                IsFeatured = seed.Featured,
                IsNew = seed.IsNew,
                Specifications = seed.Specs,
                CreatedAt = createdAt
            };

            for (var i = 0; i < seed.Images.Length; i++)
            {
                product.Images.Add(new ProductImage
                {
                    ProductId = product.Id,
                    Url = seed.Images[i].Url,
                    StoragePath = null,
                    AltText = seed.Images[i].Alt,
                    SortOrder = i,
                    IsPrimary = i == 0,
                    CreatedAt = createdAt
                });
            }

            for (var i = 0; i < seed.Variants.Length; i++)
            {
                var v = seed.Variants[i];
                var variant = new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = v.Sku,
                    Name = v.Name,
                    Color = v.Color,
                    Finish = v.Finish,
                    ColorTemperature = v.Temperature,
                    Wattage = v.Wattage,
                    Price = v.Price,
                    CompareAtPrice = v.Compare,
                    IsDefault = i == 0,
                    IsActive = true,
                    CreatedAt = createdAt
                };
                variant.Inventory = new Inventory
                {
                    VariantId = variant.Id,
                    QuantityOnHand = v.Stock,
                    ReservedQuantity = 0,
                    LowStockThreshold = v.LowStock,
                    CreatedAt = createdAt
                };
                product.Variants.Add(variant);
            }

            db.Products.Add(product);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} products", ProductSeed.Length);
    }
}
