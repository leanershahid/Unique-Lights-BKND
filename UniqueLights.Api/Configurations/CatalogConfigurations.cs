using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(140).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ImageUrl).HasMaxLength(1024);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.Slug).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => x.ParentId);
        b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(220).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.Property(x => x.ShortDescription).HasMaxLength(500);
        b.Property(x => x.Description).HasMaxLength(10000);
        b.Property(x => x.Brand).HasMaxLength(100);
        b.Property(x => x.BasePrice).HasPrecision(12, 2);
        b.Property(x => x.CompareAtPrice).HasPrecision(12, 2);
        b.Property(x => x.AverageRating).HasPrecision(3, 2).HasDefaultValue(0m);
        b.Property(x => x.ReviewCount).HasDefaultValue(0);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        var converter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => string.IsNullOrEmpty(v)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new Dictionary<string, string>());

        var comparer = new ValueComparer<Dictionary<string, string>>(
            (a, c) => DictionaryEquals(a, c),
            v => DictionaryHash(v),
            v => new Dictionary<string, string>(v));

        b.Property(x => x.Specifications)
            .HasColumnType("jsonb")
            .HasConversion(converter, comparer)
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        b.HasIndex(x => x.Slug).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => x.Sku).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => x.Name);
        b.HasIndex(x => new { x.CategoryId, x.IsActive });
        b.HasIndex(x => x.Brand);

        b.HasOne(x => x.Category).WithMany(c => c.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }

    private static bool DictionaryEquals(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static int DictionaryHash(Dictionary<string, string> v)
    {
        var hash = 0;
        foreach (var (key, value) in v)
        {
            hash ^= HashCode.Combine(key, value);
        }
        return hash;
    }
}

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("product_images");
        b.HasKey(x => x.Id);
        b.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        b.Property(x => x.StoragePath).HasMaxLength(512);
        b.Property(x => x.AltText).HasMaxLength(250);
        b.HasIndex(x => new { x.ProductId, x.SortOrder });
        b.HasOne(x => x.Product).WithMany(p => p.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.ToTable("product_variants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Color).HasMaxLength(50);
        b.Property(x => x.Finish).HasMaxLength(50);
        b.Property(x => x.ColorTemperature).HasMaxLength(20);
        b.Property(x => x.Price).HasPrecision(12, 2);
        b.Property(x => x.CompareAtPrice).HasPrecision(12, 2);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.Sku).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Product).WithMany(p => p.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_product_variants_price_non_negative", "price >= 0");
            t.HasCheckConstraint("ck_product_variants_wattage_positive", "wattage IS NULL OR wattage > 0");
        });
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> b)
    {
        b.ToTable("inventory", t =>
        {
            t.HasCheckConstraint("ck_inventory_quantity_on_hand_non_negative", "quantity_on_hand >= 0");
            t.HasCheckConstraint("ck_inventory_reserved_quantity_non_negative", "reserved_quantity >= 0");
            t.HasCheckConstraint("ck_inventory_low_stock_threshold_non_negative", "low_stock_threshold >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.LowStockThreshold).HasDefaultValue(5);
        b.Property(x => x.QuantityOnHand).HasDefaultValue(0);
        b.Property(x => x.ReservedQuantity).HasDefaultValue(0);

        // PostgreSQL xmin system column as optimistic concurrency token (not created by migrations).
        b.Property(x => x.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        b.Ignore(x => x.Available);
        b.HasIndex(x => x.VariantId).IsUnique();
        b.HasOne(x => x.Variant).WithOne(v => v.Inventory).HasForeignKey<Inventory>(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_rating_range", "rating BETWEEN 1 AND 5"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Rating).HasColumnType("smallint");
        b.Property(x => x.Title).HasMaxLength(120);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.Property(x => x.IsApproved).HasDefaultValue(true);
        b.HasIndex(x => new { x.ProductId, x.UserId }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.Product).WithMany(p => p.Reviews).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
