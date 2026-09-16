using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions;
using OrderManagement.Application.Dtos;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public class OrderService(
    IOrderRepository orders,
    IProductRepository products,
    IUnitOfWork uow,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request, CancellationToken ct = default)
    {
        // Race condition #1: item duplikat dalam satu request.
        // Kalau tidak digabung, dua baris untuk produk yang sama
        // akan mengurangi stock dua kali dengan pengecekan terpisah.
        var merged = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemRequest(g.Key, g.Sum(x => x.Quantity)))
            .OrderBy(i => i.ProductId)   // Race condition #2: urutan konsisten cegah deadlock
            .ToList();

        await using var tx = await uow.BeginTransactionAsync(ct);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            ShippingAddress = request.ShippingAddress,
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        decimal total = 0;

        foreach (var item in merged)
        {
            var product = await products.GetAsync(item.ProductId, ct)
                ?? throw new NotFoundException($"Product {item.ProductId} not found.");

            // Atomic: cek dan kurangi dalam satu statement
            var ok = await products.TryDeductStockAsync(item.ProductId, item.Quantity, ct);

            if (!ok)
            {
                // Baca ulang untuk pesan error yang akurat
                var current = await products.GetAsync(item.ProductId, ct);
                logger.LogWarning(
                    "Stock deduction failed for {ProductId}: requested {Qty}, available {Available}",
                    item.ProductId, item.Quantity, current?.StockQuantity ?? 0);

                throw new InsufficientStockException(
                    item.ProductId, item.Quantity, current?.StockQuantity ?? 0);
            }

            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = product.Price
            });

            total += product.Price * item.Quantity;
        }

        order.TotalAmount = total;

        await orders.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId}, total {Total}",
            order.Id, order.CustomerId, total);

        return Map(order);
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await orders.GetWithItemsAsync(id, ct)
            ?? throw new NotFoundException($"Order {id} not found.");

        return Map(order);
    }

    public async Task<PagedResult<OrderResponse>> ListAsync(
        ListOrdersQuery query, CancellationToken ct = default)
    {
        var result = await orders.ListAsync(query, ct);

        return new PagedResult<OrderResponse>(
            result.Items.Select(Map).ToList(),
            result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<OrderResponse> UpdateStatusAsync(
        Guid id, OrderStatus newStatus, CancellationToken ct = default)
    {
        var order = await orders.GetWithItemsAsync(id, ct)
            ?? throw new NotFoundException($"Order {id} not found.");

        if (!OrderStatusTransition.CanTransition(order.Status, newStatus))
            throw new InvalidStatusTransitionException(
                order.Status.ToString(), newStatus.ToString());

        // Cancel lewat endpoint ini juga harus mengembalikan stock
        if (newStatus == OrderStatus.Cancelled)
            return await CancelAsync(id, ct);

        order.Status = newStatus;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        order.Version++;

        await uow.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderId} status changed to {Status}", id, newStatus);

        return Map(order);
    }

    public async Task<OrderResponse> CancelAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await uow.BeginTransactionAsync(ct);

        var order = await orders.GetWithItemsAsync(id, ct)
            ?? throw new NotFoundException($"Order {id} not found.");

        if (!OrderStatusTransition.CanCancel(order.Status))
            throw new InvalidStatusTransitionException(
                order.Status.ToString(), OrderStatus.Cancelled.ToString());

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        order.Version++;

        // Stock dikembalikan HANYA kalau SaveChanges berhasil.
        // Kalau ada yang cancel duluan, Version sudah berubah,
        // SaveChanges melempar DbUpdateConcurrencyException,
        // dan restore di bawah tidak pernah jalan.
        await uow.SaveChangesAsync(ct);

        foreach (var item in order.Items)
            await products.RestoreStockAsync(item.ProductId, item.Quantity, ct);

        await tx.CommitAsync(ct);

        logger.LogInformation("Order {OrderId} cancelled, stock restored", id);

        return Map(order);
    }

    private static OrderResponse Map(Order o) => new(
        o.Id, o.CustomerId, o.Status, o.ShippingAddress, o.TotalAmount,
        o.CreatedAt, o.UpdatedAt,
        o.Items.Select(i => new OrderItemResponse(i.ProductId, i.Quantity, i.UnitPrice)).ToList());
}