using System.Net;
using System.Net.Http.Json;
using Shop.Contracts.Payment;
using Shop.Payment.Features;
using Shop.Payment.Payments;
using Shop.Testing;

namespace Shop.Payment.Tests.Integration;

[Collection(PaymentCollection.Name)]
public sealed class PaymentTests(PaymentFactory factory)
{
    private static readonly int NextYear = DateTime.UtcNow.Year + 1;

    private async Task<string> TokenAsync(Guid customerId, string number = "4242 4242 4242 4242")
    {
        using var client = factory.Customer(customerId);
        var response = await client.PostAsJsonAsync("/api/payment/cards", new { number, expiryMonth = 12, expiryYear = NextYear, cvc = "123" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<CardTokenResponse>()).Token;
    }

    private async Task<Guid> ChargeAsync(Guid customerId, string token, decimal amount = 858m)
    {
        var orderId = Guid.NewGuid();
        await factory.Harness.Bus.Publish(new ProcessPayment(orderId, customerId, amount, token));
        return orderId;
    }

    [Fact]
    public async Task A_card_becomes_a_token_that_shows_only_the_last_digits()
    {
        using var client = factory.Customer(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/payment/cards", new { number = "5555 5555 5555 4444", expiryMonth = 1, expiryYear = NextYear, cvc = "987" });

        var card = await response.ReadAsync<CardTokenResponse>();
        Assert.StartsWith("tok_", card.Token, StringComparison.Ordinal);
        Assert.Equal(("Mastercard", "4444"), (card.Brand, card.Last4));
        Assert.DoesNotContain("5555555555554444", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("4242 4242 4242 4241", 12, "123")]
    [InlineData("4242 4242 4242 4242", 13, "123")]
    [InlineData("4242 4242 4242 4242", 12, "12")]
    public async Task Invalid_cards_are_refused(string number, int month, string cvc)
    {
        using var client = factory.Customer(Guid.NewGuid());
        var response = await client.PostAsJsonAsync("/api/payment/cards", new { number, expiryMonth = month, expiryYear = NextYear, cvc });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_approved_card_is_charged_once_even_if_asked_twice()
    {
        var customer = Guid.NewGuid();
        var orderId = await ChargeAsync(customer, await TokenAsync(customer));

        var paid = await factory.Log.WaitAsync<PaymentSucceeded>(m => m.OrderId == orderId);
        await factory.Harness.Bus.Publish(new ProcessPayment(orderId, customer, 858m, "tok_whatever"));
        await Messages.UntilAsync(() => factory.Log.Count<PaymentSucceeded>(m => m.OrderId == orderId) == 2, "The repeated request got no answer.");

        Assert.Equal(("4242", 858m), (paid.CardLast4, paid.Amount));
        var payment = Assert.Single(await factory.PaymentsAsync(orderId));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Theory]
    [InlineData("4000 0000 0000 0002", "declined")]
    [InlineData("4000 0000 0000 9995", "insufficient funds")]
    public async Task Test_cards_are_declined_with_a_reason(string number, string reason)
    {
        var customer = Guid.NewGuid();
        var orderId = await ChargeAsync(customer, await TokenAsync(customer, number));

        var failed = await factory.Log.WaitAsync<PaymentFailed>(m => m.OrderId == orderId);

        Assert.Contains(reason, failed.Reason, StringComparison.Ordinal);
        Assert.Equal(PaymentStatus.Declined, Assert.Single(await factory.PaymentsAsync(orderId)).Status);
    }

    [Fact]
    public async Task The_no_answer_card_never_answers()
    {
        var customer = Guid.NewGuid();
        var orderId = await ChargeAsync(customer, await TokenAsync(customer, "4000 0000 0000 0077"));

        await factory.Harness.ConsumedAsync<ProcessPayment>(m => m.OrderId == orderId);
        await Task.Delay(1500);

        Assert.Equal(0, factory.Log.Count<PaymentSucceeded>(m => m.OrderId == orderId) + factory.Log.Count<PaymentFailed>(m => m.OrderId == orderId));
        Assert.Empty(await factory.PaymentsAsync(orderId));
    }

    [Fact]
    public async Task A_token_pays_once_and_only_for_its_own_customer()
    {
        var customer = Guid.NewGuid();
        var token = await TokenAsync(customer);

        var stolen = await ChargeAsync(Guid.NewGuid(), token);
        var first = await ChargeAsync(customer, token);
        await factory.Log.WaitAsync<PaymentSucceeded>(m => m.OrderId == first);
        var second = await ChargeAsync(customer, token);
        var unknown = await ChargeAsync(customer, "tok_doesnotexist");

        Assert.Contains("someone else", (await factory.Log.WaitAsync<PaymentFailed>(m => m.OrderId == stolen)).Reason, StringComparison.Ordinal);
        Assert.Contains("already used", (await factory.Log.WaitAsync<PaymentFailed>(m => m.OrderId == second)).Reason, StringComparison.Ordinal);
        Assert.Contains("unknown", (await factory.Log.WaitAsync<PaymentFailed>(m => m.OrderId == unknown)).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refund_happens_once_and_only_for_a_payment_that_went_through()
    {
        var customer = Guid.NewGuid();
        var orderId = await ChargeAsync(customer, await TokenAsync(customer));
        await factory.Log.WaitAsync<PaymentSucceeded>(m => m.OrderId == orderId);

        await factory.Harness.Bus.Publish(new RefundPayment(orderId, "Cancelled by the customer."));
        await factory.Harness.Bus.Publish(new RefundPayment(orderId, "Cancelled by the customer."));
        await factory.Harness.Bus.Publish(new RefundPayment(Guid.NewGuid(), "Never charged."));
        await factory.Log.WaitAsync<PaymentRefunded>(m => m.OrderId == orderId);
        await Task.Delay(1000);

        Assert.Equal(1, factory.Log.Count<PaymentRefunded>(m => m.OrderId == orderId));
        Assert.Equal(PaymentStatus.Refunded, Assert.Single(await factory.PaymentsAsync(orderId)).Status);

        using var admin = factory.Admin();
        var view = await admin.GetFromJsonAsync<PaymentResponse>($"/api/payment/payments/{orderId}", Http.Json);
        Assert.Equal(PaymentStatus.Refunded, view!.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Customer(customer).GetAsync($"/api/payment/payments/{orderId}")).StatusCode);
    }
}
