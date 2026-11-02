using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shop.Testing;

namespace Shop.Gateway.Tests;

/// <summary>The gateway in front of stub services, with a low sign-in limit so the limiter can be seen working.</summary>
public sealed class GatewayFactory : WebApplicationFactory<DownstreamHealthCheck>, IAsyncLifetime
{
    public const int AuthPerMinute = 5;

    public StubService Identity { get; private set; } = null!;

    public StubService Catalog { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.UseSetting("RateLimits:AuthPerMinute", AuthPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("ReverseProxy:Clusters:identity:Destinations:primary:Address", Identity.Address);
        builder.UseSetting("ReverseProxy:Clusters:catalog:Destinations:primary:Address", Catalog.Address);
    }

    public async Task InitializeAsync()
    {
        Identity = await StubService.StartAsync("identity");
        Catalog = await StubService.StartAsync("catalog");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Identity.DisposeAsync();
        await Catalog.DisposeAsync();
    }
}
