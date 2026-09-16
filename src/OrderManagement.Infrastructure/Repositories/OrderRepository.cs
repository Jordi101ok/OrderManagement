using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Abstractions;
using OrderManagement.Application.Dtos;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Infrastructure.Repositories;

public class OrderRepository(AppDbContext db) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct = default)
        => await db.Orders.AddAsync(order, ct);

    public Task<Order?> GetWithItemsAsync(Guid id, CancellationToken ct = default)
        => db.Orders
             .Include(o => o.Items)
             .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<PagedResult<Order>> ListAsync(
        ListOrdersQuery q, CancellationToken ct = default)
    {
        var query = db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .AsQueryable();

        if (q.Status is not null)
            query = query.Where(o => o.Status == q.Status);

        if (q.CustomerId is not null)
            query = query.Where(o => o.CustomerId == q.CustomerId);

        if (q.From is not null)
            query = query.Where(o => o.CreatedAt >= q.From);

        if (q.To is not null)
            query = query.Where(o => o.CreatedAt <= q.To);

        var total = await query.CountAsync(ct);

        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)          // tie-breaker, lihat catatan di bawah
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<Order>(items, page, size, total);
    }
}