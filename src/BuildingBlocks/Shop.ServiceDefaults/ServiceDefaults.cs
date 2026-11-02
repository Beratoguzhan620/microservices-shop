using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;
using Shop.ServiceDefaults.Logging;
using Shop.ServiceDefaults.OpenApi;

namespace Shop.ServiceDefaults;

/// <summary>What the API reference says about a service.</summary>
public sealed record ServiceInfo(string Name, string Title, string Description);

/// <summary>
/// What every service has in common, set up the same way everywhere: structured logs with masked personal data and
/// the correlation id, problem responses with stable codes, JSON settings, token validation, OpenAPI and health.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, ServiceInfo service, bool authentication = true)
    {
        builder.Services.AddSingleton(service);
        builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", service.Name));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();

        if (authentication)
        {
            builder.Services.AddShopAuthentication(builder.Configuration);
        }

        builder.Services.AddOpenApi(options => options
            .AddDocumentTransformer<DocumentInfoTransformer>()
            .AddDocumentTransformer<SecuritySchemesTransformer>()
            .AddOperationTransformer<SecuritySchemesTransformer>());
        builder.Services.AddHealthChecks();
        return builder;
    }

    /// <summary>The middleware every service runs, in order, before its own endpoints.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app, bool authentication = true)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, error) =>
            error is not null || context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
            : context.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information);
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        if (authentication)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        return app;
    }

    /// <summary>Health probes, the OpenAPI document and the service's own API reference.</summary>
    public static WebApplication MapServiceDefaults(this WebApplication app)
    {
        var service = app.Services.GetRequiredService<ServiceInfo>();
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle(service.Title)
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
            .DisableTelemetry());
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        return app;
    }
}
