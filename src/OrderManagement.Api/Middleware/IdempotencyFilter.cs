using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrderManagement.Application.Abstractions;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Api.Middleware;

public class IdempotencyFilter(
    IIdempotencyStore store,
    ILogger<IdempotencyFilter> logger) : IAsyncActionFilter
{
    private const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var key = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();

        // Header opsional — tanpa key, jalan seperti biasa
        if (string.IsNullOrWhiteSpace(key))
        {
            await next();
            return;
        }

        var body = context.ActionArguments.Values.FirstOrDefault();
        var hash = ComputeHash(body);

        var existing = await store.TryBeginAsync(key, hash);

        if (existing is not null)
        {
            // Key sama tapi payload beda — kesalahan klien
            if (existing.RequestHash != hash)
            {
                logger.LogWarning("Idempotency key {Key} reused with different payload", key);

                context.Result = new ObjectResult(new
                {
                    type = "https://httpstatuses.io/422",
                    title = "IDEMPOTENCY_KEY_REUSED",
                    status = 422,
                    detail = "This idempotency key was already used with a different request body."
                })
                { StatusCode = 422 };
                return;
            }

            if (existing.State == IdempotencyState.Completed)
            {
                logger.LogInformation("Replaying cached response for key {Key}", key);

                context.HttpContext.Response.Headers["Idempotency-Replayed"] = "true";
                context.Result = new ContentResult
                {
                    Content = existing.ResponseBody,
                    ContentType = "application/json",
                    StatusCode = existing.StatusCode ?? 200
                };
                return;
            }

            // Masih InProgress — request pertama belum selesai (Skenario C)
            logger.LogInformation("Concurrent request for in-progress key {Key}", key);

            context.HttpContext.Response.Headers["Retry-After"] = "1";
            context.Result = new ObjectResult(new
            {
                type = "https://httpstatuses.io/409",
                title = "IDEMPOTENCY_IN_PROGRESS",
                status = 409,
                detail = "A request with this idempotency key is currently being processed."
            })
            { StatusCode = 409 };
            return;
        }

        // Kita yang pertama — lanjutkan ke controller
        var executed = await next();

        if (executed.Result is ObjectResult { Value: not null } result
            && result.StatusCode is >= 200 and < 300)
        {
            var json = JsonSerializer.Serialize(result.Value,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var orderId = TryExtractId(result.Value);

            await store.CompleteAsync(key, orderId, json, result.StatusCode ?? 200);
        }
    }

    private static string ComputeHash(object? body)
    {
        var json = JsonSerializer.Serialize(body);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static Guid TryExtractId(object value)
        => value.GetType().GetProperty("Id")?.GetValue(value) is Guid id ? id : Guid.Empty;
}