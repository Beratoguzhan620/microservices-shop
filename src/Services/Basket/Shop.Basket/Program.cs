using MassTransit;
using Shop.Basket.Baskets;
using Shop.Basket.Features;
using Shop.ServiceDefaults;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("basket", "Basket",
    "Each customer's basket, kept in Redis. Checking out checks prices against the catalog and hands the order to Ordering."));

var redis = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured, for example localhost:6379.");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    // Keep trying in the background rather than failing at start: readiness reports Redis until it answers.
    var options = ConfigurationOptions.Parse(redis);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});
builder.Services.AddSingleton<BasketStore>();
builder.Services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

builder.Services.AddHttpClient<CatalogClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]
            ?? throw new InvalidOperationException("Services:Catalog is not configured, for example http://localhost:5102.")))
    .AddStandardResilienceHandler();

// No database here, so no outbox: the basket is only cleared once the order message is out, and Ordering
// recognises the order id if a checkout is ever delivered twice.
builder.Services.AddMassTransit(bus =>
{
    bus.SetKebabCaseEndpointNameFormatter();
    bus.UsingRabbitMq((context, rabbit) =>
    {
        rabbit.Host(new Uri(builder.Configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not configured.")));
        rabbit.ConfigureEndpoints(context);
    });
});

var app = builder.Build();
app.UseServiceDefaults();
app.MapServiceDefaults();
app.MapBasketEndpoints();
await app.RunAsync();
