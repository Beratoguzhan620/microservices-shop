using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shop.Identity.Data;
using Shop.Testing;

namespace Shop.Identity.Tests.Integration;

public sealed class IdentityFactory : WebApplicationFactory<IdentityDbContext>, IAsyncLifetime
{
    public const string AdminEmail = "admin@shop.example";
    public const string AdminPassword = "admin-password-123";

    private readonly PostgresFixture _postgres = new();
    private string _connectionString = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Identity", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:PrivateKey", TestKeys.PrivateKeyPem);
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.UseSetting("Identity:Admin:Email", AdminEmail);
        builder.UseSetting("Identity:Admin:Password", AdminPassword);
    }

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _connectionString = await _postgres.CreateDatabaseAsync("identity");
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IdentityCollection : ICollectionFixture<IdentityFactory>
{
    public const string Name = "identity";
}
