using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts.Ordering;
using Shop.Ordering.Data;
using Shop.Ordering.Orders;

namespace Shop.Ordering.Consumers;

/// <summary>
/// Turns a checkout into an order and starts its process. The order id comes from the basket, so a checkout that
/// arrives twice (a retry after a lost acknowledgement) finds its order already there and does nothing.
/// </summary>
public sealed class BasketCheckedOutConsumer(OrderingDbContext db) : IConsumer<BasketCheckedOut>
{
    public async Task Consume(ConsumeContext<BasketCheckedOut> context)
    {
        var checkout = context.Message;
        if (await db.Orders.AnyAsync(o => o.Id == checkout.OrderId, context.CancellationToken))
        {
            return;
        }

        var order = Order.Place(checkout);
        db.Orders.Add(order);
        await context.Publish(new OrderSubmitted(order.Id, order.CustomerId, order.Lines(), order.Total, checkout.PaymentToken, checkout.At), context.CancellationToken);
    }
}
