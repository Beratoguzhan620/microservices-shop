using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Payment.Data;
using Shop.Payment.Payments;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;

namespace Shop.Payment.Features;

/// <summary>Card details go here and nowhere else; the CVC is checked and forgotten.</summary>
public sealed record CardRequest(string Number, int ExpiryMonth, int ExpiryYear, string Cvc);

public sealed record CardTokenResponse(string Token, string Brand, string Last4, DateTimeOffset ExpiresAt);

public sealed record PaymentResponse(Guid Id, Guid OrderId, decimal Amount, PaymentStatus Status, string CardLast4, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

internal static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payment/cards", Tokenize).WithTags("Payment").RequireAuthorization()
            .WithSummary("Turn a card into a payment token for checkout")
            .WithDescription("""
                The token pays for one order within 30 minutes. Test cards: `4242 4242 4242 4242` is approved,
                `4000 0000 0000 0002` is declined, `4000 0000 0000 9995` has insufficient funds, and
                `4000 0000 0000 0077` never gets an answer, so the order times out. Any future expiry and CVC work.
                """);
        app.MapGet("/api/payment/payments/{orderId:guid}", ByOrder).WithTags("Payment").RequireAuthorization(AuthDefaults.AdminPolicy)
            .WithSummary("The payment of an order (admin)");
        return app;
    }

    private static async Task<Created<CardTokenResponse>> Tokenize(CardRequest request, ClaimsPrincipal user, PaymentDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var token = CardToken.Create(user.UserId(), request.Number, request.ExpiryMonth, request.ExpiryYear, request.Cvc, clock.GetUtcNow());
        db.CardTokens.Add(token);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created("/api/payment/cards", new CardTokenResponse(token.Token, token.Brand, token.Last4, token.ExpiresAt));
    }

    private static async Task<Ok<PaymentResponse>> ByOrder(Guid orderId, PaymentDbContext db, CancellationToken ct)
    {
        var p = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.OrderId == orderId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This order has no payment.");
        return TypedResults.Ok(new PaymentResponse(p.Id, p.OrderId, p.Amount, p.Status, p.CardLast4, p.Reason, p.CreatedAt, p.UpdatedAt));
    }
}
