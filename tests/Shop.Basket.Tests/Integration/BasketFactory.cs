using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shop.Basket.Baskets;
using Shop.Testing;
using Testcontainers.Redis;

namespace Shop.Basket.Tests.Integration;

/// <summary>A catalog that answers from a dictionary the tests change, and can be switched off.</summary>
public sealed class FakeCatalog : HttpMessageHandler
{
    public ConcurrentDictionary<Guid, ProductSnapshot> Products { get; } = new();

    public bool Down { get; set; }

    public Guid Add(string name, decimal price, bool active = true)
    {
        var id = Guid.NewGuid();
        Products[id] = new ProductSnapshot(id, Http.Unique("SKU").ToUpperInvariant(), name, price, active);
        return id;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        var ids = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query).GetValues("ids") ?? [];
        var found = ids.Select(Guid.Parse).Where(Products.ContainsKey).Select(id => Products[id]).ToList();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(found) });
    }
}

public sealed class BasketFactory : WebApplicationFactory<BasketStore>, IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public FakeCatalog Catalog { get; } = new();

    public ITestHarness Harness => Services.GetTestHarness();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://guest:guest@localhost:5672");
        builder.UseSetting("Services:Catalog", "http://catalog.test");
        builder.UseSetting("Jwt:PublicKey", TestKeys.PublicKeyPem);
        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.AddHttpClient<CatalogClient>().ConfigurePrimaryHttpMessageHandler(() => Catalog);
        });
    }

    public HttpClient Customer(Guid? id = null) => CreateClient().WithToken(TestKeys.Token(id ?? Guid.NewGuid()));

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class BasketCollection : ICollectionFixture<BasketFactory>
{
    public const string Name = "basket";
}
