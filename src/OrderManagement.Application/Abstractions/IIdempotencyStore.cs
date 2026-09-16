using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions;

public interface IIdempotencyStore
{
    /// <summary>
    /// Mencoba memesan key. Mengembalikan null kalau berhasil (kita yang pertama).
    /// Kalau key sudah ada, mengembalikan record milik pemenang.
    /// </summary>
    Task<IdempotencyRecord?> TryBeginAsync(
        string key, string requestHash, CancellationToken ct = default);

    Task CompleteAsync(
        string key, Guid orderId, string responseBody, int statusCode,
        CancellationToken ct = default);
}