using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrderManagement.Application.Dtos;
using OrderManagement.Tests.Integration;

namespace OrderManagement.Tests.Concurrency;

[Collection("Database")]
public class ConcurrentStockDeductionTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Skenario A: dua user submit order yang sama-sama butuh 10 unit,
    /// padahal stock tinggal 15.
    /// Hanya satu yang boleh berhasil; total terdeduksi tidak boleh > 15.
    /// </summary>
    [Fact]
    public async Task TwoOrdersOf10_WithStock15_OnlyOneSucceeds()
    {
        await fixture.ResetAsync(productXStock: 15);

        var request = new CreateOrderRequest(
            DatabaseFixture.CustomerId,
            [new OrderItemRequest(DatabaseFixture.ProductX, 10)],
            "Jl. Test No. 1");

        // Barrier memastikan kedua request berangkat benar-benar bersamaan,
        // bukan berurutan.
        var barrier = new TaskCompletionSource();

        async Task<HttpResponseMessage> Submit()
        {
            var client = fixture.Factory.CreateClient();
            await barrier.Task;
            return await client.PostAsJsonAsync("/api/orders", request);
        }

        var t1 = Submit();
        var t2 = Submit();

        barrier.SetResult();

        var responses = await Task.WhenAll(t1, t2);

        // Tepat satu berhasil
        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1);

        // Yang kalah mendapat 409 dengan alasan yang jelas
        var failed = responses.Single(r => !r.IsSuccessStatusCode);
        failed.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await failed.Content.ReadAsStringAsync();
        body.Should().Contain("INSUFFICIENT_STOCK");

        // Yang paling penting: stock tidak pernah minus,
        // dan total terdeduksi tepat 10 (bukan 20)
        var stock = await fixture.GetStockAsync(DatabaseFixture.ProductX);
        stock.Should().Be(5);
    }

    /// <summary>
    /// Versi lebih keras: 10 request bersamaan pada stock 15,
    /// masing-masing butuh 3 unit. Maksimal 5 yang boleh berhasil.
    /// </summary>
    [Fact]
    public async Task TenConcurrentOrders_StockNeverGoesNegative()
    {
        await fixture.ResetAsync(productXStock: 15);

        var request = new CreateOrderRequest(
            DatabaseFixture.CustomerId,
            [new OrderItemRequest(DatabaseFixture.ProductX, 3)],
            "Jl. Test No. 1");

        var barrier = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var client = fixture.Factory.CreateClient();
            await barrier.Task;
            return await client.PostAsJsonAsync("/api/orders", request);
        }).ToList();

        barrier.SetResult();
        var responses = await Task.WhenAll(tasks);

        var succeeded = responses.Count(r => r.IsSuccessStatusCode);

        succeeded.Should().Be(5);            // 15 / 3

        var stock = await fixture.GetStockAsync(DatabaseFixture.ProductX);
        stock.Should().Be(0);
        stock.Should().BeGreaterThanOrEqualTo(0);
    }
}