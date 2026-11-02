using System.Net.Mail;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;

namespace Shop.Identity.Users;

/// <summary>A shop account: a customer, or an admin who manages the catalog, stock and orders.</summary>
public sealed class User
{
    public const int DisplayNameMinLength = 2;
    public const int DisplayNameMaxLength = 60;
    public const int EmailMaxLength = 254;

    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = ShopRoles.Customer;

    public DateTimeOffset CreatedAt { get; private set; }

    public static User Register(string email, string displayName, DateTimeOffset now)
    {
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length is < DisplayNameMinLength or > DisplayNameMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed,
                $"A display name has {DisplayNameMinLength} to {DisplayNameMaxLength} characters.");
        }

        return new User
        {
            Id = Guid.CreateVersion7(now),
            Email = NormalizeEmail(email),
            DisplayName = name,
            Role = ShopRoles.Customer,
            CreatedAt = now,
        };
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void MakeAdmin() => Role = ShopRoles.Admin;

    public static string NormalizeEmail(string? email)
    {
        var trimmed = (email ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > EmailMaxLength
            || !MailAddress.TryCreate(trimmed, out var address)
            || !string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase)
            || !address.Host.Contains('.', StringComparison.Ordinal))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A valid email address is required.");
        }

        return trimmed.ToLowerInvariant();
    }
}
