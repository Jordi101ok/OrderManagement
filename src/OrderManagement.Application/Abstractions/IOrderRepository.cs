using OrderManagement.Application.Dtos;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Abstractions;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct = default);
    Task<Order?> GetWithItemsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Order>> ListAsync(ListOrdersQuery query, CancellationToken ct = default);
}