using Microsoft.EntityFrameworkCore;
using Shop.Messaging;
using Shop.Payment.Consumers;
using Shop.Payment.Data;
using Shop.Payment.Features;
using Shop.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("payment", "Payment",
    "Cards become one-time tokens here; orders are charged and refunded through messages, against a pretend card network with test cards."));

var connectionString = Persistence.ConnectionString(builder.Configuration, "Payment");
builder.Services.AddDbContext<PaymentDbContext>(options => PaymentDbContext.Configure(options, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>("database", tags: ["ready"]);
builder.Services.AddShopMessaging<PaymentDbContext>(builder.Configuration, bus =>
{
    bus.AddConsumer<ProcessPaymentConsumer>();
    bus.AddConsumer<RefundPaymentConsumer>();
});

var app = builder.Build();
app.UseServiceDefaults();
app.MapServiceDefaults();
app.MapPaymentEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
