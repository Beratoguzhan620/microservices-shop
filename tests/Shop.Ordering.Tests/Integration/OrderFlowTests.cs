using System.Net;
using System.Net.Http.Json;
using Shop.Contracts.Inventory;
using Shop.Contracts.Ordering;
using Shop.Contracts.Payment;
using Shop.Ordering.Features;
using Shop.Ordering.Orders;
using Shop.Ordering.Saga;
using Shop.Testing;

namespace Shop.Ordering.Tests.Integration;

[Collection(OrderingCollection.Name)]
public sealed class OrderFlowTests(OrderingFactory factory)
{
    private const string Token = "tok_0123456789abcdef";
    private static readonly Guid Coffee = Guid.NewGuid();
    private static readonly Guid Kettle = Guid.NewGuid();

    private static BasketCheckedOut Checkout(Guid? customerId = null) => new(
        Guid.NewGuid(), customerId ?? Guid.NewGuid(), "ayse@example.com", "Ayşe Yılmaz",
        [new OrderLine(Coffee, "KHV-001", "Etiyopya 250 g", 389.90m, 2), new OrderLine(Kettle, "EKP-003", "Kettle", 2690m, 1)],
        new ShippingAddress("Ayşe Yılmaz", "Atatürk Cad. No: 24/5", "Samsun", "55060", "05551234567"),
        Token, DateTimeOffset.UtcNow);

    private async Task<BasketCheckedOut> AwaitingPaymentAsync(Guid? customerId = null)
    {
        var checkout = Checkout(customerId);
        await factory.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        await factory.Bus.Publish(new StockReserved(checkout.OrderId, DateTimeOffset.UtcNow));
        await factory.Log.WaitAsync<ProcessPayment>(m => m.OrderId == checkout.OrderId);
        return checkout;
    }

    private async Task<BasketCheckedOut> ConfirmedAsync(Guid? customerId = null)
    {
        var checkout = await AwaitingPaymentAsync(customerId);
        await factory.Bus.Publish(new PaymentSucceeded(checkout.OrderId, Guid.NewGuid(), 3469.80m, "4242", DateTimeOffset.UtcNow));
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.Confirmed);
        return checkout;
    }

    [Fact]
    public async Task A_checkout_becomes_an_order_that_asks_for_its_stock()
    {
        var checkout = Checkout();

        await factory.Bus.Publish(checkout);

        var reserve = await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        Assert.Equal(new[] { (Coffee, 2), (Kettle, 1) }, reserve.Lines.Select(l => (l.ProductId, l.Quantity)));
        var order = await factory.WaitForAsync(checkout.OrderId, OrderStatus.Submitted);
        Assert.Equal(3469.80m, order.Total);
        Assert.Equal("Samsun", order.ShippingAddress.City);
    }

    [Fact]
    public async Task Held_stock_asks_for_the_payment_and_a_payment_confirms_the_order()
    {
        var checkout = await AwaitingPaymentAsync();

        var charge = factory.Log.All<ProcessPayment>(m => m.OrderId == checkout.OrderId).Single();
        Assert.Equal((3469.80m, Token, checkout.CustomerId), (charge.Amount, charge.PaymentToken, charge.CustomerId));
        Assert.Equal(OrderStatus.AwaitingPayment, (await factory.WaitForAsync(checkout.OrderId, OrderStatus.AwaitingPayment)).Status);

        await factory.Bus.Publish(new PaymentSucceeded(checkout.OrderId, Guid.NewGuid(), 3469.80m, "4242", DateTimeOffset.UtcNow));

        await factory.Log.WaitAsync<OrderConfirmed>(m => m.OrderId == checkout.OrderId);
        var order = await factory.WaitForAsync(checkout.OrderId, OrderStatus.Confirmed);
        Assert.Equal("4242", order.CardLast4);

        // Stock stays held until the order ships.
        Assert.Equal(0, factory.Log.Count<CommitStock>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task Missing_stock_rejects_the_order_without_asking_for_money()
    {
        var checkout = Checkout();
        await factory.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);

        await factory.Bus.Publish(new StockReservationFailed(checkout.OrderId, "One product is not available in that quantity.", [Kettle], DateTimeOffset.UtcNow));

        var rejected = await factory.Log.WaitAsync<OrderRejected>(m => m.OrderId == checkout.OrderId);
        var order = await factory.WaitForAsync(checkout.OrderId, OrderStatus.Rejected);
        Assert.Equal(rejected.Reason, order.Reason);
        Assert.Equal(0, factory.Log.Count<ProcessPayment>(m => m.OrderId == checkout.OrderId));
        Assert.Equal(0, await factory.SagaCountAsync(checkout.OrderId));
    }

    [Fact]
    public async Task A_declined_payment_gives_the_stock_back()
    {
        var checkout = await AwaitingPaymentAsync();

        await factory.Bus.Publish(new PaymentFailed(checkout.OrderId, "The card was declined.", DateTimeOffset.UtcNow));

        var release = await factory.Log.WaitAsync<ReleaseStock>(m => m.OrderId == checkout.OrderId);
        Assert.Equal("The card was declined.", release.Reason);
        var order = await factory.WaitForAsync(checkout.OrderId, OrderStatus.Rejected);
        Assert.Equal("The card was declined.", order.Reason);
    }

    [Fact]
    public async Task An_order_that_waits_too_long_for_its_payment_gives_up_and_refunds_a_late_payment()
    {
        var checkout = await AwaitingPaymentAsync();
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.AwaitingPayment);
        Assert.Equal(0, await factory.SweepAsync());

        // The deadline is 5 seconds in this test host.
        factory.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.True(await factory.SweepAsync() >= 1);

        await factory.Log.WaitAsync<ReleaseStock>(m => m.OrderId == checkout.OrderId);
        var order = await factory.WaitForAsync(checkout.OrderId, OrderStatus.Rejected);
        Assert.Equal(OrderStateMachine.TimedOutReason, order.Reason);

        await factory.Bus.Publish(new PaymentSucceeded(checkout.OrderId, Guid.NewGuid(), 3469.80m, "4242", DateTimeOffset.UtcNow));

        await factory.Log.WaitAsync<RefundPayment>(m => m.OrderId == checkout.OrderId);
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.Rejected, o => o.Refunded);
        Assert.Equal(0, factory.Log.Count<OrderConfirmed>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task Shipping_takes_the_stock_out_and_ends_the_process()
    {
        var checkout = await ConfirmedAsync();
        using var admin = factory.Admin();

        var shipped = await admin.PostAsJsonAsync($"/api/ordering/admin/orders/{checkout.OrderId}/ship", new { trackingCode = "YK-123456" });

        Assert.Equal(HttpStatusCode.Accepted, shipped.StatusCode);
        await factory.Log.WaitAsync<CommitStock>(m => m.OrderId == checkout.OrderId);
        await factory.Log.WaitAsync<OrderShipped>(m => m.OrderId == checkout.OrderId);
        var order = await factory.WaitForAsync(checkout.OrderId, OrderStatus.Shipped);
        Assert.Equal("YK-123456", order.TrackingCode);
        await Messages.UntilAsync(() => factory.SagaCountAsync(checkout.OrderId).GetAwaiter().GetResult() == 0, "The saga did not end.");

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/ordering/admin/orders/{checkout.OrderId}/ship", new { trackingCode = "YK-123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await factory.Customer(checkout.CustomerId).PostAsJsonAsync($"/api/ordering/orders/{checkout.OrderId}/cancel", new { })).StatusCode);
    }

    [Fact]
    public async Task Cancelling_releases_the_stock_and_refunds_the_payment()
    {
        var customer = Guid.NewGuid();
        var checkout = await ConfirmedAsync(customer);
        using var client = factory.Customer(customer);

        var response = await client.PostAsJsonAsync($"/api/ordering/orders/{checkout.OrderId}/cancel", new { reason = "Yanlış ürün seçtim." });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await factory.Log.WaitAsync<ReleaseStock>(m => m.OrderId == checkout.OrderId);
        var refund = await factory.Log.WaitAsync<RefundPayment>(m => m.OrderId == checkout.OrderId);
        Assert.Equal("Yanlış ürün seçtim.", refund.Reason);
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.Cancelled);

        await factory.Bus.Publish(new PaymentRefunded(checkout.OrderId, 3469.80m, DateTimeOffset.UtcNow));
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.Cancelled, o => o.Refunded);
        Assert.Equal(0, factory.Log.Count<CommitStock>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task Unpaid_orders_cannot_ship_and_strangers_cannot_cancel()
    {
        var checkout = await AwaitingPaymentAsync();
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.AwaitingPayment);

        Assert.Equal(HttpStatusCode.Conflict, (await factory.Admin().PostAsJsonAsync($"/api/ordering/admin/orders/{checkout.OrderId}/ship", new { trackingCode = "YK-1" + "2345" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Customer(Guid.NewGuid()).PostAsJsonAsync($"/api/ordering/orders/{checkout.OrderId}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await factory.Admin().PostAsJsonAsync($"/api/ordering/admin/orders/{checkout.OrderId}/ship", new { trackingCode = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_checkout_delivered_twice_is_one_order()
    {
        var checkout = Checkout();

        await factory.Bus.Publish(checkout);
        await factory.Bus.Publish(checkout);
        await factory.Log.WaitAsync<ReserveStock>(m => m.OrderId == checkout.OrderId);
        await Task.Delay(1000);

        Assert.Equal(1, factory.Log.Count<OrderSubmitted>(m => m.OrderId == checkout.OrderId));
        Assert.Equal(1, factory.Log.Count<ReserveStock>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task A_repeated_payment_answer_confirms_once()
    {
        var checkout = await AwaitingPaymentAsync();
        var paid = new PaymentSucceeded(checkout.OrderId, Guid.NewGuid(), 3469.80m, "4242", DateTimeOffset.UtcNow);

        await factory.Bus.Publish(paid);
        await factory.Bus.Publish(paid);
        await factory.WaitForAsync(checkout.OrderId, OrderStatus.Confirmed);
        await Task.Delay(1000);

        Assert.Equal(1, factory.Log.Count<OrderConfirmed>(m => m.OrderId == checkout.OrderId));
    }

    [Fact]
    public async Task Customers_see_their_own_orders_and_nobody_else_s()
    {
        var customer = Guid.NewGuid();
        var checkout = await ConfirmedAsync(customer);
        using var mine = factory.Customer(customer);
        using var stranger = factory.Customer(Guid.NewGuid());

        var order = await mine.GetFromJsonAsync<OrderResponse>($"/api/ordering/orders/{checkout.OrderId}", Http.Json);
        var list = await mine.GetFromJsonAsync<List<OrderSummary>>("/api/ordering/orders", Http.Json);

        Assert.Equal(2, order!.Items.Count);
        Assert.Equal("4242", order.CardLast4);
        Assert.True(order.CanCancel);
        Assert.Equal(3, Assert.Single(list!).ItemCount);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/ordering/orders/{checkout.OrderId}")).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<List<OrderSummary>>("/api/ordering/orders", Http.Json))!);
    }

    [Fact]
    public async Task Admins_list_every_order_by_status()
    {
        var checkout = await ConfirmedAsync();
        using var admin = factory.Admin();

        var confirmed = await admin.GetFromJsonAsync<OrderPage>("/api/ordering/admin/orders?status=Confirmed&pageSize=100", Http.Json);

        Assert.Contains(confirmed!.Items, o => o.Id == checkout.OrderId);
        Assert.All(confirmed.Items, o => Assert.Equal(OrderStatus.Confirmed, o.Status));
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Customer(Guid.NewGuid()).GetAsync("/api/ordering/admin/orders")).StatusCode);
    }
}
