using System.Net;
using System.Net.Http.Json;
using Shop.Contracts.Catalog;
using Shop.Contracts.Inventory;
using Shop.Inventory.Features;
using Shop.Inventory.Stock;
using Shop.Testing;

namespace Shop.Inventory.Tests.Integration;

[Collection(InventoryCollection.Name)]
public sealed class StockTests(InventoryFactory factory)
{
    [Fact]
    public async Task A_new_product_is_tracked_with_the_opening_stock_once()
    {
        var product = new ProductCreated(Guid.NewGuid(), "KHV-777", "Kenya AA", 429m, DateTimeOffset.UtcNow);

        await factory.Harness.Bus.Publish(product);
        await factory.Harness.Bus.Publish(product);
        await factory.Harness.ConsumedAsync<ProductCreated>(m => m.ProductId == product.ProductId);

        var item = await WaitForAsync(() => factory.ItemAsync(product.ProductId));
        Assert.Equal(InventoryFactory.OpeningStock, item.OnHand);
        Assert.Equal("KHV-777", item.Sku);
    }

    [Fact]
    public async Task Stock_is_held_for_every_line_or_for_none()
    {
        var plenty = await factory.StockAsync(10);
        var scarce = await factory.StockAsync(1);
        var orderId = Guid.NewGuid();

        await factory.Harness.Bus.Publish(new ReserveStock(orderId, [new StockLine(plenty, 3), new StockLine(scarce, 2)]));

        var failed = await factory.Log.WaitAsync<StockReservationFailed>(m => m.OrderId == orderId);
        Assert.Equal(new[] { scarce }, failed.UnavailableProductIds);
        Assert.Equal(0, (await factory.ItemAsync(plenty)).Reserved);
        Assert.Empty(await factory.ReservationsAsync(orderId));
    }

    [Fact]
    public async Task Held_stock_is_committed_once_and_a_repeated_request_holds_nothing_twice()
    {
        var product = await factory.StockAsync(10);
        var orderId = Guid.NewGuid();

        await factory.Harness.Bus.Publish(new ReserveStock(orderId, [new StockLine(product, 2), new StockLine(product, 1)]));
        await factory.Log.WaitAsync<StockReserved>(m => m.OrderId == orderId);
        await factory.Harness.Bus.Publish(new ReserveStock(orderId, [new StockLine(product, 3)]));
        await WaitUntilAsync(() => factory.Log.Count<StockReserved>(m => m.OrderId == orderId) == 2);

        var held = await factory.ItemAsync(product);
        Assert.Equal(3, held.Reserved);
        Assert.Equal(7, held.Available);

        await factory.Harness.Bus.Publish(new CommitStock(orderId));
        await factory.Harness.Bus.Publish(new CommitStock(orderId));
        await WaitUntilAsync(async () => (await factory.ItemAsync(product)).OnHand == 7);
        await Task.Delay(300);

        var committed = await factory.ItemAsync(product);
        Assert.Equal(7, committed.OnHand);
        Assert.Equal(0, committed.Reserved);
        Assert.Equal(ReservationStatus.Committed, Assert.Single(await factory.ReservationsAsync(orderId)).Status);
    }

    [Fact]
    public async Task Released_stock_can_be_sold_again()
    {
        var product = await factory.StockAsync(2);
        var orderId = Guid.NewGuid();
        await factory.Harness.Bus.Publish(new ReserveStock(orderId, [new StockLine(product, 2)]));
        await factory.Log.WaitAsync<StockReserved>(m => m.OrderId == orderId);

        await factory.Harness.Bus.Publish(new ReleaseStock(orderId, "Payment declined."));
        await factory.Log.WaitAsync<StockReleased>(m => m.OrderId == orderId);

        var item = await factory.ItemAsync(product);
        Assert.Equal(2, item.OnHand);
        Assert.Equal(0, item.Reserved);
        var next = Guid.NewGuid();
        await factory.Harness.Bus.Publish(new ReserveStock(next, [new StockLine(product, 2)]));
        await factory.Log.WaitAsync<StockReserved>(m => m.OrderId == next);
    }

    [Fact]
    public async Task Twenty_orders_for_the_last_five_units_sell_exactly_five()
    {
        var product = await factory.StockAsync(5);
        var orders = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(orders.Select(id => factory.Harness.Bus.Publish(new ReserveStock(id, [new StockLine(product, 1)]))));
        await WaitUntilAsync(() =>
            orders.Count(id => factory.Log.Count<StockReserved>(m => m.OrderId == id) + factory.Log.Count<StockReservationFailed>(m => m.OrderId == id) > 0) == 20,
            seconds: 60);

        Assert.Equal(5, orders.Count(id => factory.Log.Count<StockReserved>(m => m.OrderId == id) > 0));
        var item = await factory.ItemAsync(product);
        Assert.Equal(5, item.Reserved);
        Assert.Equal(0, item.Available);
    }

    [Fact]
    public async Task Anyone_can_see_availability_without_held_stock()
    {
        var product = await factory.StockAsync(4);
        var orderId = Guid.NewGuid();
        await factory.Harness.Bus.Publish(new ReserveStock(orderId, [new StockLine(product, 3)]));
        await factory.Log.WaitAsync<StockReserved>(m => m.OrderId == orderId);
        using var anonymous = factory.CreateClient();

        var availability = await anonymous.GetFromJsonAsync<List<AvailabilityResponse>>($"/api/inventory/availability?ids={product}&ids={Guid.NewGuid()}", Http.Json);

        Assert.Equal(1, Assert.Single(availability!).Available);
    }

    [Fact]
    public async Task Admins_count_stock_but_never_below_what_is_held_or_over_a_newer_count()
    {
        var product = await factory.StockAsync(5);
        var orderId = Guid.NewGuid();
        await factory.Harness.Bus.Publish(new ReserveStock(orderId, [new StockLine(product, 3)]));
        await factory.Log.WaitAsync<StockReserved>(m => m.OrderId == orderId);
        using var admin = factory.Admin();
        var item = (await admin.GetFromJsonAsync<List<StockResponse>>("/api/inventory/stock", Http.Json))!.Single(i => i.ProductId == product);

        var belowHeld = await admin.PutAsJsonAsync($"/api/inventory/stock/{product}", new { onHand = 2, version = item.Version });
        var ok = await admin.PutAsJsonAsync($"/api/inventory/stock/{product}", new { onHand = 40, version = item.Version });
        var stale = await admin.PutAsJsonAsync($"/api/inventory/stock/{product}", new { onHand = 50, version = item.Version });

        Assert.Equal(HttpStatusCode.Conflict, belowHeld.StatusCode);
        Assert.Equal(37, (await ok.ReadAsync<StockResponse>()).Available);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().WithToken(TestKeys.Token(Guid.NewGuid())).GetAsync("/api/inventory/stock")).StatusCode);
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T>> read)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                return await read();
            }
            catch (InvalidOperationException) when (i < 50)
            {
                await Task.Delay(100);
            }
        }
    }

    private static Task WaitUntilAsync(Func<bool> condition, int seconds = 20) => WaitUntilAsync(() => Task.FromResult(condition()), seconds);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < until, "The condition was not met in time.");
            await Task.Delay(100);
        }
    }
}
