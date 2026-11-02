using System.Security.Cryptography;
using Shop.ServiceDefaults.Errors;

namespace Shop.Payment.Payments;

/// <summary>What the pretend card network does with a card, chosen by its number like a real provider's test cards.</summary>
public enum CardBehaviour
{
    Approve,
    Decline,
    InsufficientFunds,

    /// <summary>The network never answers, so the order's payment deadline is what ends it.</summary>
    NoAnswer,
}

/// <summary>
/// The card is checked and turned into a token here; the order process only ever sees the token. The full number
/// and the CVC are not stored anywhere, only the last four digits and how the test card behaves.
/// </summary>
public sealed class CardToken
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private CardToken()
    {
    }

    public string Token { get; private set; } = string.Empty;

    public Guid CustomerId { get; private set; }

    public string Last4 { get; private set; } = string.Empty;

    public string Brand { get; private set; } = string.Empty;

    public CardBehaviour Behaviour { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static readonly IReadOnlyDictionary<string, CardBehaviour> TestCards = new Dictionary<string, CardBehaviour>
    {
        ["4242424242424242"] = CardBehaviour.Approve,
        ["5555555555554444"] = CardBehaviour.Approve,
        ["4000000000000002"] = CardBehaviour.Decline,
        ["4000000000009995"] = CardBehaviour.InsufficientFunds,
        ["4000000000000077"] = CardBehaviour.NoAnswer,
    };

    public static CardToken Create(Guid customerId, string? number, int expiryMonth, int expiryYear, string? cvc, DateTimeOffset now)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 13 or > 19 || !Luhn(digits))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The card number is not valid.");
        }

        if (expiryMonth is < 1 or > 12 || new DateTime(expiryYear is >= 2000 and <= 2100 ? expiryYear : 2000, expiryMonth, 1).AddMonths(1) <= now.UtcDateTime)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The card has expired.");
        }

        if (cvc is null || cvc.Length is < 3 or > 4 || !cvc.All(char.IsAsciiDigit))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The security code has 3 or 4 digits.");
        }

        return new CardToken
        {
            Token = "tok_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            CustomerId = customerId,
            Last4 = digits[^4..],
            Brand = digits[0] switch { '4' => "Visa", '5' => "Mastercard", '3' => "Amex", _ => "Card" },
            Behaviour = TestCards.TryGetValue(digits, out var behaviour) ? behaviour : CardBehaviour.Approve,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        };
    }

    /// <summary>A token pays for one order, for the customer it was made for, within half an hour.</summary>
    public string? Problem(Guid customerId, DateTimeOffset now) =>
        CustomerId != customerId ? "The payment token belongs to someone else."
        : UsedAt is not null ? "The payment token was already used."
        : now >= ExpiresAt ? "The payment token expired; enter the card again."
        : null;

    public void Use(DateTimeOffset now) => UsedAt = now;

    public static bool Luhn(string digits)
    {
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
        }

        return sum % 10 == 0;
    }
}
