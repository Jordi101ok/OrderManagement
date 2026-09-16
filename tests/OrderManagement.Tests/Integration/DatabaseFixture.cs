using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Persistence;
using Respawn;

namespace OrderManagement.Tests.Integration;

public class DatabaseFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; private set; } = null!;
    private Respawner _respawner = null!;
    private NpgsqlConnection _connection = null!;

    public static readonly Guid ProductX = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid CustomerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public async Task InitializeAsync()
    {
        Factory = new ApiFactory();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Warm-up: Neon free tier autosuspend, query pertama bisa lambat
        await db.Database.MigrateAsync();

        _connection = new NpgsqlConnection(ApiFactory.ConnectionString);
        await _connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_connection, new RespawnerOptions
        {
            SchemasToInclude = ["oms"],
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = [new Respawn.Graph.Table("oms", "__EFMigrationsHistory")]
        });
    }

    /// <summary>Reset database ke kondisi bersih + seed produk.</summary>
    public async Task ResetAsync(int productXStock = 15)
    {
        await _respawner.ResetAsync(_connection);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Products.Add(new Product
        {
            Id = ProductX,
            Name = "Product X",
            StockQuantity = productXStock,
            Price = 50000
        });

        await db.SaveChangesAsync();
    }

    public async Task<int> GetStockAsync(Guid productId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products.AsNoTracking()
            .FirstAsync(p => p.Id == productId);
        return product.StockQuantity;
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;