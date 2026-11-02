using MassTransit;
using Shop.Contracts.Inventory;
using Shop.Contracts.Ordering;
using Shop.Ordering.Orders;

namespace Shop.Ordering.Saga;

/// <summary>
/// The order process, in one place. Each step reacts to the answer of the previous one and decides what comes next:
/// <code>
/// Submitted ── reserve stock ──▶ stock held ──▶ Confirmed (stock committed)
///                    └─────────▶ no stock   ──▶ Rejected
/// </code>
/// </summary>
public sealed class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => Submitted, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReserved, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReservationFailed, e => e.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(Submitted)
                .Then(context =>
                {
                    context.Saga.CustomerId = context.Message.CustomerId;
                    context.Saga.Total = context.Message.Total;
                    context.Saga.SubmittedAt = context.Message.At;
                })
                .Publish(context => new ReserveStock(
                    context.Message.OrderId,
                    context.Message.Lines.Select(l => new StockLine(l.ProductId, l.Quantity)).ToList()))
                .TransitionTo(ReservingStock));

        During(ReservingStock,
            When(StockReserved)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.Confirmed))
                .Publish(context => new CommitStock(context.Message.OrderId))
                .Publish(context => new OrderConfirmed(context.Message.OrderId, context.Message.At))
                .Finalize(),
            When(StockReservationFailed)
                .ThenAsync(context => context.MoveOrderAsync(OrderStatus.Rejected, context.Message.Reason))
                .Publish(context => new OrderRejected(context.Message.OrderId, context.Message.Reason, context.Message.At))
                .Finalize());

        SetCompletedWhenFinalized();
    }

    public State ReservingStock { get; private set; } = null!;

    public Event<OrderSubmitted> Submitted { get; private set; } = null!;

    public Event<StockReserved> StockReserved { get; private set; } = null!;

    public Event<StockReservationFailed> StockReservationFailed { get; private set; } = null!;
}
