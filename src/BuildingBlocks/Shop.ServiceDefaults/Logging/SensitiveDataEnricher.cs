using Serilog.Core;
using Serilog.Events;

namespace Shop.ServiceDefaults.Logging;

/// <summary>Shortens personal data to what is still useful when reading logs.</summary>
public static class Masking
{
    public const string Hidden = "***";

    /// <summary><c>ayse.yilmaz@example.com</c> → <c>a***@example.com</c>.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Hidden;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? Hidden : $"{email[0]}{Hidden}{email[at..]}";
    }

    /// <summary>Only the last four digits of a card number: <c>**** 4242</c>.</summary>
    public static string Card(string? number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length < 4 ? Hidden : $"**** {digits[^4..]}";
    }
}

/// <summary>
/// Last line of defence for log output: any property whose name says it holds a secret or personal data is
/// masked before it reaches a sink, whoever wrote the log call.
/// </summary>
public sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> Secrets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Token", "Authorization", "ConnectionString", "AccessToken", "access_token", "Cvc", "Cvv", "PrivateKey",
    };

    private static readonly HashSet<string> Emails = new(StringComparer.OrdinalIgnoreCase) { "Email", "CustomerEmail" };

    private static readonly HashSet<string> Cards = new(StringComparer.OrdinalIgnoreCase) { "CardNumber", "Pan" };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var text = value is ScalarValue { Value: { } scalar } ? scalar.ToString() : null;
            var masked = name switch
            {
                _ when Secrets.Contains(name) => Masking.Hidden,
                _ when Emails.Contains(name) => Masking.Email(text),
                _ when Cards.Contains(name) => Masking.Card(text),
                _ => null,
            };

            if (masked is not null)
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(masked)));
            }
        }
    }
}
