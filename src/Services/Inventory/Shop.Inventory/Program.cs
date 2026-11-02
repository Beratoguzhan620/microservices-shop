using Microsoft.EntityFrameworkCore;
using Shop.Inventory.Consumers;
using Shop.Inventory.Data;
using Shop.Inventory.Features;
using Shop.Messaging;
using Shop.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("inventory", "Inventory",
    "Stock per product, and stock held for orders in progress. Orders reserve, commit and release stock through messages; admins count it here."));

var connectionString = Persistence.ConnectionString(builder.Configuration, "Inventory");
builder.Services.AddDbContext<InventoryDbContext>(options => InventoryDbContext.Configure(options, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("database", tags: ["ready"]);
builder.Services.AddShopMessaging<InventoryDbContext>(builder.Configuration, bus =>
{
    bus.AddConsumer<ProductCreatedConsumer>();
    bus.AddConsumer<ReserveStockConsumer>();
    bus.AddConsumer<CommitStockConsumer>();
    bus.AddConsumer<ReleaseStockConsumer>();
});

var app = builder.Build();
app.UseServiceDefaults();
app.MapServiceDefaults();
app.MapInventoryEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
