using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderManagement.Application.Abstractions;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Persistence;

namespace OrderManagement.Infrastructure.Repositories;

public class IdempotencyStore(AppDbContext db) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> TryBeginAsync(
        string key, string requestHash, CancellationToken ct = default)
    {
        var record = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            Key = key,
            RequestHash = requestHash,
            State = IdempotencyState.InProgress,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.IdempotencyRecords.Add(record);

        try
        {
            await db.SaveChangesAsync(ct);
            return null;   // kita yang pertama
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Kalah race — unique constraint menolak INSERT kita.
            db.Entry(record).State = EntityState.Detached;

            return await db.IdempotencyRecords
                .AsNoTracking()
                .FirstAsync(r => r.Key == key, ct);
        }
    }

    public async Task CompleteAsync(
        string key, Guid orderId, string responseBody, int statusCode,
        CancellationToken ct = default)
    {
        var record = await db.IdempotencyRecords.FirstAsync(r => r.Key == key, ct);

        record.State = IdempotencyState.Completed;
        record.OrderId = orderId;
        record.ResponseBody = responseBody;
        record.StatusCode = statusCode;

        await db.SaveChangesAsync(ct);
    }
}