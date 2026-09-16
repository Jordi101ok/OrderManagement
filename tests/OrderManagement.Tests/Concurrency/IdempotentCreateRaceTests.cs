using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OrderManagement.Application.Dtos;
using OrderManagement.Infrastructure.Persistence;
using OrderManagement.Tests.Integration;

namespace OrderManagement.Tests.Concurrency;

[Collection("Database")]
public class IdempotentCreateRaceTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Skenario C: dua POST /orders dengan idempotency key yang sama
    /// tiba bersamaan, sebelum salah satu sempat commit.
    /// Sistem harus tetap hanya membuat satu order.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentRequests_SameKey_CreatesOnlyOneOrder()
    {
        await fixture.ResetAsync(productXStock: 50);

        var key = $"race-{Guid.NewGuid()}";
        var request = new CreateOrderRequest(
            DatabaseFixture.CustomerId,
            [new OrderItemRequest(DatabaseFixture.ProductX, 5)],
            "Jl. Test No. 1");

        var barrier = new TaskCompletionSource();

        async Task<HttpResponseMessage> Submit()
        {
            var client = fixture.Factory.CreateClient();
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(request)
            };
            msg.Headers.Add("Idempotency-Key", key);

            await barrier.Task;
            return await client.SendAsync(msg);
        }

        var t1 = Submit();
        var t2 = Submit();
        barrier.SetResult();

        var responses = await Task.WhenAll(t1, t2);

        // Yang menentukan: hanya SATU order yang terbuat,
        // apapun status code yang diterima kedua request
        var orderCount = await CountOrdersAsync();
        orderCount.Should().Be(1);

        // Stock hanya berkurang sekali
        var stock = await fixture.GetStockAsync(DatabaseFixture.ProductX);
        stock.Should().Be(45);

        // Yang kalah mendapat 201 (replay) atau 409 (in-progress),
        // tergantung apakah pemenang sudah selesai commit
        var winner = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var inProgress = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        (winner + inProgress).Should().Be(2);
        winner.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// Request berurutan dengan key sama: yang kedua harus mendapat
    /// response identik dari cache, bukan membuat order baru.
    /// Ini kasus double-click yang disebut di soal.
    /// </summary>
    [Fact]
    public async Task SequentialRequests_SameKey_ReplaysCachedResponse()
    {
        await fixture.ResetAsync(productXStock: 50);

        var key = $"replay-{Guid.NewGuid()}";
        var request = new CreateOrderRequest(
            DatabaseFixture.CustomerId,
            [new OrderItemRequest(DatabaseFixture.ProductX, 5)],
            "Jl. Test No. 1");

        var client = fixture.Factory.CreateClient();

        async Task<HttpResponseMessage> Submit()
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
            {
                Content = JsonContent.Create(request)
            };
            msg.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(msg);
        }

        var first = await Submit();
        var second = await Submit();

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        second.Headers.Contains("Idempotency-Replayed").Should().BeTrue();

        // Order ID harus identik
        var o1 = await first.Content.ReadFromJsonAsync<OrderResponse>();
        var o2 = await second.Content.ReadFromJsonAsync<OrderResponse>();
        o2!.Id.Should().Be(o1!.Id);

        // Stock hanya berkurang sekali
        var stock = await fixture.GetStockAsync(DatabaseFixture.ProductX);
        stock.Should().Be(45);

        (await CountOrdersAsync()).Should().Be(1);
    }

    private async Task<int> CountOrdersAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.CountAsync();
    }
}