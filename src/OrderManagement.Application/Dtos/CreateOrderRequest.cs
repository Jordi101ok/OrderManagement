namespace OrderManagement.Application.Dtos;

public record CreateOrderRequest(
    Guid CustomerId,
    List<OrderItemRequest> Items,
    string ShippingAddress);

public record OrderItemRequest(Guid ProductId, int Quantity);