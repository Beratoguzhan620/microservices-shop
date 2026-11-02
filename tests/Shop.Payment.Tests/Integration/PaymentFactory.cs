using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Payment.Data;
using Microsoft.EntityFrameworkCore;
using Shop.ServiceDefaults.Auth;
using Shop.Testing;

namespace Shop.Payment.Tests.Integration;

/// <summary>Payment on its own database, with an in-memory bus standing in for RabbitMQ.</summary>
public sealed class PaymentFactory : WebApplicationFactory<PaymentDbContext>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _connectionString = string.Empty;

    public ITestHarness Harness => Services.GetTestHarness();

    public MessageLog Log { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Payment", _connectionString);
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }

    public HttpClient Admin() => CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), ShopRoles.Admin));

    public HttpClient Customer(Guid customerId) => CreateClient().WithToken(TestKeys.Token(customerId));

    public async Task<List<Payments.Payment>> PaymentsAsync(Guid orderId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Payments.AsNoTracking().Where(p => p.OrderId == orderId).ToListAsync();
    }

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _connectionString = await _postgres.CreateDatabaseAsync("payment");
        CreateClient().Dispose();
        Log = await (await MessageLog.StartAsync(Services.GetRequiredService<IBus>()))
            .ListenAsync<Shop.Contracts.Payment.PaymentSucceeded>();
        await Log.ListenAsync<Shop.Contracts.Payment.PaymentFailed>();
        await Log.ListenAsync<Shop.Contracts.Payment.PaymentRefunded>();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentCollection : ICollectionFixture<PaymentFactory>
{
    public const string Name = "payment";
}

