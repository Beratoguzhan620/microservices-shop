using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Shop.Gateway;
using Shop.ServiceDefaults;
using Shop.ServiceDefaults.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("gateway", "Shop gateway", "The single entry point in front of the services."));

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHttpClient(DownstreamHealthCheck.ClientName, client => client.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddHealthChecks().AddCheck<DownstreamHealthCheck>("services", tags: ["ready"]);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Everything through the gateway, per address: generous for a person browsing, a brake on scripts.
    var perMinute = builder.Configuration.GetValue("RateLimits:RequestsPerMinute", 600);
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/health")
            ? RateLimitPartition.GetNoLimiter(string.Empty)
            : RateLimitPartition.GetFixedWindowLimiter(GatewayPolicies.ClientKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Signing in and registering, per address: slow enough to make guessing passwords pointless.
    var authPerMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);
    options.AddPolicy(GatewayPolicies.AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(GatewayPolicies.ClientKey(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = GatewayPolicies.RejectAsync;
});

var app = builder.Build();
app.UseServiceDefaults();
app.UseRateLimiter();

app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Shop API")
        .WithTheme(ScalarTheme.Default)
        .ForceLightMode()
        .HideDarkModeToggle()
        .DisableDefaultFonts()
        .WithCustomCss(ScalarStyle.Css)
        .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
        .HideDeveloperTools()
        .HideClientButton()
        .DisableAgent()
        .DisableMcp()
        .DisableTelemetry();

    // One reference for the whole shop: each service's document, fetched through the gateway.
    foreach (var (name, title) in GatewayPolicies.Documents)
    {
        options.AddDocument(name, title, $"/openapi/{name}.json", isDefault: name == "catalog");
    }
});
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = DownstreamHealthCheck.WriteAsync,
});
app.MapReverseProxy();

await app.RunAsync();
