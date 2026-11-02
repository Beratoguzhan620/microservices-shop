using Shop.Basket.Baskets;
using Shop.ServiceDefaults.Errors;

namespace Shop.Basket.Tests.Unit;

public sealed class CustomerBasketTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static CustomerBasket Basket() =>
        CustomerBasket.Empty(Guid.NewGuid(), Now).Set(A, "A-1", "Kahve", 100m, 2, Now).Set(B, "B-1", "Filtre", 50m, 1, Now);

    [Fact]
    public void Totals_add_up_and_lines_keep_their_place()
    {
        var basket = Basket().Set(A, "A-1", "Kahve", 100m, 5, Now);

        Assert.Equal(new[] { A, B }, basket.Lines.Select(l => l.ProductId));
        Assert.Equal(550m, basket.Total);
        Assert.Equal(6, basket.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Quantities_stay_reasonable(int quantity)
    {
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Basket().Set(A, "A-1", "Kahve", 100m, quantity, Now)).Code);
    }

    [Fact]
    public void A_basket_holds_at_most_thirty_products()
    {
        var basket = CustomerBasket.Empty(Guid.NewGuid(), Now);
        for (var i = 0; i < CustomerBasket.MaxLines; i++)
        {
            basket = basket.Set(Guid.NewGuid(), $"S-{i}", "Ürün", 1m, 1, Now);
        }

        Assert.Throws<DomainException>(() => basket.Set(Guid.NewGuid(), "S-X", "Ürün", 1m, 1, Now));
    }

    [Fact]
    public void Repricing_lists_every_change_and_drops_what_is_no_longer_sold()
    {
        var (basket, changes) = Basket().Reprice(new Dictionary<Guid, (string, decimal, bool)>
        {
            [A] = ("Kahve Yeni", 120m, true),
        }, Now);

        Assert.Equal(2, changes.Count);
        var line = Assert.Single(basket.Lines);
        Assert.Equal(("Kahve Yeni", 120m), (line.Name, line.UnitPrice));
        Assert.Equal(240m, basket.Total);
    }

    [Fact]
    public void Repricing_an_unchanged_basket_changes_nothing()
    {
        var original = Basket();
        var (basket, changes) = original.Reprice(new Dictionary<Guid, (string, decimal, bool)>
        {
            [A] = ("Kahve", 100m, true),
            [B] = ("Filtre", 50m, true),
        }, Now.AddDays(1));

        Assert.Empty(changes);
        Assert.Same(original, basket);
    }
}
