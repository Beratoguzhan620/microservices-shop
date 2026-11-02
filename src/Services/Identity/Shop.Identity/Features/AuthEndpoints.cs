using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Identity.Data;
using Shop.Identity.Users;
using Shop.ServiceDefaults;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;
using Shop.ServiceDefaults.Logging;

namespace Shop.Identity.Features;

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName, string Role)
{
    public static UserResponse From(User user) => new(user.Id, user.Email, user.DisplayName, user.Role);
}

public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);

internal static partial class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity").WithTags("Accounts");
        group.MapPost("/register", Register).WithSummary("Create a customer account")
            .WithDescription("The response holds an access token, so the customer is signed in at once.");
        group.MapPost("/login", Login).WithSummary("Sign in")
            .WithDescription("A wrong email and a wrong password get the same answer. The gateway limits attempts per address.");
        group.MapGet("/me", Me).RequireAuthorization().WithSummary("The signed-in account");
        return app;
    }

    private static async Task<Results<Created<SessionResponse>, ValidationProblem>> Register(
        RegisterRequest request, IdentityDbContext db, Passwords passwords, TokenIssuer tokens, TimeProvider clock,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(request.DisplayName?.Trim().Length is >= User.DisplayNameMinLength and <= User.DisplayNameMaxLength,
                "displayName", $"A display name has {User.DisplayNameMinLength} to {User.DisplayNameMaxLength} characters.")
            .Require(request.Password is { Length: >= Passwords.MinLength and <= Passwords.MaxLength },
                "password", $"A password has {Passwords.MinLength} to {Passwords.MaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var user = User.Register(request.Email, request.DisplayName!, clock.GetUtcNow());
        user.SetPasswordHash(passwords.Hash(user, request.Password));
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.EmailTaken, "An account with this email already exists.");
        }

        LogRegistered(loggers.CreateLogger(typeof(AuthEndpoints)), user.Id);
        var (token, expiresAt) = tokens.Issue(user);
        return TypedResults.Created("/api/identity/me", new SessionResponse(token, expiresAt, UserResponse.From(user)));
    }

    private static async Task<Results<Ok<SessionResponse>, ValidationProblem>> Login(
        LoginRequest request, IdentityDbContext db, Passwords passwords, TokenIssuer tokens, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Password is required.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        if (!passwords.Verify(user, request.Password, out var rehash) || user is null)
        {
            LogFailed(loggers.CreateLogger(typeof(AuthEndpoints)), Masking.Email(email));
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            user.SetPasswordHash(rehash);
            await db.SaveChangesAsync(ct);
        }

        var (token, expiresAt) = tokens.Issue(user);
        return TypedResults.Ok(new SessionResponse(token, expiresAt, UserResponse.From(user)));
    }

    private static async Task<Ok<UserResponse>> Me(ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct)
    {
        var id = principal.UserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The account no longer exists.");
        return TypedResults.Ok(UserResponse.From(user));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Account {UserId} registered")]
    private static partial void LogRegistered(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed sign-in for {MaskedEmail}")]
    private static partial void LogFailed(ILogger logger, string maskedEmail);
}
