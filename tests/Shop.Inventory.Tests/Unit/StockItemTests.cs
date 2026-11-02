using Shop.Inventory.Stock;
using Shop.ServiceDefaults.Errors;

namespace Shop.Inventory.Tests.Unit;

public sealed class StockItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static StockItem Item(int onHand) => StockItem.Track(Guid.NewGuid(), "SKU-1", "Ürün", onHand, Now);

    [Fact]
    public void Reserving_lowers_availability_not_stock()
    {
        var item = Item(5);
        item.Reserve(3, Now);

        Assert.Equal(5, item.OnHand);
        Assert.Equal(3, item.Reserved);
        Assert.Equal(2, item.Available);
        Assert.False(item.CanReserve(3));
        Assert.Equal(ErrorCodes.OutOfStock, Assert.Throws<DomainException>(() => item.Reserve(3, Now)).Code);
    }

    [Fact]
    public void Committing_takes_the_held_units_out_and_releasing_gives_them_back()
    {
        var item = Item(5);
        item.Reserve(3, Now);
        item.Commit(2, Now);
        item.Release(1, Now);

        Assert.Equal(3, item.OnHand);
        Assert.Equal(0, item.Reserved);
    }

    [Fact]
    public void Counting_cannot_go_below_what_is_held()
    {
        var item = Item(5);
        item.Reserve(4, Now);

        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => item.Count(3, Now)).Code);
        Assert.Throws<DomainException>(() => Item(1).Count(-1, Now));
        item.Count(10, Now);
        Assert.Equal(6, item.Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Nothing_or_less_cannot_be_reserved(int quantity)
    {
        Assert.False(Item(5).CanReserve(quantity));
    }
}
