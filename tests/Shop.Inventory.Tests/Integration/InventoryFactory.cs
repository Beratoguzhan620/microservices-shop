using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Inventory.Data;
using Shop.Inventory.Stock;
using Shop.ServiceDefaults.Auth;
using Shop.Testing;

namespace Shop.Inventory.Tests.Integration;

/// <summary>Inventory on its own database, with an in-memory bus standing in for RabbitMQ.</summary>
public sealed class InventoryFactory : WebApplicationFactory<InventoryDbContext>, IAsyncLifetime
{
    public const int OpeningStock = 7;

    private readonly PostgresFixture _postgres = new();
    private string _connectionString = string.Empty;

    public ITestHarness Harness => Services.GetTestHarness();

    public MessageLog Log { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Inventory", _connectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Inventory:OpeningStock", OpeningStock.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }

    public HttpClient Admin() => CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), ShopRoles.Admin));

    /// <summary>A product with this much stock, straight in the database.</summary>
    public async Task<Guid> StockAsync(int onHand, string? sku = null)
    {
        var id = Guid.NewGuid();
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        db.StockItems.Add(StockItem.Track(id, sku ?? Http.Unique("SKU").ToUpperInvariant(), "Ürün", onHand, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<StockItem> ItemAsync(Guid productId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().StockItems.FindAsync(productId)
            ?? throw new InvalidOperationException("No such stock item.");
    }

    public async Task<List<Reservation>> ReservationsAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return db.Reservations.Where(r => r.OrderId == orderId).ToList();
    }

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _connectionString = await _postgres.CreateDatabaseAsync("inventory");
        CreateClient().Dispose();
        Log = await (await MessageLog.StartAsync(Services.GetRequiredService<IBus>()))
            .ListenAsync<Shop.Contracts.Inventory.StockReserved>();
        await Log.ListenAsync<Shop.Contracts.Inventory.StockReservationFailed>();
        await Log.ListenAsync<Shop.Contracts.Inventory.StockReleased>();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InventoryCollection : ICollectionFixture<InventoryFactory>
{
    public const string Name = "inventory";
}
