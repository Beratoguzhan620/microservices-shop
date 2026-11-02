using System.Net;
using System.Net.Http.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shop.Identity.Features;
using Shop.ServiceDefaults.Auth;
using Shop.Testing;

namespace Shop.Identity.Tests.Integration;

[Collection(IdentityCollection.Name)]
public sealed class AccountTests(IdentityFactory factory)
{
    private const string Password = "correct-horse-battery";

    private async Task<(HttpResponseMessage Response, string Email)> RegisterAsync(string? email = null, string name = "Ayşe Yılmaz")
    {
        email ??= $"{Http.Unique("ayse")}@example.com";
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/identity/register", new { email, displayName = name, password = Password });
        return (response, email);
    }

    [Fact]
    public async Task Registering_signs_the_customer_in()
    {
        var (response, email) = await RegisterAsync(name: "  Ayşe Yılmaz ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.ReadAsync<SessionResponse>();
        Assert.Equal(email, session.User.Email);
        Assert.Equal("Ayşe Yılmaz", session.User.DisplayName);
        Assert.Equal(ShopRoles.Customer, session.User.Role);

        using var client = factory.CreateClient().WithToken(session.AccessToken);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/identity/me", Http.Json);
        Assert.Equal(session.User.Id, me!.Id);
    }

    [Fact]
    public async Task Tokens_are_RS256_and_check_out_with_the_public_key()
    {
        var (response, email) = await RegisterAsync();
        var session = await response.ReadAsync<SessionResponse>();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(session.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = ShopClaims.Issuer,
            ValidAudience = ShopClaims.Audience,
            IssuerSigningKey = new RsaSecurityKey(PublicKey()),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
        });

        Assert.True(result.IsValid);
        var token = (JsonWebToken)result.SecurityToken;
        Assert.Equal(email, token.GetClaim(ShopClaims.Email).Value);
        Assert.Equal(ShopRoles.Customer, token.GetClaim(ShopClaims.Role).Value);
    }

    private static System.Security.Cryptography.RSA PublicKey()
    {
        var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(TestKeys.PublicKeyPem);
        return rsa;
    }

    [Fact]
    public async Task An_email_is_registered_once_whatever_its_case()
    {
        var (_, email) = await RegisterAsync();

        var (again, _) = await RegisterAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", await again.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("not-an-email", "Ayşe", "correct-horse-battery")]
    [InlineData("a@example.com", "A", "correct-horse-battery")]
    [InlineData("a@example.com", "Ayşe", "short")]
    public async Task Invalid_registrations_are_refused(string email, string name, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/identity/register", new { email, displayName = name, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Sign_in_works_with_the_right_password_and_says_the_same_for_any_mistake()
    {
        var (_, email) = await RegisterAsync();
        using var client = factory.CreateClient();

        var ok = await client.PostAsJsonAsync("/api/identity/login", new { email = $" {email.ToUpperInvariant()} ", password = Password });
        var wrongPassword = await client.PostAsJsonAsync("/api/identity/login", new { email, password = "wrong-password-1" });
        var unknown = await client.PostAsJsonAsync("/api/identity/login", new { email = "nobody@example.com", password = Password });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await DetailAsync(wrongPassword), await DetailAsync(unknown));
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString() + "|" + doc.RootElement.GetProperty("code").GetString();
    }

    [Fact]
    public async Task The_admin_comes_from_configuration_and_nobody_signs_up_as_one()
    {
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/identity/login", new { email = IdentityFactory.AdminEmail, password = IdentityFactory.AdminPassword });
        var (registered, _) = await RegisterAsync();

        Assert.Equal(ShopRoles.Admin, (await login.ReadAsync<SessionResponse>()).User.Role);
        Assert.Equal(ShopRoles.Customer, (await registered.ReadAsync<SessionResponse>()).User.Role);
    }

    [Fact]
    public async Task Me_needs_a_valid_token()
    {
        using var anonymous = factory.CreateClient();
        using var forged = factory.CreateClient().WithToken(TestKeys.Token(Guid.NewGuid(), signingKey: System.Security.Cryptography.RSA.Create(2048)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/identity/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/identity/me")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_the_correlation_id_and_health_is_ready()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        request.Headers.Add("X-Correlation-Id", "test-123");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-123", response.Headers.GetValues("X-Correlation-Id").Single());
    }
}
