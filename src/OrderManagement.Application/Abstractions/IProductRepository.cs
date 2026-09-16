using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions;

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Mengurangi stock secara atomic. Mengembalikan false kalau stock tidak cukup.
    /// Pengecekan dan pengurangan terjadi dalam satu statement SQL,
    /// jadi tidak ada celah antara cek dan update.
    /// </summary>
    Task<bool> TryDeductStockAsync(Guid productId, int quantity, CancellationToken ct = default);

    Task RestoreStockAsync(Guid productId, int quantity, CancellationToken ct = default);
}