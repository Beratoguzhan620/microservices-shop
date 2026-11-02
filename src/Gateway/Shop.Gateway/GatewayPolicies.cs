using Microsoft.AspNetCore.RateLimiting;
using Shop.ServiceDefaults.Errors;

namespace Shop.Gateway;

internal static class GatewayPolicies
{
    public const string AuthRateLimit = "auth";

    /// <summary>The services whose API documents the gateway's reference shows.</summary>
    public static readonly (string Name, string Title)[] Documents =
    [
        ("catalog", "Catalog"),
        ("basket", "Basket"),
        ("ordering", "Ordering"),
        ("inventory", "Inventory"),
        ("identity", "Identity"),
    ];

    /// <summary>The browser's address (nginx forwards it), never the proxy's.</summary>
    public static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken ct)
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await TypedResults.Problem(
                title: "Too many requests",
                detail: "Slow down and try again in a minute.",
                statusCode: StatusCodes.Status429TooManyRequests,
                extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.RateLimited })
            .ExecuteAsync(context.HttpContext);
    }
}
