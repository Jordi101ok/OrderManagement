using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Exceptions;

namespace OrderManagement.Api.Middleware;

public class ExceptionHandlingMiddleware(
    RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var correlationId = context.Items["X-Correlation-Id"]?.ToString();

        var (status, code, message) = ex switch
        {
            NotFoundException e => (404, e.ErrorCode, e.Message),
            ValidationException e => (422, e.ErrorCode, e.Message),
            InsufficientStockException e => (409, e.ErrorCode, e.Message),
            InvalidStatusTransitionException e => (409, e.ErrorCode, e.Message),
            IdempotencyConflictException e => (409, e.ErrorCode, e.Message),
            ConflictException e => (409, e.ErrorCode, e.Message),

            DbUpdateConcurrencyException => (409, "CONCURRENT_MODIFICATION",
                "The order was modified by another request. Please reload and try again."),

            _ => (500, "INTERNAL_ERROR", "An unexpected error occurred.")
        };

        if (status >= 500)
            logger.LogError(ex, "Unhandled exception");
        else
            logger.LogWarning("Request failed: {Code} - {Message}", code, ex.Message);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var payload = new
        {
            type = $"https://httpstatuses.io/{status}",
            title = code,
            status,
            detail = message,
            correlationId
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}