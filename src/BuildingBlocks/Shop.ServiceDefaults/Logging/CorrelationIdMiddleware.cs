using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Shop.ServiceDefaults.Logging;

/// <summary>
/// One id per request, from the gateway through every service it calls. The gateway creates it (or keeps a
/// well-formed one from the caller) and forwards it; each service puts it in its responses, error bodies and logs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsAcceptable(incoming) ? incoming : Guid.NewGuid().ToString("N");

        // Forwarded by the gateway to the services behind it.
        context.Request.Headers[HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // Anything else could be used to inject text into logs, so it is replaced rather than trusted.
    private static bool IsAcceptable(string value) =>
        value.Length is > 0 and <= MaxLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
