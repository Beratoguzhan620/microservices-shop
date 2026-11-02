using MassTransit;
using Shop.Contracts.Inventory;
using Shop.Contracts.Ordering;
using Shop.Contracts.Payment;
using Shop.Ordering.Orders;

namespace Shop.Ordering.Saga;

/// <summary>
/// The order process, in one place. Each step reacts to the answer of the previous one, and every way out undoes
/// what was done before it:
/// <code>
/// Submitted ─ReserveStock─▶ ReservingStock ─StockReserved─▶ AwaitingPayment ─PaymentSucceeded─▶ Confirmed ─ship─▶ Shipped
///                              │ no stock                       │ declined / deadline passed       │ cancel
///                              ▼                                ▼                                  ▼
///                          Rejected                    Rejected + ReleaseStock        Cancelled + ReleaseStock + RefundPayment
///                                                       (a payment that still arrives is refunded)
/// </code>
/// </summary>
public sealed class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine(IConfiguration configuration)
    {
        var paymentTimeout = TimeSpan.FromSeconds(configuration.GetValue("Ordering:PaymentTimeoutSeconds", 120));
        InstanceState(x => x.CurrentState);

        Event(() => Submitted, e => e.CorrelateById(m => m.Message.OrderId));

        // A message for an order whose process has ended (a late answer, a repeat) is dropped, not retried.
        Event(() => StockReserved, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => StockReservationFailed, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => PaymentSucceeded, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => PaymentFailed, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => DeadlinePassed, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => ShipmentRequested, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => CancellationRequested, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));
        Event(() => PaymentRefunded, e => e.CorrelateById(m => m.Message.OrderId).OnMissingInstance(m => m.Discard()));

        Initially(
            When(Submitted)
                .Then(context =>
                {
                    context.Saga.CustomerId = context.Message.CustomerId;
                    context.Saga.Total = context.Message.Total;
                    context.Saga.SubmittedAt = context.Message.At;
                    context.Saga.PaymentToken = context.Message.PaymentToken;
                })
                .Publish(context => new ReserveStock(
                    context.Message.OrderId,
                    context.Message.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList()))
                .TransitionTo(ReservingStock));

        During(ReservingStock,
            When(StockReserved)
                .Then(context => context.Saga.PaymentDeadline = context.GetServiceOrCreateInstance<TimeProvider>().GetUtcNow() + paymentTimeout)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.AwaitingPayment))
                .Publish(context => new ProcessPayment(context.Saga.CorrelationId, context.Saga.CustomerId, context.Saga.Total, context.Saga.PaymentToken))
                .TransitionTo(AwaitingPayment),
            When(StockReservationFailed)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.Rejected, context.Message.Reason))
                .Publish(context => new OrderRejected(context.Message.OrderId, context.Message.Reason, context.Message.At))
                .Finalize());

        During(AwaitingPayment,
            When(PaymentSucceeded)
                .Then(context => context.Saga.PaymentDeadline = null)
                .ThenAsync(context => context.PaidAsync(context.Message.CardLast4))
                .Publish(context => new OrderConfirmed(context.Message.OrderId, context.Message.At))
                .TransitionTo(Confirmed),
            When(PaymentFailed)
                .Then(context => context.Saga.PaymentDeadline = null)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.Rejected, context.Message.Reason))
                .Publish(context => new ReleaseStock(context.Message.OrderId, context.Message.Reason))
                .Publish(context => new OrderRejected(context.Message.OrderId, context.Message.Reason, context.Message.At))
                .Finalize(),
            When(DeadlinePassed)
                .Then(context => context.Saga.PaymentDeadline = null)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.Rejected, TimedOutReason))
                .Publish(context => new ReleaseStock(context.Message.OrderId, TimedOutReason))
                .Publish(context => new OrderRejected(context.Message.OrderId, TimedOutReason, context.Message.At))
                .TransitionTo(TimedOut));

        // The order gave up on its payment; if the money still arrives, it goes straight back.
        During(TimedOut,
            When(PaymentSucceeded)
                .Publish(context => new RefundPayment(context.Message.OrderId, "The payment arrived after the order had timed out."))
                .ThenAsync(context => context.RefundedAsync())
                .Finalize(),
            When(PaymentFailed).Finalize());

        During(Confirmed,
            When(ShipmentRequested)
                .ThenAsync(context => context.ShippedAsync(context.Message.TrackingCode))
                .Publish(context => new CommitStock(context.Message.OrderId))
                .Publish(context => new OrderShipped(context.Message.OrderId, context.Message.TrackingCode, context.Message.At))
                .Finalize(),
            When(CancellationRequested)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.Cancelled, context.Message.Reason))
                .Publish(context => new ReleaseStock(context.Message.OrderId, context.Message.Reason))
                .Publish(context => new RefundPayment(context.Message.OrderId, context.Message.Reason))
                .Publish(context => new OrderCancelled(context.Message.OrderId, context.Message.Reason, context.Message.At))
                .TransitionTo(Refunding));

        During(Refunding,
            When(PaymentRefunded)
                .ThenAsync(context => context.RefundedAsync())
                .Finalize());

        // Late or repeated messages for an order that has moved on change nothing; requests that do not fit the
        // order's state (shipping an unpaid order) are refused by the API before they get here.
        OnUnhandledEvent(context => context.Ignore());

        SetCompletedWhenFinalized();
    }

    public const string TimedOutReason = "The payment did not complete in time.";

    public State ReservingStock { get; private set; } = null!;

    public State AwaitingPayment { get; private set; } = null!;

    public State Confirmed { get; private set; } = null!;

    public State TimedOut { get; private set; } = null!;

    public State Refunding { get; private set; } = null!;

    public Event<OrderSubmitted> Submitted { get; private set; } = null!;

    public Event<StockReserved> StockReserved { get; private set; } = null!;

    public Event<StockReservationFailed> StockReservationFailed { get; private set; } = null!;

    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; } = null!;

    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;

    public Event<PaymentDeadlinePassed> DeadlinePassed { get; private set; } = null!;

    public Event<OrderShipmentRequested> ShipmentRequested { get; private set; } = null!;

    public Event<OrderCancellationRequested> CancellationRequested { get; private set; } = null!;

    public Event<PaymentRefunded> PaymentRefunded { get; private set; } = null!;
}
