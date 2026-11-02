using Microsoft.AspNetCore.Identity;
using Shop.Identity.Users;

namespace Shop.Identity.Features;

/// <summary>
/// ASP.NET Core Identity's PBKDF2 hasher. Unknown emails are checked against a dummy hash, so signing in with a
/// missing account takes as long as with a wrong password.
/// </summary>
internal sealed class Passwords
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    private static readonly User Dummy = User.Register("dummy@invalid.example", "Dummy", DateTimeOffset.UnixEpoch);

    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _dummyHash;

    public Passwords()
    {
        _dummyHash = _hasher.HashPassword(Dummy, Guid.NewGuid().ToString());
    }

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User? user, string password, out string? rehash)
    {
        rehash = null;
        if (user is null)
        {
            _hasher.VerifyHashedPassword(Dummy, _dummyHash, password);
            return false;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            rehash = _hasher.HashPassword(user, password);
        }

        return result != PasswordVerificationResult.Failed;
    }
}
