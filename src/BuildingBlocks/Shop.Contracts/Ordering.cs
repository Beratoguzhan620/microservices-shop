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
    IReadOnlyList<OrderLine> Lines, ShippingAddress Address, DateTimeOffset At);

/// <summary>The order was stored and its process starts.</summary>
public sealed record OrderSubmitted(Guid OrderId, Guid CustomerId, IReadOnlyList<OrderLine> Lines, decimal Total, DateTimeOffset At);

/// <summary>The order is final: stock taken (and, from stage 3, paid).</summary>
public sealed record OrderConfirmed(Guid OrderId, DateTimeOffset At);

/// <summary>The order could not go ahead; nothing is kept for it.</summary>
public sealed record OrderRejected(Guid OrderId, string Reason, DateTimeOffset At);
