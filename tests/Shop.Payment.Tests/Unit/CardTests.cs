using Shop.Payment.Payments;
using Shop.ServiceDefaults.Errors;

namespace Shop.Payment.Tests.Unit;

public sealed class CardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("4242424242424242", true)]
    [InlineData("4242424242424241", false)]
    [InlineData("5555555555554444", true)]
    [InlineData("4000000000000077", true)]
    public void Card_numbers_pass_the_Luhn_check(string number, bool valid)
    {
        Assert.Equal(valid, CardToken.Luhn(number));
    }

    [Theory]
    [InlineData("4242424242424242", CardBehaviour.Approve)]
    [InlineData("4000000000000002", CardBehaviour.Decline)]
    [InlineData("4000000000009995", CardBehaviour.InsufficientFunds)]
    [InlineData("4000000000000077", CardBehaviour.NoAnswer)]
    [InlineData("4111111111111111", CardBehaviour.Approve)]
    public void Test_cards_choose_how_the_network_behaves(string number, CardBehaviour behaviour)
    {
        Assert.Equal(behaviour, CardToken.Create(Guid.NewGuid(), number, 12, 2030, "123", Now).Behaviour);
    }

    [Fact]
    public void A_card_that_expired_last_month_is_refused_and_this_month_s_is_fine()
    {
        Assert.Throws<DomainException>(() => CardToken.Create(Guid.NewGuid(), "4242424242424242", 10, 2026, "123", Now));
        Assert.Equal("4242", CardToken.Create(Guid.NewGuid(), "4242 4242 4242 4242", 11, 2026, "123", Now).Last4);
    }

    [Fact]
    public void A_token_is_for_one_customer_one_use_and_half_an_hour()
    {
        var customer = Guid.NewGuid();
        var token = CardToken.Create(customer, "4242424242424242", 12, 2030, "123", Now);

        Assert.Null(token.Problem(customer, Now.AddMinutes(29)));
        Assert.NotNull(token.Problem(Guid.NewGuid(), Now));
        Assert.NotNull(token.Problem(customer, Now.AddMinutes(30)));
        token.Use(Now);
        Assert.NotNull(token.Problem(customer, Now.AddMinutes(1)));
    }

    [Fact]
    public void Only_a_successful_payment_can_be_refunded_and_only_once()
    {
        var paid = Payments.Payment.Charge(Guid.NewGuid(), Guid.NewGuid(), 100m, "4242", PaymentStatus.Succeeded, null, Now);
        var declined = Payments.Payment.Charge(Guid.NewGuid(), Guid.NewGuid(), 100m, "0002", PaymentStatus.Declined, "Declined", Now);

        Assert.True(paid.Refund("Cancelled", Now));
        Assert.False(paid.Refund("Cancelled", Now));
        Assert.False(declined.Refund("Cancelled", Now));
    }
}
