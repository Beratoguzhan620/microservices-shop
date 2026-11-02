namespace Shop.Payment.Payments;

public enum PaymentStatus
{
    Succeeded,
    Declined,
    Refunded,
}

/// <summary>One charge per order: the order id is the key, so a repeated request finds the earlier result.</summary>
public sealed class Payment
{
    private Payment()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string CardLast4 { get; private set; } = string.Empty;

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Payment Charge(Guid orderId, Guid customerId, decimal amount, string last4, PaymentStatus status, string? reason, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        OrderId = orderId,
        CustomerId = customerId,
        Amount = amount,
        CardLast4 = last4,
        Status = status,
        Reason = reason,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <returns>Whether money went back now; a declined or already refunded payment has nothing to give back.</returns>
    public bool Refund(string reason, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Succeeded)
        {
            return false;
        }

        Status = PaymentStatus.Refunded;
        Reason = reason;
        UpdatedAt = now;
        return true;
    }
}
