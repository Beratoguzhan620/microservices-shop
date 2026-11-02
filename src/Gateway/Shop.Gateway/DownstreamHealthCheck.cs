using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Configuration;

namespace Shop.Gateway;

/// <summary>
/// The gateway is ready when every service behind it is: it asks each cluster's <c>/health/ready</c> in parallel and
/// reports them by name, so one look shows which service is down.
/// </summary>
public sealed class DownstreamHealthCheck(IHttpClientFactory clients, IProxyConfigProvider proxy) : IHealthCheck
{
    public const string ClientName = "health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var client = clients.CreateClient(ClientName);
        var checks = proxy.GetConfig().Clusters
            .Select(cluster => (cluster.ClusterId, Address: cluster.Destinations?.Values.FirstOrDefault()?.Address))
            .Where(c => c.Address is not null)
            .Select(async c =>
            {
                try
                {
                    using var response = await client.GetAsync(new Uri(new Uri(c.Address!), "/health/ready"), cancellationToken);
                    return (c.ClusterId, Healthy: response.IsSuccessStatusCode);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return (c.ClusterId, Healthy: false);
                }
            });

        var results = await Task.WhenAll(checks);
        var data = results.ToDictionary(r => r.ClusterId, r => (object)(r.Healthy ? "Healthy" : "Unhealthy"));
        return results.All(r => r.Healthy)
            ? HealthCheckResult.Healthy("All services are ready.", data)
            : HealthCheckResult.Unhealthy($"Not ready: {string.Join(", ", results.Where(r => !r.Healthy).Select(r => r.ClusterId))}.", data: data);
    }

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var services = report.Entries.SelectMany(e => e.Value.Data).ToDictionary(d => d.Key, d => d.Value);
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString(), services }));
    }
}
