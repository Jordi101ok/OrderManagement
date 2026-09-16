using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Dtos;

public record UpdateStatusRequest(OrderStatus Status);