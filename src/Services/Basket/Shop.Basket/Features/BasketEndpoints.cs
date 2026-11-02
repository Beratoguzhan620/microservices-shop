using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Shop.Basket.Baskets;
using Shop.Contracts.Ordering;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;

namespace Shop.Basket.Features;

public sealed record BasketLineResponse(Guid ProductId, string Sku, string Name, decimal UnitPrice, int Quantity, decimal Total);

public sealed record BasketResponse(IReadOnlyList<BasketLineResponse> Lines, int Count, decimal Total, string Currency)
{
    public static BasketResponse From(CustomerBasket basket) => new(
        basket.Lines.Select(l => new BasketLineResponse(l.ProductId, l.Sku, l.Name, l.UnitPrice, l.Quantity, l.Total)).ToList(),
        basket.Count, basket.Total, "TRY");
}

public sealed record SetQuantityRequest(int Quantity);

public sealed record AddressRequest(string FullName, string Line1, string City, string PostalCode, string Phone);

/// <summary><c>ExpectedTotal</c> is the total the customer was shown; if prices moved since, nothing is ordered.</summary>
public sealed record CheckoutRequest(AddressRequest Address, decimal ExpectedTotal);

public sealed record CheckoutResponse(Guid OrderId);

internal static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder app)
    {
        var basket = app.MapGroup("/api/basket").WithTags("Basket").RequireAuthorization();
        basket.MapGet("/", Get).WithSummary("Your basket");
        basket.MapPut("/items/{productId:guid}", Set).WithSummary("Set how many of a product are in the basket")
            .WithDescription("0 removes the line. The name and price are taken from the catalog now.");
        basket.MapDelete("/items/{productId:guid}", Remove).WithSummary("Remove a product");
        basket.MapDelete("/", Clear).WithSummary("Empty the basket");
        basket.MapPost("/checkout", Checkout).WithSummary("Place the order")
            .WithDescription("""
                Checks every price against the catalog first. If a price changed or a product is no longer sold, the
                answer is 409 `basket_changed` with the updated basket and nothing is ordered. Otherwise the order is
                handed to Ordering and the answer is 202 with its id: follow it at `GET /api/ordering/orders/{id}`.
                """);
        return app;
    }

    private static async Task<Ok<BasketResponse>> Get(ClaimsPrincipal user, BasketStore store, TimeProvider clock) =>
        TypedResults.Ok(BasketResponse.From(await store.GetAsync(user.UserId()) ?? CustomerBasket.Empty(user.UserId(), clock.GetUtcNow())));

    private static async Task<Ok<BasketResponse>> Set(
        Guid productId, SetQuantityRequest request, ClaimsPrincipal user, BasketStore store, CatalogClient catalog, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var basket = await store.GetAsync(user.UserId()) ?? CustomerBasket.Empty(user.UserId(), now);
        if (request.Quantity == 0)
        {
            basket = basket.Remove(productId, now);
        }
        else
        {
            var products = await catalog.SnapshotsAsync([productId], ct);
            if (!products.TryGetValue(productId, out var product) || !product.IsActive)
            {
                throw new DomainException(ErrorCodes.NotFound, "This product is not sold.");
            }

            basket = basket.Set(productId, product.Sku, product.Name, product.Price, request.Quantity, now);
        }

        await store.SaveAsync(basket);
        return TypedResults.Ok(BasketResponse.From(basket));
    }

    private static async Task<Ok<BasketResponse>> Remove(Guid productId, ClaimsPrincipal user, BasketStore store, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var basket = (await store.GetAsync(user.UserId()) ?? CustomerBasket.Empty(user.UserId(), now)).Remove(productId, now);
        await store.SaveAsync(basket);
        return TypedResults.Ok(BasketResponse.From(basket));
    }

    private static async Task<NoContent> Clear(ClaimsPrincipal user, BasketStore store)
    {
        await store.DeleteAsync(user.UserId());
        return TypedResults.NoContent();
    }

    private static async Task<Results<Accepted<CheckoutResponse>, ValidationProblem, ProblemHttpResult>> Checkout(
        CheckoutRequest request, ClaimsPrincipal user, BasketStore store, CatalogClient catalog, IPublishEndpoint publish,
        TimeProvider clock, CancellationToken ct)
    {
        var address = request.Address;
        var validation = new Validation()
            .Require(Text(address?.FullName, 3, 100), "address.fullName", "Write the recipient's name.")
            .Require(Text(address?.Line1, 5, 200), "address.line1", "Write the street address.")
            .Require(Text(address?.City, 2, 60), "address.city", "Write the city.")
            .Require(address?.PostalCode is { } code && code.Trim().Length == 5 && code.Trim().All(char.IsAsciiDigit), "address.postalCode", "A postal code has 5 digits.")
            .Require(address?.Phone is { } phone && phone.Count(char.IsAsciiDigit) is >= 10 and <= 13, "address.phone", "Write a phone number with 10 to 13 digits.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var customerId = user.UserId();
        var now = clock.GetUtcNow();

        // Taken out in one step: a second checkout at the same moment finds nothing to order.
        var basket = await store.TakeAsync(customerId);
        if (basket is null || basket.Lines.Count == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The basket is empty.");
        }

        try
        {
            var current = await catalog.SnapshotsAsync(basket.Lines.Select(l => l.ProductId), ct);
            var (repriced, changes) = basket.Reprice(current.ToDictionary(p => p.Key, p => (p.Value.Name, p.Value.Price, p.Value.IsActive)), now);
            if (changes.Count > 0 || repriced.Total != request.ExpectedTotal)
            {
                await store.SaveAsync(repriced);
                return TypedResults.Problem(
                    title: "The basket changed",
                    detail: changes.Count > 0 ? string.Join(" ", changes) : "The total is not the one you saw. Check the basket again.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = ErrorCodes.BasketChanged,
                        ["changes"] = changes,
                        ["basket"] = BasketResponse.From(repriced),
                    });
            }

            var orderId = NewId.NextSequentialGuid();
            await publish.Publish(new BasketCheckedOut(
                orderId, customerId, user.EmailAddress(), user.DisplayName(),
                basket.Lines.Select(l => new OrderLine(l.ProductId, l.Sku, l.Name, l.UnitPrice, l.Quantity)).ToList(),
                new ShippingAddress(address!.FullName.Trim(), address.Line1.Trim(), address.City.Trim(), address.PostalCode.Trim(), address.Phone.Trim()),
                now), ct);
            return TypedResults.Accepted($"/api/ordering/orders/{orderId}", new CheckoutResponse(orderId));
        }
        catch
        {
            // Nothing was ordered: the customer gets their basket back.
            await store.SaveAsync(basket);
            throw;
        }
    }

    private static bool Text(string? value, int min, int max) => value?.Trim().Length is { } length && length >= min && length <= max;
}
