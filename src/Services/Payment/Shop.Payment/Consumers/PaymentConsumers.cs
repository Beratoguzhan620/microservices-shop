using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts.Payment;
using Shop.Payment.Data;
using Shop.Payment.Payments;

namespace Shop.Payment.Consumers;

/// <summary>
/// Charges an order once. A repeated request (a retry, a redelivery) finds the payment of the first one and
/// answers the same way, so a customer is never charged twice for one order.
/// </summary>
public sealed partial class ProcessPaymentConsumer(PaymentDbContext db, TimeProvider clock, ILogger<ProcessPaymentConsumer> logger) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;
        var now = clock.GetUtcNow();

        var earlier = await db.Payments.SingleOrDefaultAsync(p => p.OrderId == message.OrderId, ct);
        if (earlier is not null)
        {
            await PublishAsync(context, earlier, now);
            return;
        }

        var token = await db.CardTokens.SingleOrDefaultAsync(t => t.Token == message.PaymentToken, ct);
        var problem = token is null ? "The payment token is unknown." : token.Problem(message.CustomerId, now);
        if (problem is not null)
        {
            await context.Publish(new PaymentFailed(message.OrderId, problem, now), ct);
            return;
        }

        token!.Use(now);
        if (token.Behaviour == CardBehaviour.NoAnswer)
        {
            // The pretend network swallows the request; the order gives up when its payment deadline passes.
            LogNoAnswer(logger, message.OrderId);
            return;
        }

        var (status, reason) = token.Behaviour switch
        {
            CardBehaviour.Decline => (PaymentStatus.Declined, "The card was declined."),
            CardBehaviour.InsufficientFunds => (PaymentStatus.Declined, "The card has insufficient funds."),
            _ => (PaymentStatus.Succeeded, (string?)null),
        };
        var payment = Payments.Payment.Charge(message.OrderId, message.CustomerId, message.Amount, token.Last4, status, reason, now);
        db.Payments.Add(payment);
        LogCharged(logger, message.OrderId, status, token.Last4);
        await PublishAsync(context, payment, now);
    }

    private static Task PublishAsync(ConsumeContext context, Payments.Payment payment, DateTimeOffset now) =>
        payment.Status == PaymentStatus.Declined
            ? context.Publish(new PaymentFailed(payment.OrderId, payment.Reason ?? "The payment failed.", now))
            : context.Publish(new PaymentSucceeded(payment.OrderId, payment.Id, payment.Amount, payment.CardLast4, payment.CreatedAt));

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment for order {OrderId}: {Status} with card ending {Last4}")]
    private static partial void LogCharged(ILogger logger, Guid orderId, PaymentStatus status, string last4);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment for order {OrderId}: the card network did not answer")]
    private static partial void LogNoAnswer(ILogger logger, Guid orderId);
}

/// <summary>Gives the money back once; a second request, or one for an order that was never charged, does nothing.</summary>
public sealed class RefundPaymentConsumer(PaymentDbContext db, TimeProvider clock) : IConsumer<RefundPayment>
{
    public async Task Consume(ConsumeContext<RefundPayment> context)
    {
        var now = clock.GetUtcNow();

        // Locked, so two refund requests at the same moment cannot both see the payment as not yet refunded.
        var payment = (await db.Payments
            .FromSql($"SELECT * FROM payments WHERE order_id = {context.Message.OrderId} FOR UPDATE")
            .ToListAsync(context.CancellationToken)).SingleOrDefault();
        if (payment is not null && payment.Refund(context.Message.Reason, now))
        {
            await context.Publish(new PaymentRefunded(payment.OrderId, payment.Amount, now), context.CancellationToken);
        }
    }
}
