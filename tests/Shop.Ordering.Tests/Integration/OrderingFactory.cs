using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Ordering.Data;
using Microsoft.EntityFrameworkCore;
using Shop.Ordering.Orders;
using Shop.Ordering.Saga;
using Shop.ServiceDefaults.Auth;
using Shop.Testing;

namespace Shop.Ordering.Tests.Integration;

/// <summary>Ordering on its own database, with an in-memory bus standing in for RabbitMQ.</summary>
public sealed class OrderingFactory : WebApplicationFactory<OrderingDbContext>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _connectionString = string.Empty;

    public ITestHarness Harness => Services.GetTestHarness();

    public MessageLog Log { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ordering", _connectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }

    public HttpClient Admin() => CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), ShopRoles.Admin));

    public HttpClient Customer(Guid customerId) => CreateClient().WithToken(TestKeys.Token(customerId));

    public async Task<Order?> OrderAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
    }

    public async Task<int> SagaCountAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Set<OrderState>().CountAsync(s => s.CorrelationId == orderId);
    }

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _connectionString = await _postgres.CreateDatabaseAsync("ordering");
        CreateClient().Dispose();
        Log = await (await MessageLog.StartAsync(Services.GetRequiredService<IBus>()))
            .ListenAsync<Shop.Contracts.Ordering.OrderSubmitted>();
        await Log.ListenAsync<Shop.Contracts.Ordering.OrderConfirmed>();
        await Log.ListenAsync<Shop.Contracts.Ordering.OrderRejected>();
        await Log.ListenAsync<Shop.Contracts.Inventory.ReserveStock>();
        await Log.ListenAsync<Shop.Contracts.Inventory.CommitStock>();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class OrderingCollection : ICollectionFixture<OrderingFactory>
{
    public const string Name = "ordering";
}

