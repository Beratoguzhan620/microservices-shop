namespace Shop.Contracts.Payment;

/// <summary>Charge the order with the card behind this token (the card number itself never travels in messages).</summary>
public sealed record ProcessPayment(Guid OrderId, Guid CustomerId, decimal Amount, string PaymentToken);

public sealed record PaymentSucceeded(Guid OrderId, Guid PaymentId, decimal Amount, string CardLast4, DateTimeOffset At);

public sealed record PaymentFailed(Guid OrderId, string Reason, DateTimeOffset At);

/// <summary>Give the money back: the order was cancelled, or the payment came in after the order had given up on it.</summary>
public sealed record RefundPayment(Guid OrderId, string Reason);

public sealed record PaymentRefunded(Guid OrderId, decimal Amount, DateTimeOffset At);
