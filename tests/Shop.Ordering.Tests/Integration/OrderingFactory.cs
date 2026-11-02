using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Ordering.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Shop.Ordering.Orders;
using Shop.Ordering.Saga;
using Shop.ServiceDefaults.Auth;
using Shop.Testing;
using Testcontainers.RabbitMq;

namespace Shop.Ordering.Tests.Integration;

/// <summary>
/// Ordering on its own database and a real RabbitMQ, so the saga runs with its EF Core repository and the outbox
/// exactly as in production (the test harness would swap in an in-memory saga repository).
/// </summary>
public sealed class OrderingFactory : WebApplicationFactory<OrderingDbContext>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();
    private string _connectionString = string.Empty;

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public IBus Bus => Services.GetRequiredService<IBus>();

    public Task<int> SweepAsync() => Services.GetRequiredService<PaymentDeadlineSweeper>().SweepAsync(CancellationToken.None);

    public MessageLog Log { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Ordering", _connectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbit.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Ordering:PaymentTimeoutSeconds", "5");
        builder.UseSetting("Ordering:DeadlineSweepSeconds", "1");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.ConfigureTestServices(services =>
        {
            // Time moves only when a test moves it, so deadlines are reached on purpose, not by a slow machine.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient Admin() => CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), ShopRoles.Admin));

    public HttpClient Customer(Guid customerId) => CreateClient().WithToken(TestKeys.Token(customerId));

    public async Task<Order?> OrderAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId);
    }

    /// <summary>Waits until the order reaches the status (the saga updates it a moment after each message).</summary>
    public async Task<Order> WaitForAsync(Guid orderId, OrderStatus status, Func<Order, bool>? also = null)
    {
        Order? order = null;
        await Messages.UntilAsync(
            () => (order = OrderAsync(orderId).GetAwaiter().GetResult()) is { } o && o.Status == status && (also?.Invoke(o) ?? true),
            $"The order did not become {status}; it is {order?.Status.ToString() ?? "missing"}.");
        return order!;
    }

    public async Task<int> SagaCountAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Set<OrderState>().CountAsync(s => s.CorrelationId == orderId);
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.InitializeAsync(), _rabbit.StartAsync());
        _connectionString = await _postgres.CreateDatabaseAsync("ordering");
        CreateClient().Dispose();
        Log = await (await MessageLog.StartAsync(Services.GetRequiredService<IBus>()))
            .ListenAsync<Shop.Contracts.Ordering.OrderSubmitted>();
        await Log.ListenAsync<Shop.Contracts.Ordering.OrderConfirmed>();
        await Log.ListenAsync<Shop.Contracts.Ordering.OrderRejected>();
        await Log.ListenAsync<Shop.Contracts.Inventory.ReserveStock>();
        await Log.ListenAsync<Shop.Contracts.Inventory.CommitStock>();
        await Log.ListenAsync<Shop.Contracts.Inventory.ReleaseStock>();
        await Log.ListenAsync<Shop.Contracts.Payment.ProcessPayment>();
        await Log.ListenAsync<Shop.Contracts.Payment.RefundPayment>();
        await Log.ListenAsync<Shop.Contracts.Ordering.OrderShipped>();
        await Log.ListenAsync<Shop.Contracts.Ordering.OrderCancelled>();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class OrderingCollection : ICollectionFixture<OrderingFactory>
{
    public const string Name = "ordering";
}

