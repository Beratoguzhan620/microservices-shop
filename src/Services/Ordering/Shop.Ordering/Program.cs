using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Ordering.Consumers;
using Shop.Ordering.Data;
using Shop.Ordering.Features;
using Shop.Ordering.Saga;
using Shop.Messaging;
using Shop.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("ordering", "Ordering",
    "Orders and the process behind each one: a saga that holds stock, takes the payment and confirms, or undoes what was done."));

var connectionString = Persistence.ConnectionString(builder.Configuration, "Ordering");
builder.Services.AddDbContext<OrderingDbContext>(options => OrderingDbContext.Configure(options, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<OrderingDbContext>("database", tags: ["ready"]);
builder.Services.AddShopMessaging<OrderingDbContext>(builder.Configuration, bus =>
{
    bus.AddConsumer<BasketCheckedOutConsumer>();
    bus.AddSagaStateMachine<OrderStateMachine, OrderState>()
        .EntityFrameworkRepository(repository =>
        {
            repository.ExistingDbContext<OrderingDbContext>();
            repository.UsePostgres();
        });
});

var app = builder.Build();
app.UseServiceDefaults();
app.MapServiceDefaults();
app.MapOrderEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
