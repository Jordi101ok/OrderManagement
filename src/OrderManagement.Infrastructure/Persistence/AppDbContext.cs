using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("oms");

        b.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200).IsRequired();
            e.Property(p => p.Price).HasPrecision(18, 2);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_products_stock_non_negative", "\"StockQuantity\" >= 0"));
        });

        b.Entity<Order>(e =>
        {
            e.Property(o => o.ShippingAddress).HasMaxLength(500).IsRequired();
            e.Property(o => o.TotalAmount).HasPrecision(18, 2);
            e.Property(o => o.Status).HasConversion<int>();

            e.Property(o => o.Version).IsConcurrencyToken();

            e.HasMany(o => o.Items)
             .WithOne()
             .HasForeignKey(i => i.OrderId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(o => o.CustomerId);
            e.HasIndex(o => o.Status);
            e.HasIndex(o => o.CreatedAt);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            e.HasIndex(i => i.ProductId);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.Property(r => r.Key).HasMaxLength(128).IsRequired();
            e.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
            e.Property(r => r.State).HasConversion<int>();

            e.HasIndex(r => r.Key).IsUnique();
        });
    }
}
