using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shop.Basket.Features;
using Shop.Contracts.Ordering;
using Shop.Testing;

namespace Shop.Basket.Tests.Integration;

[Collection(BasketCollection.Name)]
public sealed class BasketTests(BasketFactory factory)
{
    private static readonly object Address = new
    {
        fullName = "Ayşe Yılmaz", line1 = "Atatürk Cad. No: 24/5", city = "Samsun", postalCode = "55060", phone = "0555 123 45 67",
    };

    private static async Task<BasketResponse> SetAsync(HttpClient client, Guid productId, int quantity)
    {
        var response = await client.PutAsJsonAsync($"/api/basket/items/{productId}", new { quantity });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<BasketResponse>();
    }

    [Fact]
    public async Task Products_go_in_with_the_catalog_s_name_and_price()
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        var filter = factory.Catalog.Add("Kâğıt Filtre", 129.90m);
        using var customer = factory.Customer();

        await SetAsync(customer, coffee, 2);
        await SetAsync(customer, filter, 1);
        var basket = await SetAsync(customer, coffee, 3);

        Assert.Equal(new[] { "Kenya AA 250 g", "Kâğıt Filtre" }, basket.Lines.Select(l => l.Name));
        Assert.Equal(4, basket.Count);
        Assert.Equal(1416.90m, basket.Total);

        var removed = await SetAsync(customer, coffee, 0);
        Assert.Equal(129.90m, Assert.Single(removed.Lines).Total);
    }

    [Fact]
    public async Task Each_customer_has_their_own_basket()
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        using var ayse = factory.Customer();
        using var mehmet = factory.Customer();

        await SetAsync(ayse, coffee, 1);

        Assert.Empty((await mehmet.GetFromJsonAsync<BasketResponse>("/api/basket", Http.Json))!.Lines);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/basket")).StatusCode);
    }

    [Fact]
    public async Task Unsold_products_and_silly_quantities_are_refused()
    {
        var archived = factory.Catalog.Add("Eski Ürün", 10m, active: false);
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        using var customer = factory.Customer();

        Assert.Equal(HttpStatusCode.NotFound, (await customer.PutAsJsonAsync($"/api/basket/items/{archived}", new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PutAsJsonAsync($"/api/basket/items/{Guid.NewGuid()}", new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PutAsJsonAsync($"/api/basket/items/{coffee}", new { quantity = 21 })).StatusCode);
    }

    [Fact]
    public async Task Checking_out_hands_the_order_over_and_empties_the_basket()
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        var customerId = Guid.NewGuid();
        using var customer = factory.Customer(customerId);
        var basket = await SetAsync(customer, coffee, 2);

        var response = await customer.PostAsJsonAsync("/api/basket/checkout", new { address = Address, expectedTotal = basket.Total });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var orderId = (await response.ReadAsync<CheckoutResponse>()).OrderId;
        Assert.Equal($"/api/ordering/orders/{orderId}", response.Headers.Location!.ToString());
        var checkout = await factory.Harness.PublishedAsync<BasketCheckedOut>(m => m.OrderId == orderId);
        Assert.Equal(customerId, checkout.CustomerId);
        Assert.Equal(858m, checkout.Lines.Sum(l => l.Total));
        Assert.Equal("55060", checkout.Address.PostalCode);
        Assert.Empty((await customer.GetFromJsonAsync<BasketResponse>("/api/basket", Http.Json))!.Lines);
    }

    [Fact]
    public async Task A_price_change_stops_the_checkout_and_shows_the_new_basket()
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        using var customer = factory.Customer();
        var basket = await SetAsync(customer, coffee, 2);
        factory.Catalog.Products[coffee] = factory.Catalog.Products[coffee] with { Price = 459m };

        var response = await customer.PostAsJsonAsync("/api/basket/checkout", new { address = Address, expectedTotal = basket.Total });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("basket_changed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(918m, body.RootElement.GetProperty("basket").GetProperty("total").GetDecimal());
        Assert.Contains("429.00 → 459.00", body.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(918m, (await customer.GetFromJsonAsync<BasketResponse>("/api/basket", Http.Json))!.Total);

        var confirmed = await customer.PostAsJsonAsync("/api/basket/checkout", new { address = Address, expectedTotal = 918m });
        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
    }

    [Fact]
    public async Task Two_checkouts_at_the_same_moment_place_one_order()
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        using var customer = factory.Customer();
        var basket = await SetAsync(customer, coffee, 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            customer.PostAsJsonAsync("/api/basket/checkout", new { address = Address, expectedTotal = basket.Total })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Accepted));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Accepted), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    [Fact]
    public async Task When_the_catalog_is_down_nothing_is_ordered_and_the_basket_is_kept()
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        using var customer = factory.Customer();
        var basket = await SetAsync(customer, coffee, 1);
        factory.Catalog.Down = true;
        try
        {
            var response = await customer.PostAsJsonAsync("/api/basket/checkout", new { address = Address, expectedTotal = basket.Total });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("service_unavailable", await response.ProblemCodeAsync());
        }
        finally
        {
            factory.Catalog.Down = false;
        }

        Assert.Single((await customer.GetFromJsonAsync<BasketResponse>("/api/basket", Http.Json))!.Lines);
    }

    [Theory]
    [InlineData("", "Atatürk Cad. 1", "Samsun", "55060", "05551234567")]
    [InlineData("Ayşe Yılmaz", "Atatürk Cad. 1", "Samsun", "5506", "05551234567")]
    [InlineData("Ayşe Yılmaz", "Atatürk Cad. 1", "Samsun", "55060", "123")]
    public async Task Incomplete_addresses_are_refused_before_anything_happens(string name, string line1, string city, string postal, string phone)
    {
        var coffee = factory.Catalog.Add("Kenya AA 250 g", 429m);
        using var customer = factory.Customer();
        var basket = await SetAsync(customer, coffee, 1);

        var response = await customer.PostAsJsonAsync("/api/basket/checkout", new
        {
            address = new { fullName = name, line1, city, postalCode = postal, phone },
            expectedTotal = basket.Total,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await customer.GetFromJsonAsync<BasketResponse>("/api/basket", Http.Json))!.Lines);
    }
}
