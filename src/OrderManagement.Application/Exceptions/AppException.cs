namespace OrderManagement.Application.Exceptions;

public abstract class AppException(string message, string errorCode)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public class NotFoundException(string message)
    : AppException(message, "NOT_FOUND");

public class ValidationException(string message)
    : AppException(message, "VALIDATION_ERROR");

public class ConflictException(string message, string code = "CONFLICT")
    : AppException(message, code);

public class InsufficientStockException(Guid productId, int requested, int available)
    : AppException(
        $"Insufficient stock for product {productId}. Requested {requested}, available {available}.",
        "INSUFFICIENT_STOCK")
{
    public Guid ProductId { get; } = productId;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}

public class InvalidStatusTransitionException(string from, string to)
    : AppException($"Cannot transition order from {from} to {to}.", "INVALID_TRANSITION");

public class IdempotencyConflictException(string message)
    : AppException(message, "IDEMPOTENCY_CONFLICT");