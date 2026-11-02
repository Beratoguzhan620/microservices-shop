using System.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Messaging;

/// <summary>
/// RabbitMQ through MassTransit, set up the same way in every service that keeps data:
/// <list type="bullet">
/// <item>The <b>bus outbox</b>: messages a request publishes are written to the service's database in the same
/// transaction as its change, and sent from there; a crash in between loses neither.</item>
/// <item>The <b>consumer outbox and inbox</b>: a consumer's changes and the messages it sends commit together, and a
/// message that arrives twice is recognised and skipped.</item>
/// <item>Retries for passing failures; a message that keeps failing goes to its queue's <c>_error</c> queue.</item>
/// </list>
/// </summary>
public static class Messaging
{
    public static IServiceCollection AddShopMessaging<TDbContext>(
        this IServiceCollection services, IConfiguration configuration, Action<IBusRegistrationConfigurator>? configure = null)
        where TDbContext : DbContext
    {
        services.AddMassTransit(bus =>
        {
            bus.SetKebabCaseEndpointNameFormatter();
            configure?.Invoke(bus);

            bus.AddEntityFrameworkOutbox<TDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.IsolationLevel = IsolationLevel.ReadCommitted;
                outbox.QueryDelay = TimeSpan.FromMilliseconds(500);
                outbox.DuplicateDetectionWindow = TimeSpan.FromDays(1);
            });

            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(retry => retry.Intervals(100, 500, 1000, 2000, 5000));
                endpoint.UseEntityFrameworkOutbox<TDbContext>(context);
            });

            bus.UsingRabbitMq((context, rabbit) =>
            {
                rabbit.Host(new Uri(configuration.GetConnectionString("RabbitMq")
                    ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is not configured, for example amqp://guest:guest@localhost:5672.")));
                rabbit.ConfigureEndpoints(context);
            });
        });
        return services;
    }

    /// <summary>The outbox and inbox tables, in the service's own database.</summary>
    public static ModelBuilder AddOutboxTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
        return modelBuilder;
    }
}
