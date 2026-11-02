using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Gateway.Tests;

/// <summary>
/// A real HTTP server on a free port standing in for a service behind the gateway. It echoes what it received, so
/// tests can see exactly what the gateway forwarded.
/// </summary>
public sealed class StubService : IAsyncDisposable
{
    private readonly WebApplication _app;

    private StubService(WebApplication app, string name)
    {
        _app = app;
        Name = name;
    }

    public string Name { get; }

    public bool Ready { get; set; } = true;

    public string Address => _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    public static async Task<StubService> StartAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var stub = new StubService(app, name);
        app.MapGet("/health/ready", () => stub.Ready ? Results.Ok("Healthy") : Results.StatusCode(503));
        app.MapGet("/openapi/v1.json", () => Results.Json(new { openapi = "3.1.0", info = new { title = name } }));
        app.Map("/{**path}", (HttpContext context) => Results.Json(new Echo(
            name,
            context.Request.Method,
            context.Request.Path.Value!,
            context.Request.QueryString.Value ?? string.Empty,
            context.Request.Headers["X-Correlation-Id"].ToString(),
            context.Request.Headers.Authorization.ToString())));
        await app.StartAsync();
        return stub;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed record Echo(string Service, string Method, string Path, string Query, string CorrelationId, string Authorization);
