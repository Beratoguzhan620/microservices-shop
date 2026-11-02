using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shop.Identity.Users;
using Shop.ServiceDefaults.Auth;

namespace Shop.Identity.Features;

/// <summary>Signs access tokens with the private key that only this service holds (RS256).</summary>
internal sealed class TokenIssuer(IConfiguration configuration, TimeProvider clock)
{
    private readonly SigningCredentials _credentials = new(new RsaSecurityKey(JwtKeys.Load(configuration, "PrivateKey")), SecurityAlgorithms.RsaSha256);
    private readonly TimeSpan _lifetime = TimeSpan.FromMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", 120));
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var now = clock.GetUtcNow();
        var expires = now + _lifetime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ShopClaims.Issuer,
            Audience = ShopClaims.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ShopClaims.Email, user.Email),
                new Claim(ShopClaims.Name, user.DisplayName),
                new Claim(ShopClaims.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
        });
        return (token, expires);
    }
}
