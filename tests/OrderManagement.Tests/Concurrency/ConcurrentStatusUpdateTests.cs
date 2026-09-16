using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using OrderManagement.Application.Dtos;
using OrderManagement.Domain.Enums;
using OrderManagement.Tests.Integration;

namespace OrderManagement.Tests.Concurrency;

[Collection("Database")]
public class ConcurrentStatusUpdateTests(DatabaseFixture fixture)
{
    /// <summary>
    /// Skenario B: dua admin meng-update status order yang sama secara bersamaan.
    /// Satu update jadi Confirmed, satu lagi cancel jadi Cancelled.
    /// Hanya satu yang boleh menang; yang lain dapat error yang jelas.
    /// </summary>
    [Fact]
    public async Task TwoAdminsUpdatingSameOrder_OnlyOneWins()
    {
        await fixture.ResetAsync(productXStock: 50);

        // Siapkan satu order dalam status Pending
        var client = fixture.Factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest(
            DatabaseFixture.CustomerId,
            [new OrderItemRequest(DatabaseFixture.ProductX, 5)],
            "Jl. Test No. 1"));

        created.EnsureSuccessStatusCode();
        var order = await created.Content.ReadFromJsonAsync<OrderResponse>();
        var orderId = order!.Id;

        var barrier = new TaskCompletionSource();

        // Admin 1: Pending → Confirmed
        async Task<HttpResponseMessage> Confirm()
        {
            var c = fixture.Factory.CreateClient();
            await barrier.Task;
            return await c.PatchAsJsonAsync(
                $"/api/orders/{orderId}/status",
                new UpdateStatusRequest(OrderStatus.Confirmed));
        }

        // Admin 2: Pending → Cancelled
        async Task<HttpResponseMessage> Cancel()
        {
            var c = fixture.Factory.CreateClient();
            await barrier.Task;
            return await c.PostAsync($"/api/orders/{orderId}/cancel", null);
        }

        var t1 = Confirm();
        var t2 = Cancel();
        barrier.SetResult();

        var responses = await Task.WhenAll(t1, t2);

        // Tepat satu menang
        responses.Count(r => r.IsSuccessStatusCode).Should().Be(1);

        var loser = responses.Single(r => !r.IsSuccessStatusCode);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Status akhir harus konsisten dengan pemenang,
        // bukan campuran dari keduanya
        var final = await client.GetFromJsonAsync<OrderResponse>($"/api/orders/{orderId}");
        final!.Status.Should().BeOneOf(OrderStatus.Confirmed, OrderStatus.Cancelled);

        // Stock harus konsisten dengan status akhir:
        // Cancelled → dikembalikan (50), Confirmed → tetap terpakai (45)
        var stock = await fixture.GetStockAsync(DatabaseFixture.ProductX);
        var expected = final.Status == OrderStatus.Cancelled ? 50 : 45;
        stock.Should().Be(expected);
    }
}