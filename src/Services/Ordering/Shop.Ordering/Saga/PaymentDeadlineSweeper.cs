using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts.Ordering;
using Shop.Ordering.Data;

namespace Shop.Ordering.Saga;

/// <summary>
/// Looks every few seconds for orders still waiting for their payment past the deadline, and tells their saga.
/// The deadline lives in the saga's row, so it survives restarts, and several instances may sweep at once: the
/// saga only acts on the first notice and ignores the rest.
/// </summary>
public sealed partial class PaymentDeadlineSweeper(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration, ILogger<PaymentDeadlineSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Ordering:DeadlineSweepSeconds", 5));
        using var timer = new PeriodicTimer(interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var now = clock.GetUtcNow();

        var overdue = await db.Set<OrderState>().AsNoTracking()
            .Where(s => s.CurrentState == nameof(OrderStateMachine.AwaitingPayment) && s.PaymentDeadline < now)
            .OrderBy(s => s.PaymentDeadline)
            .Select(s => s.CorrelationId)
            .Take(100)
            .ToListAsync(ct);
        foreach (var orderId in overdue)
        {
            await publish.Publish(new PaymentDeadlinePassed(orderId, now), ct);
        }

        // Through the outbox, like everything else this service sends.
        await db.SaveChangesAsync(ct);
        if (overdue.Count > 0)
        {
            LogSwept(logger, overdue.Count);
        }

        return overdue.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} order(s) passed their payment deadline")]
    private static partial void LogSwept(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The payment deadline sweep failed; it runs again shortly")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
