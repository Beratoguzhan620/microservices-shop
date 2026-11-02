using Microsoft.EntityFrameworkCore;
using Shop.Identity.Data;
using Shop.Identity.Features;
using Shop.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("identity", "Identity",
    "Customer accounts and sign-in. Tokens are signed here with RS256; the other services only hold the public key."));

var connectionString = Persistence.ConnectionString(builder.Configuration, "Identity");
builder.Services.AddDbContext<IdentityDbContext>(options => IdentityDbContext.Configure(options, connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("database", tags: ["ready"]);
builder.Services.AddSingleton<Passwords>();
builder.Services.AddSingleton<TokenIssuer>();

var app = builder.Build();
app.UseServiceDefaults();
app.MapServiceDefaults();
app.MapAuthEndpoints();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}

await AdminSeeder.SeedAsync(app.Services);
await app.RunAsync();

