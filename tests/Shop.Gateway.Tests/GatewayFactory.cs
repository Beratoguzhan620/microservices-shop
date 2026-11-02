using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shop.Testing;

namespace Shop.Gateway.Tests;

/// <summary>The gateway in front of stub services, with a low sign-in limit so the limiter can be seen working.</summary>
public sealed class GatewayFactory : WebApplicationFactory<DownstreamHealthCheck>, IAsyncLifetime
{
    public const int AuthPerMinute = 5;

    public static readonly string[] Clusters = ["identity", "catalog", "basket", "inventory", "ordering"];

    public Dictionary<string, StubService> Stubs { get; } = [];

    public StubService Catalog => Stubs["catalog"];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.UseSetting("RateLimits:AuthPerMinute", AuthPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (name, stub) in Stubs)
        {
            builder.UseSetting($"ReverseProxy:Clusters:{name}:Destinations:primary:Address", stub.Address);
        }
    }

    public async Task InitializeAsync()
    {
        foreach (var name in Clusters)
        {
            Stubs[name] = await StubService.StartAsync(name);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var stub in Stubs.Values)
        {
            await stub.DisposeAsync();
        }
    }
}
