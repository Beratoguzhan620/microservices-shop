using Shop.ServiceDefaults.Errors;

namespace Shop.Inventory.Stock;

/// <summary>
/// How many of a product the warehouse has, and how many of those are held for orders that are still going
/// through. Only <see cref="Available"/> can be promised to a new order.
/// </summary>
public sealed class StockItem
{
    private StockItem()
    {
    }

    public Guid ProductId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public int Available => OnHand - Reserved;

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static StockItem Track(Guid productId, string sku, string name, int onHand, DateTimeOffset now) =>
        new() { ProductId = productId, Sku = sku, Name = name, OnHand = Math.Max(0, onHand), UpdatedAt = now };

    /// <summary>Sets the count after a delivery or a stocktake. It cannot go below what is already promised.</summary>
    public void Count(int onHand, DateTimeOffset now)
    {
        if (onHand < 0 || onHand > 1_000_000)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Stock is between 0 and 1,000,000.");
        }

        if (onHand < Reserved)
        {
            throw new DomainException(ErrorCodes.InvalidState,
                $"{Reserved} are held for orders in progress; stock cannot be set below that.");
        }

        OnHand = onHand;
        UpdatedAt = now;
    }

    public bool CanReserve(int quantity) => quantity > 0 && quantity <= Available;

    public void Reserve(int quantity, DateTimeOffset now)
    {
        if (!CanReserve(quantity))
        {
            throw new DomainException(ErrorCodes.OutOfStock, $"Only {Available} of {Sku} are available.");
        }

        Reserved += quantity;
        UpdatedAt = now;
    }

    /// <summary>The order is final: the held units leave the warehouse.</summary>
    public void Commit(int quantity, DateTimeOffset now)
    {
        Reserved -= quantity;
        OnHand -= quantity;
        UpdatedAt = now;
    }

    /// <summary>The order fell through: the held units can be sold again.</summary>
    public void Release(int quantity, DateTimeOffset now)
    {
        Reserved -= quantity;
        UpdatedAt = now;
    }
}

public enum ReservationStatus
{
    Held,
    Committed,
    Released,
}

/// <summary>Units of one product held for one order. One row per order and product, so a repeat changes nothing.</summary>
public sealed class Reservation
{
    private Reservation()
    {
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Reservation Hold(Guid orderId, Guid productId, int quantity, DateTimeOffset now) =>
        new() { OrderId = orderId, ProductId = productId, Quantity = quantity, Status = ReservationStatus.Held, CreatedAt = now, UpdatedAt = now };

    public void Commit(DateTimeOffset now)
    {
        Status = ReservationStatus.Committed;
        UpdatedAt = now;
    }

    public void Release(DateTimeOffset now)
    {
        Status = ReservationStatus.Released;
        UpdatedAt = now;
    }
}
