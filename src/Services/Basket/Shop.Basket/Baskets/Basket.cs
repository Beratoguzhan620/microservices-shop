using Shop.ServiceDefaults.Errors;

namespace Shop.Basket.Baskets;

/// <summary>A line with the name and price the customer saw when they added it.</summary>
public sealed record BasketLine(Guid ProductId, string Sku, string Name, decimal UnitPrice, int Quantity)
{
    public decimal Total => UnitPrice * Quantity;
}

/// <summary>
/// A customer's basket. It lives in Redis and expires after a month of not being touched; it is a list of
/// intentions, not of reservations, so stock is only checked when the order is placed.
/// </summary>
public sealed record CustomerBasket(Guid CustomerId, IReadOnlyList<BasketLine> Lines, DateTimeOffset UpdatedAt)
{
    public const int MaxLines = 30;
    public const int MaxQuantity = 20;

    public decimal Total => Lines.Sum(l => l.Total);

    public int Count => Lines.Sum(l => l.Quantity);

    public static CustomerBasket Empty(Guid customerId, DateTimeOffset now) => new(customerId, [], now);

    /// <summary>Sets a line's quantity; 0 removes it. The price is the product's current one.</summary>
    public CustomerBasket Set(Guid productId, string sku, string name, decimal unitPrice, int quantity, DateTimeOffset now)
    {
        if (quantity is < 0 or > MaxQuantity)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A quantity is between 0 and {MaxQuantity}.");
        }

        var lines = Lines.Where(l => l.ProductId != productId).ToList();
        if (quantity > 0)
        {
            if (lines.Count >= MaxLines)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"A basket holds at most {MaxLines} different products.");
            }

            // Keep the line where it was, so the basket does not reorder itself.
            var index = Lines.ToList().FindIndex(l => l.ProductId == productId);
            var line = new BasketLine(productId, sku, name, unitPrice, quantity);
            if (index >= 0)
            {
                lines.Insert(index, line);
            }
            else
            {
                lines.Add(line);
            }
        }

        return this with { Lines = lines, UpdatedAt = now };
    }

    public CustomerBasket Remove(Guid productId, DateTimeOffset now) =>
        this with { Lines = Lines.Where(l => l.ProductId != productId).ToList(), UpdatedAt = now };

    /// <summary>
    /// Brings names and prices up to date and drops products that are no longer sold. The changes are listed, so the
    /// customer confirms the new total before anything is ordered.
    /// </summary>
    public (CustomerBasket Basket, IReadOnlyList<string> Changes) Reprice(IReadOnlyDictionary<Guid, (string Name, decimal Price, bool Sold)> current, DateTimeOffset now)
    {
        var changes = new List<string>();
        var lines = new List<BasketLine>();
        foreach (var line in Lines)
        {
            if (!current.TryGetValue(line.ProductId, out var product) || !product.Sold)
            {
                changes.Add($"{line.Name} is no longer sold and was removed.");
                continue;
            }

            if (product.Price != line.UnitPrice)
            {
                changes.Add($"{product.Name}: {line.UnitPrice:0.00} → {product.Price:0.00} TRY.");
            }

            lines.Add(line with { Name = product.Name, UnitPrice = product.Price });
        }

        return (changes.Count == 0 ? this : this with { Lines = lines, UpdatedAt = now }, changes);
    }
}
