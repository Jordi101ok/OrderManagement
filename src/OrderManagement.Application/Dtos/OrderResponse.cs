using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Dtos;

public record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    string ShippingAddress,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<OrderItemResponse> Items);

public record OrderItemResponse(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice);