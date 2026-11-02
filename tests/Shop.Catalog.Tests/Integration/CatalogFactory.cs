using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Catalog.Data;
using Shop.ServiceDefaults.Auth;
using Shop.Testing;

namespace Shop.Catalog.Tests.Integration;

/// <summary>The Catalog service on its own database, seeded with the demo catalog.</summary>
public sealed class CatalogFactory : WebApplicationFactory<CatalogDbContext>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private string _connectionString = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Catalog", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Catalog:SeedDemoData", "true");
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }

    public ITestHarness Harness => Services.GetTestHarness();

    public HttpClient Admin() => CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), ShopRoles.Admin));

    public HttpClient Customer() => CreateClient().WithToken(TestKeys.Token(Guid.NewGuid()));

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _connectionString = await _postgres.CreateDatabaseAsync("catalog");
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogCollection : ICollectionFixture<CatalogFactory>
{
    public const string Name = "catalog";
}
