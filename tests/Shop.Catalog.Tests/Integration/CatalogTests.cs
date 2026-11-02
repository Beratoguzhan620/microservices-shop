using System.Net;
using System.Net.Http.Json;
using Shop.Catalog.Features;
using Shop.Testing;

namespace Shop.Catalog.Tests.Integration;

[Collection(CatalogCollection.Name)]
public sealed class BrowsingTests(CatalogFactory factory)
{
    [Fact]
    public async Task Anyone_can_list_categories_with_their_product_counts()
    {
        using var client = factory.CreateClient();

        var categories = await client.GetFromJsonAsync<List<CategoryResponse>>("/api/catalog/categories", Http.Json);

        Assert.Contains(categories!, c => c is { Slug: "kahve" } && c.ProductCount >= 5);
        Assert.Contains(categories!, c => c is { Slug: "cay", Name: "Çay" });
    }

    [Fact]
    public async Task Search_finds_by_text_in_name_or_description_and_by_sku()
    {
        using var client = factory.CreateClient();

        var byName = await PageAsync(client, "?q=kenya");
        var byDescription = await PageAsync(client, "?q=bergamot");
        var bySku = await PageAsync(client, "?q=khv-003");
        var wildcard = await PageAsync(client, "?q=100%25");

        Assert.Equal("Kenya AA 250 g", Assert.Single(byName.Items).Name);
        Assert.Equal("CAY-002", Assert.Single(byDescription.Items).Sku);
        Assert.Equal("KHV-003", Assert.Single(bySku.Items).Sku);
        Assert.Empty(wildcard.Items);
    }

    [Fact]
    public async Task Filters_sort_and_pages_compose()
    {
        using var client = factory.CreateClient();

        var cheapCoffee = await PageAsync(client, "?category=kahve&maxPrice=400&sort=PriceAsc");
        Assert.All(cheapCoffee.Items, p => Assert.True(p.Price <= 400 && p.Category.Slug == "kahve"));
        Assert.Equal(cheapCoffee.Items.Select(p => p.Price).Order(), cheapCoffee.Items.Select(p => p.Price));

        var expensiveFirst = await PageAsync(client, "?sort=PriceDesc&pageSize=1");
        Assert.Equal("Kuğu Boyunlu Kettle 1 L", Assert.Single(expensiveFirst.Items).Name);

        var second = await PageAsync(client, "?sort=Name&pageSize=5&page=2");
        Assert.Equal(5, second.Items.Count);
        Assert.True(second.Total >= 16);
    }

    [Theory]
    [InlineData("?pageSize=500")]
    [InlineData("?page=0")]
    [InlineData("?minPrice=500&maxPrice=100")]
    public async Task Bad_queries_are_refused(string query)
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/catalog/products{query}")).StatusCode);
    }

    [Fact]
    public async Task A_product_is_found_by_its_slug()
    {
        using var client = factory.CreateClient();

        var product = await client.GetFromJsonAsync<ProductResponse>("/api/catalog/products/chemex-6-fincan", Http.Json);

        Assert.Equal(2390.00m, product!.Price);
        Assert.Equal("TRY", product.Currency);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/products/no-such-thing")).StatusCode);
    }

    private static async Task<ProductPage> PageAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<ProductPage>($"/api/catalog/products{query}", Http.Json))!;
}

[Collection(CatalogCollection.Name)]
public sealed class AdministrationTests(CatalogFactory factory)
{
    private async Task<Guid> CategoryAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/catalog/categories", new { name = Http.Unique("Kategori") });
        return (await response.ReadAsync<CategoryResponse>()).Id;
    }

    private static async Task<ProductResponse> CreateAsync(HttpClient admin, Guid category, string name = "Moka Pot 3 Fincan", decimal price = 799.90m)
    {
        var response = await admin.PostAsJsonAsync("/api/catalog/products", new
        {
            sku = Http.Unique("MKP").ToUpperInvariant(),
            name,
            description = "Alüminyum ocak üstü kahve makinesi.",
            price,
            categoryId = category,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProductResponse>();
    }

    [Fact]
    public async Task Admins_add_products_with_a_slug_from_the_name()
    {
        using var admin = factory.Admin();
        var category = await CategoryAsync(admin);

        var first = await CreateAsync(admin, category, "Çift Cidarlı Bardak");
        var second = await CreateAsync(admin, category, "Çift Cidarlı Bardak");

        Assert.Equal("cift-cidarli-bardak", first.Slug);
        Assert.Equal($"cift-cidarli-bardak-{second.Sku.ToLowerInvariant()}", second.Slug);
    }

    [Fact]
    public async Task An_outdated_version_cannot_overwrite_a_newer_change()
    {
        using var admin = factory.Admin();
        var category = await CategoryAsync(admin);
        var product = await CreateAsync(admin, category);

        var first = await admin.PutAsJsonAsync($"/api/catalog/products/{product.Id}", new { name = product.Name, description = "", price = 849.90m, categoryId = category, version = product.Version });
        var stale = await admin.PutAsJsonAsync($"/api/catalog/products/{product.Id}", new { name = product.Name, description = "", price = 699.90m, categoryId = category, version = product.Version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = await admin.GetFromJsonAsync<ProductResponse>($"/api/catalog/products/{product.Slug}", Http.Json);
        Assert.Equal(849.90m, current!.Price);
        Assert.NotEqual(product.Version, current.Version);
    }

    [Fact]
    public async Task Archived_products_disappear_for_customers_but_not_for_admins()
    {
        using var admin = factory.Admin();
        using var anonymous = factory.CreateClient();
        var product = await CreateAsync(admin, await CategoryAsync(admin), Http.Unique("Arşiv"));

        await admin.PostAsync($"/api/catalog/products/{product.Id}/archive", null);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/catalog/products/{product.Slug}")).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<ProductPage>($"/api/catalog/products?q={product.Sku}", Http.Json))!.Items);
        Assert.Single((await admin.GetFromJsonAsync<ProductPage>($"/api/catalog/products?q={product.Sku}&includeArchived=true", Http.Json))!.Items);
        Assert.Empty((await anonymous.GetFromJsonAsync<ProductPage>($"/api/catalog/products?q={product.Sku}&includeArchived=true", Http.Json))!.Items);

        await admin.PostAsync($"/api/catalog/products/{product.Id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/catalog/products/{product.Slug}")).StatusCode);
    }

    [Fact]
    public async Task Customers_and_anonymous_visitors_cannot_change_the_catalog()
    {
        using var customer = factory.Customer();
        using var anonymous = factory.CreateClient();
        var body = new { name = "Yeni" };

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/catalog/categories", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/catalog/categories", body)).StatusCode);
    }

    [Fact]
    public async Task Invalid_products_and_duplicate_skus_are_refused()
    {
        using var admin = factory.Admin();
        var category = await CategoryAsync(admin);

        var badPrice = await admin.PostAsJsonAsync("/api/catalog/products", new { sku = "BAD-001", name = "Fincan", price = 10.999m, categoryId = category });
        var noCategory = await admin.PostAsJsonAsync("/api/catalog/products", new { sku = "BAD-002", name = "Fincan", price = 10m, categoryId = Guid.NewGuid() });
        var duplicate = await admin.PostAsJsonAsync("/api/catalog/products", new { sku = "khv-001", name = "Kopya", price = 10m, categoryId = category });

        Assert.Equal(HttpStatusCode.BadRequest, badPrice.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }
}
