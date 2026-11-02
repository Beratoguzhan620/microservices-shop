namespace Shop.ServiceDefaults.Errors;

/// <summary>
/// A business rule was broken. <see cref="Code"/> is a stable, machine-readable value that every service returns
/// in its problem responses, so clients handle errors the same way whichever service answered.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The error codes shared by all services.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Forbidden = "forbidden";
    public const string Conflict = "conflict";
    public const string InvalidState = "invalid_state";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string OutOfStock = "out_of_stock";
    public const string PaymentDeclined = "payment_declined";
    public const string RateLimited = "rate_limited";

    private static readonly Dictionary<string, int> Statuses = new(StringComparer.Ordinal)
    {
        [ValidationFailed] = 400,
        [InvalidCredentials] = 401,
        [Forbidden] = 403,
        [NotFound] = 404,
        [Conflict] = 409,
        [InvalidState] = 409,
        [EmailTaken] = 409,
        [OutOfStock] = 409,
        [PaymentDeclined] = 402,
        [RateLimited] = 429,
    };

    /// <summary>Unknown codes are business rule failures (422).</summary>
    public static int StatusFor(string code) => Statuses.TryGetValue(code, out var status) ? status : 422;
}
