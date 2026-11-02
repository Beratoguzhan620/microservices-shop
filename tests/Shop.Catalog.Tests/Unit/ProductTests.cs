using Shop.Catalog.Products;
using Shop.ServiceDefaults.Errors;

namespace Shop.Catalog.Tests.Unit;

public sealed class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Filtre Kahve Öğütücü", "filtre-kahve-ogutucu")]
    [InlineData("  Çay  &  Şeker!! ", "cay-seker")]
    [InlineData("Café Crème", "cafe-creme")]
    [InlineData("İĞNE", "igne")]
    public void Slugs_are_plain_ascii(string name, string expected)
    {
        Assert.Equal(expected, Slugs.From(name));
    }

    [Fact]
    public void A_new_product_is_on_sale_with_a_normalized_sku()
    {
        var product = Product.Create(" khv-010 ", " Kenya AA ", " Meyveli ", 429m, Guid.NewGuid(), Now);

        Assert.Equal("KHV-010", product.Sku);
        Assert.Equal("Kenya AA", product.Name);
        Assert.Equal("kenya-aa", product.Slug);
        Assert.True(product.IsActive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.001)]
    [InlineData(1_000_000.01)]
    public void Prices_must_be_positive_with_two_decimals(decimal price)
    {
        Assert.Equal(ErrorCodes.ValidationFailed, Assert.Throws<DomainException>(() => Product.Create("SKU-1", "Ürün", "", price, Guid.NewGuid(), Now)).Code);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("-AB")]
    [InlineData("AB CD")]
    [InlineData("ÜRÜN-1")]
    public void Skus_are_letters_digits_and_dashes(string sku)
    {
        Assert.Throws<DomainException>(() => Product.CheckSku(sku));
    }

    [Fact]
    public void Updating_reports_a_price_change_and_renames_the_slug()
    {
        var product = Product.Create("SKU-1", "Eski Ad", "", 100m, Guid.NewGuid(), Now);

        Assert.False(product.Update("Eski Ad", "Açıklama", 100m, product.CategoryId, Now));
        Assert.True(product.Update("Yeni Ad", "Açıklama", 120m, product.CategoryId, Now.AddMinutes(1)));
        Assert.Equal("yeni-ad", product.Slug);
        Assert.Equal(Now.AddMinutes(1), product.UpdatedAt);
    }

    [Fact]
    public void Archiving_and_restoring_toggle_sale()
    {
        var product = Product.Create("SKU-1", "Ürün", "", 100m, Guid.NewGuid(), Now);
        product.Archive(Now);
        Assert.False(product.IsActive);
        product.Restore(Now);
        Assert.True(product.IsActive);
    }
}
