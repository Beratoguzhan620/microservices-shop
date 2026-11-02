namespace Shop.Contracts.Ordering;

public sealed record OrderLine(Guid ProductId, string Sku, string Name, decimal UnitPrice, int Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}

public sealed record ShippingAddress(string FullName, string Line1, string City, string PostalCode, string Phone);

/// <summary>
/// A customer checked out their basket. <see cref="OrderId"/> is chosen by the basket, so a checkout that is sent
/// twice (a retry, a double click) becomes one order.
/// </summary>
public sealed record BasketCheckedOut(
    Guid OrderId, Guid CustomerId, string CustomerEmail, string CustomerName,
    IReadOnlyList<OrderLine> Lines, ShippingAddress Address, string PaymentToken, DateTimeOffset At);

/// <summary>The order was stored and its process starts.</summary>
public sealed record OrderSubmitted(Guid OrderId, Guid CustomerId, IReadOnlyList<OrderLine> Lines, decimal Total, string PaymentToken, DateTimeOffset At);

/// <summary>Paid, with its stock held: the order will ship.</summary>
public sealed record OrderConfirmed(Guid OrderId, DateTimeOffset At);

/// <summary>Raised by Ordering's deadline sweeper when an order has waited too long for its payment.</summary>
public sealed record PaymentDeadlinePassed(Guid OrderId, DateTimeOffset At);

/// <summary>An admin marked the order as handed to the carrier.</summary>
public sealed record OrderShipmentRequested(Guid OrderId, string TrackingCode, DateTimeOffset At);

/// <summary>The customer or an admin cancelled the order before it shipped.</summary>
public sealed record OrderCancellationRequested(Guid OrderId, string Reason, DateTimeOffset At);

public sealed record OrderShipped(Guid OrderId, string TrackingCode, DateTimeOffset At);

public sealed record OrderCancelled(Guid OrderId, string Reason, DateTimeOffset At);

/// <summary>The order could not go ahead; nothing is kept for it.</summary>
public sealed record OrderRejected(Guid OrderId, string Reason, DateTimeOffset At);
