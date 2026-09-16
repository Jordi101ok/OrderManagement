using Microsoft.EntityFrameworkCore.Storage;
using OrderManagement.Application.Abstractions;

namespace OrderManagement.Infrastructure.Persistence;

public class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        return new EfTransaction(tx);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

internal class EfTransaction(IDbContextTransaction tx) : IAppTransaction
{
    public Task CommitAsync(CancellationToken ct = default) => tx.CommitAsync(ct);
    public Task RollbackAsync(CancellationToken ct = default) => tx.RollbackAsync(ct);
    public ValueTask DisposeAsync() => tx.DisposeAsync();
}