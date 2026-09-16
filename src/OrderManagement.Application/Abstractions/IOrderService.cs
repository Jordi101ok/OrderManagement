using OrderManagement.Application.Dtos;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Abstractions;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);
    Task<OrderResponse> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<OrderResponse>> ListAsync(ListOrdersQuery query, CancellationToken ct = default);
    Task<OrderResponse> UpdateStatusAsync(Guid id, OrderStatus newStatus, CancellationToken ct = default);
    Task<OrderResponse> CancelAsync(Guid id, CancellationToken ct = default);
}