using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shop.ServiceDefaults.Auth;

namespace Shop.Testing;

/// <summary>A key pair made for the test run, standing in for the Identity service's keys.</summary>
public static class TestKeys
{
    private static readonly RSA Rsa = RSA.Create(2048);

    public static string PrivateKeyPem { get; } = Rsa.ExportPkcs8PrivateKeyPem();

    public static string PublicKeyPem { get; } = Rsa.ExportSubjectPublicKeyInfoPem();

    /// <summary>A token as the Identity service would issue it, for testing the other services on their own.</summary>
    public static string Token(Guid userId, string role = ShopRoles.Customer, string name = "Ayşe Yılmaz", string email = "ayse@example.com",
        RSA? signingKey = null, string issuer = ShopClaims.Issuer, DateTimeOffset? expires = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = ShopClaims.Audience,
            IssuedAt = now.AddMinutes(-1).UtcDateTime,
            NotBefore = now.AddMinutes(-1).UtcDateTime,
            Expires = (expires ?? now.AddHours(1)).UtcDateTime,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signingKey ?? Rsa), SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ShopClaims.Role, role),
                new Claim(ShopClaims.Name, name),
                new Claim(ShopClaims.Email, email),
            ]),
        });
    }
}
