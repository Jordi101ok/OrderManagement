using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Middleware;
using OrderManagement.Application.Abstractions;
using OrderManagement.Application.Dtos;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderService service) : ControllerBase
{
    [HttpPost]
    [ServiceFilter(typeof(IdempotencyFilter))]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var order = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await service.GetAsync(id, ct));

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] OrderStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new ListOrdersQuery(status, customerId, from, to, page, pageSize);
        return Ok(await service.ListAsync(query, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
        => Ok(await service.UpdateStatusAsync(id, request.Status, ct));

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(await service.CancelAsync(id, ct));
}