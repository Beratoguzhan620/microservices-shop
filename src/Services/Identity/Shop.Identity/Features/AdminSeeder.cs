using Microsoft.EntityFrameworkCore;
using Shop.Identity.Data;
using Shop.Identity.Users;

namespace Shop.Identity.Features;

/// <summary>
/// The first admin comes from configuration (<c>Identity:Admin:Email</c> and <c>Identity:Admin:Password</c>), never
/// from the API: nobody can sign up as an admin. An existing account with that email is made admin.
/// </summary>
internal static partial class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var email = configuration["Identity:Admin:Email"];
        var password = configuration["Identity:Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null)
        {
            user = User.Register(normalized, configuration["Identity:Admin:DisplayName"] ?? "Shop Admin",
                scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow());
            user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<Passwords>().Hash(user, password));
            db.Users.Add(user);
        }

        user.MakeAdmin();
        await db.SaveChangesAsync(ct);
        LogSeeded(scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminSeeder)), user.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin account {UserId} is ready")]
    private static partial void LogSeeded(ILogger logger, Guid userId);
}
