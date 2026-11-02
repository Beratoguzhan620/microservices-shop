using System.Net;
using System.Net.Http.Json;
using Shop.Contracts.Inventory;
using Shop.Contracts.Ordering;
using Shop.Ordering.Features;
using Shop.Ordering.Orders;
using Shop.Testing;

namespace Shop.Ordering.Tests.Integration;

[Collection(OrderingCollection.Name)]
public sealed class OrderFlowTests(OrderingFactory factory)
{
    private static readonly Guid Coffee = Guid.NewGuid();
    private static readonly Guid Kettle = Guid.NewGuid();

    private static BasketCheckedOut Checkout(Guid? customerId = null) => new(
        Guid.NewGuid(), customerId ?? Guid.NewGuid(), "ayse@example.com", "Ayşe Yılmaz",
        [new OrderLine(Coffee, "KHV-001", "Etiyopya 250 g", 389.90m, 2), new OrderLine(Kettle, "EKP-003", "Kettle", 2690m, 1)],
        new ShippingAddress("Ayşe Yılmaz", "Atatürk Cad. No: 24/5", "Samsun", "55060", "05551234567"),
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_checkout_becomes_an_order_that_asks_for_its_stock()
    {
        var checkout = Checkout();

        await factory.Harness.Bus.Publish(checkout);

        var reserve = await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        Assert.Equal(new[] { (Coffee, 2), (Kettle, 1) }, reserve.Lines.Select(l => (l.ProductId, l.Quantity)));
        var order = await factory.OrderAsync(checkout.OrderId);
        Assert.Equal(OrderStatus.Submitted, order!.Status);
        Assert.Equal(3469.80m, order.Total);
        Assert.Equal("Samsun", order.ShippingAddress.City);
    }

    [Fact]
    public async Task Held_stock_confirms_the_order_and_commits_the_stock()
    {
        var checkout = Checkout();
        await factory.Harness.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);

        await factory.Harness.Bus.Publish(new StockReserved(checkout.OrderId, DateTimeOffset.UtcNow));

        await factory.Log.WaitAsync<OrderConfirmed>(m => m.OrderId == checkout.OrderId);
        await factory.Log.WaitAsync<CommitStock>(m => m.OrderId == checkout.OrderId);
        Assert.Equal(OrderStatus.Confirmed, (await factory.OrderAsync(checkout.OrderId))!.Status);
        Assert.Equal(0, await factory.SagaCountAsync(checkout.OrderId));
    }

    [Fact]
    public async Task Missing_stock_rejects_the_order_with_the_reason()
    {
        var checkout = Checkout();
        await factory.Harness.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);

        await factory.Harness.Bus.Publish(new StockReservationFailed(checkout.OrderId, "One product is not available in that quantity.", [Kettle], DateTimeOffset.UtcNow));

        var rejected = await factory.Log.WaitAsync<OrderRejected>(m => m.OrderId == checkout.OrderId);
        Assert.Contains("not available", rejected.Reason, StringComparison.Ordinal);
        var order = await factory.OrderAsync(checkout.OrderId);
        Assert.Equal(OrderStatus.Rejected, order!.Status);
        Assert.Equal(rejected.Reason, order.Reason);
        Assert.Equal(0, factory.Log.Count<CommitStock>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task A_checkout_delivered_twice_is_one_order()
    {
        var checkout = Checkout();

        await factory.Harness.Bus.Publish(checkout);
        await factory.Harness.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        await Task.Delay(1000);

        Assert.Equal(1, factory.Log.Count<OrderSubmitted>(m => m.OrderId == checkout.OrderId));
        Assert.Equal(1, factory.Log.Count<ReserveStock>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task Customers_see_their_own_orders_and_nobody_else_s()
    {
        var customer = Guid.NewGuid();
        var checkout = Checkout(customer);
        await factory.Harness.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        using var mine = factory.Customer(customer);
        using var stranger = factory.Customer(Guid.NewGuid());

        var order = await mine.GetFromJsonAsync<OrderResponse>($"/api/ordering/orders/{checkout.OrderId}", Http.Json);
        var list = await mine.GetFromJsonAsync<List<OrderSummary>>("/api/ordering/orders", Http.Json);

        Assert.Equal(2, order!.Items.Count);
        Assert.Equal(3, Assert.Single(list!).ItemCount);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/ordering/orders/{checkout.OrderId}")).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<OrderSummary>>("/api/ordering/orders", Http.Json))!);
    }

    [Fact]
    public async Task Admins_list_every_order_by_status()
    {
        var checkout = Checkout();
        await factory.Harness.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        using var admin = factory.Admin();

        var submitted = await admin.GetFromJsonAsync<OrderPage>("/api/ordering/admin/orders?status=Submitted&pageSize=100", Http.Json);

        Assert.Contains(submitted!.Items, o => o.Id == checkout.OrderId);
        Assert.All(submitted.Items, o => Assert.Equal(OrderStatus.Submitted, o.Status));
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Customer(Guid.NewGuid()).GetAsync("/api/ordering/admin/orders")).StatusCode);
    }
}
