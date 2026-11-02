using Shop.Identity.Users;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;
using Shop.ServiceDefaults.Logging;

namespace Shop.Identity.Tests.Unit;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_account_is_a_customer_with_a_normalized_email()
    {
        var user = User.Register("  Ayse.Yilmaz@Example.COM ", " Ayşe ", Now);

        Assert.Equal("ayse.yilmaz@example.com", user.Email);
        Assert.Equal("Ayşe", user.DisplayName);
        Assert.Equal(ShopRoles.Customer, user.Role);
    }

    [Theory]
    [InlineData("ayse")]
    [InlineData("ayse@localhost")]
    [InlineData("Ayşe <ayse@example.com>")]
    [InlineData("")]
    public void Invalid_emails_are_refused(string email)
    {
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => User.Register(email, "Ayşe", Now)).Code);
    }

    [Fact]
    public void Names_must_be_reasonable()
    {
        Assert.Throws<DomainException>(() => User.Register("a@example.com", "A", Now));
        Assert.Throws<DomainException>(() => User.Register("a@example.com", new string('a', 61), Now));
    }

    [Fact]
    public void An_account_can_be_made_admin()
    {
        var user = User.Register("a@example.com", "Ayşe", Now);
        user.MakeAdmin();
        Assert.Equal(ShopRoles.Admin, user.Role);
    }

    [Theory]
    [InlineData("ayse.yilmaz@example.com", "a***@example.com")]
    [InlineData("", "***")]
    [InlineData("noat", "***")]
    public void Emails_are_masked_in_logs(string email, string expected)
    {
        Assert.Equal(expected, Masking.Email(email));
    }

    [Theory]
    [InlineData("4242 4242 4242 4242", "**** 4242")]
    [InlineData("12", "***")]
    public void Card_numbers_are_masked_in_logs(string card, string expected)
    {
        Assert.Equal(expected, Masking.Card(card));
    }

    [Fact]
    public void Error_codes_map_to_statuses()
    {
        Assert.Equal(404, ErrorCodes.StatusFor(ErrorCodes.NotFound));
        Assert.Equal(409, ErrorCodes.StatusFor(ErrorCodes.OutOfStock));
        Assert.Equal(422, ErrorCodes.StatusFor("something_else"));
    }
}
