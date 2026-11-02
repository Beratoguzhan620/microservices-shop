using Microsoft.EntityFrameworkCore;
using Shop.Catalog.Data;
using Shop.Catalog.Features;
using Shop.Messaging;
using Shop.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("catalog", "Catalog",
    "Products, categories and prices. Anyone can browse and search; admins add, change and archive products."));

var connectionString = Persistence.ConnectionString(builder.Configuration, "Catalog");
builder.Services.AddDbContext<CatalogDbContext>(options => CatalogDbContext.Configure(options, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("database", tags: ["ready"]);
builder.Services.AddShopMessaging<CatalogDbContext>(builder.Configuration);

var app = builder.Build();
app.UseServiceDefaults();
app.MapServiceDefaults();
app.MapCatalogEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
}

if (app.Configuration.GetValue("Catalog:SeedDemoData", false))
{
    await DemoCatalog.SeedAsync(app.Services);
}

await app.RunAsync();
