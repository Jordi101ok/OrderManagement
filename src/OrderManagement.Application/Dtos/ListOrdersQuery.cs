using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Dtos;

public record ListOrdersQuery(
    OrderStatus? Status = null,
    Guid? CustomerId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 20);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}