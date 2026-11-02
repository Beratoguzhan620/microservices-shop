using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shop.ServiceDefaults;

/// <summary>
/// Distributed tracing: one trace follows an order from the gateway through every HTTP call, every RabbitMQ message
/// (MassTransit carries the trace context in the message headers) and every SQL statement, across all services.
/// Exported over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set; Compose sends it to Jaeger.
/// </summary>
public static class Tracing
{
    public static WebApplicationBuilder AddTracing(this WebApplicationBuilder builder, ServiceInfo service)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return builder;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName: service.Name, serviceNamespace: "shop"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = request =>
                    request.RequestUri?.AbsolutePath.StartsWith("/health", StringComparison.Ordinal) != true)
                .AddSource("MassTransit")
                .AddNpgsql()
                .AddOtlpExporter());
        return builder;
    }
}
