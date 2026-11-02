using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shop.ServiceDefaults.Errors;

namespace Shop.ServiceDefaults.Auth;

public static class ShopRoles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";
}

public static class ShopClaims
{
    public const string Role = "role";
    public const string Name = "name";
    public const string Email = "email";
    public const string Issuer = "shop-identity";
    public const string Audience = "shop";

    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new DomainException(ErrorCodes.Forbidden, "The access token names no user.");

    public static string DisplayName(this ClaimsPrincipal principal) => principal.FindFirstValue(Name) ?? string.Empty;

    public static string EmailAddress(this ClaimsPrincipal principal) => principal.FindFirstValue(Email) ?? string.Empty;
}

/// <summary>
/// Tokens are signed by the Identity service with its RSA private key; every other service and the gateway only
/// hold the public key. A service that is compromised can read tokens but cannot mint them.
/// </summary>
public static class JwtKeys
{
    /// <summary>The key from <c>Jwt:{name}</c> (PEM text) or <c>Jwt:{name}Path</c> (a PEM file, as in Compose).</summary>
    public static RSA Load(IConfiguration configuration, string name)
    {
        var pem = configuration[$"Jwt:{name}"];
        var path = configuration[$"Jwt:{name}Path"];
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(path))
        {
            pem = File.ReadAllText(path);
        }

        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException(
                $"Jwt:{name} is not configured. Set Jwt:{name} (PEM) or Jwt:{name}Path (a PEM file); see the README.");
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}

public static class AuthDefaults
{
    public const string AdminPolicy = "Admin";

    /// <summary>Validates the Identity service's tokens with its public key; adds the Admin policy.</summary>
    public static IServiceCollection AddShopAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var key = new RsaSecurityKey(JwtKeys.Load(configuration, "PublicKey"));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(bearer =>
        {
            bearer.MapInboundClaims = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = ShopClaims.Issuer,
                ValidAudience = ShopClaims.Audience,
                IssuerSigningKey = key,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                NameClaimType = JwtRegisteredClaimNames.Sub,
                RoleClaimType = ShopClaims.Role,
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(ShopRoles.Admin));
        return services;
    }
}
