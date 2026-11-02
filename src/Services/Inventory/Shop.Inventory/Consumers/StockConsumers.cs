using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts.Catalog;
using Shop.Contracts.Inventory;
using Shop.Inventory.Data;
using Shop.Inventory.Stock;

namespace Shop.Inventory.Consumers;

/// <summary>Starts tracking a new product, with the configured opening stock (0 unless the demo sets one).</summary>
public sealed class ProductCreatedConsumer(InventoryDbContext db, IConfiguration configuration, TimeProvider clock) : IConsumer<ProductCreated>
{
    public async Task Consume(ConsumeContext<ProductCreated> context)
    {
        var product = context.Message;
        if (await db.StockItems.AnyAsync(i => i.ProductId == product.ProductId, context.CancellationToken))
        {
            return;
        }

        db.StockItems.Add(StockItem.Track(product.ProductId, product.Sku, product.Name,
            configuration.GetValue("Inventory:OpeningStock", 0), clock.GetUtcNow()));
    }
}

/// <summary>
/// Holds stock for an order, all lines or none. The rows are locked in a fixed order (by product id), so two
/// orders for the same products queue up instead of deadlocking, and each sees what the other left.
/// </summary>
public sealed partial class ReserveStockConsumer(InventoryDbContext db, TimeProvider clock, ILogger<ReserveStockConsumer> logger) : IConsumer<ReserveStock>
{
    public async Task Consume(ConsumeContext<ReserveStock> context)
    {
        var message = context.Message;
        var ct = context.CancellationToken;
        var now = clock.GetUtcNow();

        // Asked again for the same order (a retry of the whole process): answer as before, hold nothing twice.
        var existing = await db.Reservations.Where(r => r.OrderId == message.OrderId).ToListAsync(ct);
        if (existing.Count > 0)
        {
            if (existing.All(r => r.Status != ReservationStatus.Released))
            {
                await context.Publish(new StockReserved(message.OrderId, now), ct);
            }
            else
            {
                await context.Publish(new StockReservationFailed(message.OrderId, "The order's stock was already released.", [], now), ct);
            }

            return;
        }

        var wanted = message.Lines.GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var ids = wanted.Keys.Order().ToArray();
        var items = await db.StockItems
            .FromSql($"SELECT *, xmin FROM stock_items WHERE product_id = ANY({ids}) ORDER BY product_id FOR UPDATE")
            .ToDictionaryAsync(i => i.ProductId, ct);

        var unavailable = ids.Where(id => !items.TryGetValue(id, out var item) || !item.CanReserve(wanted[id])).ToList();
        if (unavailable.Count > 0)
        {
            LogRefused(logger, message.OrderId, unavailable.Count);
            await context.Publish(new StockReservationFailed(message.OrderId,
                unavailable.Count == 1 ? "One product is not available in that quantity." : $"{unavailable.Count} products are not available in those quantities.",
                unavailable, now), ct);
            return;
        }

        foreach (var id in ids)
        {
            items[id].Reserve(wanted[id], now);
            db.Reservations.Add(Reservation.Hold(message.OrderId, id, wanted[id], now));
        }

        await context.Publish(new StockReserved(message.OrderId, now), ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock refused for order {OrderId}: {Count} product(s) short")]
    private static partial void LogRefused(ILogger logger, Guid orderId, int count);
}

/// <summary>The order went through; held stock leaves the warehouse. Repeats find nothing held and do nothing.</summary>
public sealed class CommitStockConsumer(InventoryDbContext db, TimeProvider clock) : IConsumer<CommitStock>
{
    public async Task Consume(ConsumeContext<CommitStock> context)
    {
        var now = clock.GetUtcNow();
        foreach (var (reservation, item) in await HeldAsync(db, context.Message.OrderId, context.CancellationToken))
        {
            reservation.Commit(now);
            item.Commit(reservation.Quantity, now);
        }
    }

    internal static async Task<List<(Reservation Reservation, StockItem Item)>> HeldAsync(InventoryDbContext db, Guid orderId, CancellationToken ct)
    {
        var held = await db.Reservations.Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Held).ToListAsync(ct);
        if (held.Count == 0)
        {
            return [];
        }

        var ids = held.Select(r => r.ProductId).Order().ToArray();
        var items = await db.StockItems
            .FromSql($"SELECT *, xmin FROM stock_items WHERE product_id = ANY({ids}) ORDER BY product_id FOR UPDATE")
            .ToDictionaryAsync(i => i.ProductId, ct);
        return held.Select(r => (r, items[r.ProductId])).ToList();
    }
}

/// <summary>The order did not go through; held stock is available again. Repeats do nothing.</summary>
public sealed class ReleaseStockConsumer(InventoryDbContext db, TimeProvider clock) : IConsumer<ReleaseStock>
{
    public async Task Consume(ConsumeContext<ReleaseStock> context)
    {
        var now = clock.GetUtcNow();
        foreach (var (reservation, item) in await CommitStockConsumer.HeldAsync(db, context.Message.OrderId, context.CancellationToken))
        {
            reservation.Release(now);
            item.Release(reservation.Quantity, now);
        }

        await context.Publish(new StockReleased(context.Message.OrderId, now), context.CancellationToken);
    }
}
