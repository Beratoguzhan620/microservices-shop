using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shop.Testing;

namespace Shop.Gateway.Tests;

public sealed class GatewayTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    [Theory]
    [InlineData("/api/catalog/products?q=kahve", "catalog", "/api/catalog/products", "?q=kahve")]
    [InlineData("/api/identity/me", "identity", "/api/identity/me", "")]
    public async Task Requests_go_to_the_right_service_unchanged(string url, string service, string path, string query)
    {
        using var client = factory.CreateClient();

        var echo = await client.GetFromJsonAsync<Echo>(url, Http.Json);

        Assert.Equal(service, echo!.Service);
        Assert.Equal(path, echo.Path);
        Assert.Equal(query, echo.Query);
    }

    [Theory]
    [InlineData("/api/basket")]
    [InlineData("/api/ordering/orders")]
    public async Task Customer_routes_need_a_valid_token_at_the_gateway(string url)
    {
        using var anonymous = factory.CreateClient();
        using var forged = factory.CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), signingKey: System.Security.Cryptography.RSA.Create(2048)));
        using var customer = factory.CreateClient().WithToken(TestKeys.Token(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Unknown_paths_are_not_forwarded()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/secret-admin")).StatusCode);
    }

    [Fact]
    public async Task The_correlation_id_reaches_the_service_and_comes_back()
    {
        using var client = factory.CreateClient();
        using var mine = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/categories");
        mine.Headers.Add("X-Correlation-Id", "order-flow-42");
        using var injected = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/categories");
        injected.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad id <script>");

        var kept = await client.SendAsync(mine);
        var replaced = await client.SendAsync(injected);

        Assert.Equal("order-flow-42", (await kept.ReadAsync<Echo>()).CorrelationId);
        Assert.Equal("order-flow-42", kept.Headers.GetValues("X-Correlation-Id").Single());
        var fresh = (await replaced.ReadAsync<Echo>()).CorrelationId;
        Assert.Matches("^[0-9a-f]{32}$", fresh);
        Assert.Equal(fresh, replaced.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task The_token_is_passed_on_to_the_service()
    {
        var token = TestKeys.Token(Guid.NewGuid());
        using var client = factory.CreateClient().WithToken(token);

        var echo = await client.GetFromJsonAsync<Echo>("/api/identity/me", Http.Json);

        Assert.Equal($"Bearer {token}", echo!.Authorization);
    }

    [Fact]
    public async Task Sign_in_attempts_are_limited_per_address_with_a_problem_response()
    {
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? limited = null;
        for (var i = 0; i < GatewayFactory.AuthPerMinute + 1; i++)
        {
            var response = await client.PostAsJsonAsync("/api/identity/login", new { email = "a@example.com", password = "x" });
            statuses.Add(response.StatusCode);
            limited = response;
        }

        Assert.Equal(GatewayFactory.AuthPerMinute, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited!.StatusCode);
        Assert.Equal("rate_limited", await limited.ProblemCodeAsync());
        Assert.Equal("60", limited.Headers.RetryAfter!.ToString());

        // Other routes are not affected by the sign-in limit.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/me")).StatusCode);
    }

    [Fact]
    public async Task Each_service_document_is_served_through_the_gateway()
    {
        using var client = factory.CreateClient();

        using var catalog = JsonDocument.Parse(await client.GetStringAsync("/openapi/catalog.json"));
        using var identity = JsonDocument.Parse(await client.GetStringAsync("/openapi/identity.json"));

        Assert.Equal("catalog", catalog.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("identity", identity.RootElement.GetProperty("info").GetProperty("title").GetString());
        foreach (var name in new[] { "basket", "inventory", "ordering" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/openapi/{name}.json")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/scalar")).StatusCode);
    }

    [Fact]
    public async Task Readiness_names_the_service_that_is_down()
    {
        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready");
        factory.Catalog.Ready = false;
        try
        {
            var notReady = await client.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
            using var body = JsonDocument.Parse(await notReady.Content.ReadAsStringAsync());
            Assert.Equal("Unhealthy", body.RootElement.GetProperty("services").GetProperty("catalog").GetString());
            Assert.Equal("Healthy", body.RootElement.GetProperty("services").GetProperty("identity").GetString());
        }
        finally
        {
            factory.Catalog.Ready = true;
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
