using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Abstractions;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Infrastructure.Repositories;

public class ProductRepository(AppDbContext db) : IProductRepository
{
    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<bool> TryDeductStockAsync(
        Guid productId, int quantity, CancellationToken ct = default)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE oms."Products"
            SET "StockQuantity" = "StockQuantity" - {quantity}
            WHERE "Id" = {productId} AND "StockQuantity" >= {quantity}
            """, ct);

        return affected == 1;
    }

    public async Task RestoreStockAsync(
        Guid productId, int quantity, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE oms."Products"
            SET "StockQuantity" = "StockQuantity" + {quantity}
            WHERE "Id" = {productId}
            """, ct);
    }
}