using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Ordering.Data;
using Shop.Ordering.Orders;

namespace Shop.Ordering.Saga;

internal static class OrderUpdates
{
    /// <summary>
    /// Moves the order to a status from inside the saga. The <see cref="OrderingDbContext"/> comes from the message's
    /// scope, the same one the saga repository and the outbox use, so the order, the saga state and the messages the
    /// step sends are saved in one transaction.
    /// </summary>
    public static async Task MoveOrderAsync<TMessage>(this BehaviorContext<OrderState, TMessage> context, OrderStatus status, string? reason = null)
        where TMessage : class
    {
        await context.ChangeOrderAsync((order, now) => order.MoveTo(status, now, reason));
    }

    public static Task PaidAsync<TMessage>(this BehaviorContext<OrderState, TMessage> context, string cardLast4)
        where TMessage : class => context.ChangeOrderAsync((order, now) => order.Paid(cardLast4, now));

    public static Task ShippedAsync<TMessage>(this BehaviorContext<OrderState, TMessage> context, string trackingCode)
        where TMessage : class => context.ChangeOrderAsync((order, now) => order.Ship(trackingCode, now));

    public static Task RefundedAsync<TMessage>(this BehaviorContext<OrderState, TMessage> context)
        where TMessage : class => context.ChangeOrderAsync((order, now) => order.MarkRefunded(now));

    private static async Task ChangeOrderAsync<TMessage>(this BehaviorContext<OrderState, TMessage> context, Action<Order, DateTimeOffset> change)
        where TMessage : class
    {
        var db = context.GetServiceOrCreateInstance<OrderingDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Id == context.Saga.CorrelationId, context.CancellationToken);
        change(order, context.GetServiceOrCreateInstance<TimeProvider>().GetUtcNow());
    }

    public static async Task<Order> OrderAsync<TMessage>(this BehaviorContext<OrderState, TMessage> context)
        where TMessage : class =>
        await context.GetServiceOrCreateInstance<OrderingDbContext>().Orders.AsNoTracking()
            .SingleAsync(o => o.Id == context.Saga.CorrelationId, context.CancellationToken);
}
