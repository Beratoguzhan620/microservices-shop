using Shop.Contracts.Ordering;

namespace Shop.Ordering.Orders;

public enum OrderStatus
{
    /// <summary>Received; stock is being held.</summary>
    Submitted,

    /// <summary>Stock is held; waiting for the payment.</summary>
    AwaitingPayment,

    /// <summary>Paid and stock taken: the order will ship.</summary>
    Confirmed,

    /// <summary>On its way to the customer.</summary>
    Shipped,

    /// <summary>Could not go ahead (no stock, payment declined or not made in time); nothing was kept.</summary>
    Rejected,

    /// <summary>Cancelled by the customer or an admin before shipping; payment refunded and stock returned.</summary>
    Cancelled,
}

public sealed class OrderItem
{
    public Guid ProductId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public decimal UnitPrice { get; init; }

    public int Quantity { get; init; }
}

public sealed class Address
{
    public string FullName { get; init; } = string.Empty;

    public string Line1 { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;
}

/// <summary>
/// What a customer ordered, at the prices they agreed to, and where the order stands. The saga moves it between
/// statuses; nothing else does.
/// </summary>
public sealed class Order
{
    private Order()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string CustomerEmail { get; private set; } = string.Empty;

    public string CustomerName { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }

    public List<OrderItem> Items { get; private set; } = [];

    public Address ShippingAddress { get; private set; } = new();

    public decimal Total { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Order Place(BasketCheckedOut checkout) => new()
    {
        Id = checkout.OrderId,
        CustomerId = checkout.CustomerId,
        CustomerEmail = checkout.CustomerEmail,
        CustomerName = checkout.CustomerName,
        Status = OrderStatus.Submitted,
        Items = checkout.Lines.Select(l => new OrderItem { ProductId = l.ProductId, Sku = l.Sku, Name = l.Name, UnitPrice = l.UnitPrice, Quantity = l.Quantity }).ToList(),
        ShippingAddress = new Address
        {
            FullName = checkout.Address.FullName, Line1 = checkout.Address.Line1, City = checkout.Address.City,
            PostalCode = checkout.Address.PostalCode, Phone = checkout.Address.Phone,
        },
        Total = checkout.Lines.Sum(l => l.UnitPrice * l.Quantity),
        CreatedAt = checkout.At,
        UpdatedAt = checkout.At,
    };

    public void MoveTo(OrderStatus status, DateTimeOffset now, string? reason = null)
    {
        Status = status;
        Reason = reason ?? Reason;
        UpdatedAt = now;
    }

    public IReadOnlyList<OrderLine> Lines() =>
        Items.Select(i => new OrderLine(i.ProductId, i.Sku, i.Name, i.UnitPrice, i.Quantity)).ToList();
}
