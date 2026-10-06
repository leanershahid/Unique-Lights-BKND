using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniqueLights.Api.Entities;

namespace UniqueLights.Api.Configurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.ToTable("carts");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasOne(x => x.User).WithOne().HasForeignKey<Cart>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("cart_items", t => t.HasCheckConstraint("ck_cart_items_quantity_range", "quantity BETWEEN 1 AND 99"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CartId, x.VariantId }).IsUnique();
        b.HasIndex(x => x.VariantId);
        b.HasOne(x => x.Cart).WithMany(c => c.Items).HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WishlistConfiguration : IEntityTypeConfiguration<Wishlist>
{
    public void Configure(EntityTypeBuilder<Wishlist> b)
    {
        b.ToTable("wishlists");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasOne(x => x.User).WithOne().HasForeignKey<Wishlist>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> b)
    {
        b.ToTable("wishlist_items");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.WishlistId, x.ProductId }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.Wishlist).WithMany(w => w.Items).HasForeignKey(x => x.WishlistId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.ToTable("coupons", t =>
        {
            t.HasCheckConstraint("ck_coupons_value_positive", "value > 0");
            t.HasCheckConstraint("ck_coupons_used_count_non_negative", "used_count >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Value).HasPrecision(12, 2);
        b.Property(x => x.MinOrderAmount).HasPrecision(12, 2);
        b.Property(x => x.MaxDiscountAmount).HasPrecision(12, 2);
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => x.Code).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public const string OrderNumberSequence = "order_number_seq";

    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", t =>
        {
            t.HasCheckConstraint("ck_orders_amounts_non_negative", "subtotal >= 0 AND discount >= 0 AND shipping >= 0 AND tax >= 0 AND total >= 0");
        });
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderNumber)
            .HasMaxLength(32)
            .HasDefaultValueSql($"'UL-' || nextval('{OrderNumberSequence}')")
            .ValueGeneratedOnAdd()
            .IsRequired();
        b.Property(x => x.OrderNumber).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.DeliveryMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Subtotal).HasPrecision(12, 2);
        b.Property(x => x.Discount).HasPrecision(12, 2);
        b.Property(x => x.Shipping).HasPrecision(12, 2);
        b.Property(x => x.Tax).HasPrecision(12, 2);
        b.Property(x => x.Total).HasPrecision(12, 2);
        b.Property(x => x.CouponCode).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.TrackingNumber).HasMaxLength(100);

        b.OwnsOne(x => x.ShippingAddress, a =>
        {
            a.Property(p => p.FullName).HasColumnName("ship_full_name").HasMaxLength(150).IsRequired();
            a.Property(p => p.Phone).HasColumnName("ship_phone").HasMaxLength(32).IsRequired();
            a.Property(p => p.Line1).HasColumnName("ship_line1").HasMaxLength(200).IsRequired();
            a.Property(p => p.Line2).HasColumnName("ship_line2").HasMaxLength(200);
            a.Property(p => p.City).HasColumnName("ship_city").HasMaxLength(100).IsRequired();
            a.Property(p => p.State).HasColumnName("ship_state").HasMaxLength(100).IsRequired();
            a.Property(p => p.PostalCode).HasColumnName("ship_postal_code").HasMaxLength(20).IsRequired();
            a.Property(p => p.Country).HasColumnName("ship_country").HasMaxLength(100).IsRequired();
        });
        b.Navigation(x => x.ShippingAddress).IsRequired();

        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.HasIndex(x => new { x.UserId, x.PlacedAt }).IsDescending(false, true);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.PlacedAt);

        b.HasOne(x => x.User).WithMany(u => u.Orders).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Coupon).WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items", t => t.HasCheckConstraint("ck_order_items_quantity_positive", "quantity > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProductSlug).HasMaxLength(220).IsRequired();
        b.Property(x => x.VariantName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
        b.Property(x => x.ImageUrl).HasMaxLength(1024);
        b.Property(x => x.UnitPrice).HasPrecision(12, 2);
        b.Property(x => x.LineTotal).HasPrecision(12, 2);
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.ProductId);
        b.HasIndex(x => x.VariantId);
        b.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        // Product/variant rows are never hard-deleted (soft delete), so the snapshot keeps referential integrity.
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.TransactionReference).HasMaxLength(100);
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasOne(x => x.Order).WithOne(o => o.Payment).HasForeignKey<Payment>(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
