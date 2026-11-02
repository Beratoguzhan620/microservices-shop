using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Contracts.Ordering;
using Shop.Ordering.Data;
using Shop.Ordering.Orders;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;

namespace Shop.Ordering.Features;

public sealed record OrderItemResponse(Guid ProductId, string Sku, string Name, decimal UnitPrice, int Quantity, decimal Total);

public sealed record AddressResponse(string FullName, string Line1, string City, string PostalCode, string Phone);

public sealed record OrderResponse(
    Guid Id, OrderStatus Status, string? Reason, decimal Total, string Currency, IReadOnlyList<OrderItemResponse> Items,
    AddressResponse ShippingAddress, string CustomerName, string CustomerEmail, string? CardLast4, string? TrackingCode, bool Refunded,
    bool CanCancel, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.Status, o.Reason, o.Total, "TRY",
        o.Items.Select(i => new OrderItemResponse(i.ProductId, i.Sku, i.Name, i.UnitPrice, i.Quantity, i.UnitPrice * i.Quantity)).ToList(),
        new AddressResponse(o.ShippingAddress.FullName, o.ShippingAddress.Line1, o.ShippingAddress.City, o.ShippingAddress.PostalCode, o.ShippingAddress.Phone),
        o.CustomerName, o.CustomerEmail, o.CardLast4, o.TrackingCode, o.Refunded, o.CanCancel, o.CreatedAt, o.UpdatedAt);
}

public sealed record CancelRequest(string? Reason);

public sealed record ShipRequest(string TrackingCode);

public sealed record OrderSummary(Guid Id, OrderStatus Status, decimal Total, int ItemCount, string CustomerName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record OrderPage(IReadOnlyList<OrderSummary> Items, int Total, int Page, int PageSize);

internal static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/ordering/orders").WithTags("Orders").RequireAuthorization();
        mine.MapGet("/", Mine).WithSummary("Your orders, newest first");
        mine.MapGet("/{id:guid}", Get).WithSummary("One order")
            .WithDescription("Right after checkout the order may not exist yet for a moment (404): it is created from the checkout message. Ask again.");
        mine.MapPost("/{id:guid}/cancel", Cancel).WithSummary("Cancel an order before it ships")
            .WithDescription("Possible while the order is Confirmed. The stock is released and the payment refunded; the answer is 202 and the order moves to Cancelled in a moment.");

        var admin = app.MapGroup("/api/ordering/admin/orders").WithTags("Order administration").RequireAuthorization(AuthDefaults.AdminPolicy);
        admin.MapGet("/", All).WithSummary("All orders (admin)").WithDescription("Filter by `status`; newest first.");
        admin.MapPost("/{id:guid}/ship", Ship).WithSummary("Mark an order as handed to the carrier (admin)")
            .WithDescription("Possible while the order is Confirmed. Its stock leaves the warehouse for good.");
        return app;
    }

    private static async Task<Ok<List<OrderSummary>>> Mine(ClaimsPrincipal user, OrderingDbContext db, CancellationToken ct)
    {
        var customerId = user.UserId();
        return TypedResults.Ok(await Summaries(db.Orders.Where(o => o.CustomerId == customerId)).Take(100).ToListAsync(ct));
    }

    private static async Task<Ok<OrderResponse>> Get(Guid id, ClaimsPrincipal user, OrderingDbContext db, CancellationToken ct)
    {
        // Someone else's order does not exist as far as the caller can tell.
        return TypedResults.Ok(OrderResponse.From(await FindVisibleAsync(id, user, db, ct)));
    }

    private static async Task<Accepted<OrderResponse>> Cancel(
        Guid id, CancelRequest? request, ClaimsPrincipal user, OrderingDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var order = await FindVisibleAsync(id, user, db, ct);
        if (!order.CanCancel)
        {
            throw new DomainException(ErrorCodes.InvalidState, order.Status == OrderStatus.Shipped
                ? "The order has shipped; it can no longer be cancelled."
                : $"A {order.Status.ToString().ToLowerInvariant()} order cannot be cancelled.");
        }

        var reason = string.IsNullOrWhiteSpace(request?.Reason)
            ? (user.IsInRole(ShopRoles.Admin) && order.CustomerId != user.UserId() ? "Cancelled by the shop." : "Cancelled by the customer.")
            : request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, 200)];
        await publish.Publish(new OrderCancellationRequested(order.Id, reason, clock.GetUtcNow()), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted($"/api/ordering/orders/{order.Id}", OrderResponse.From(order));
    }

    private static async Task<Results<Accepted<OrderResponse>, ValidationProblem>> Ship(
        Guid id, ShipRequest request, ClaimsPrincipal user, OrderingDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var tracking = request.TrackingCode?.Trim() ?? string.Empty;
        var validation = new Validation().Require(tracking.Length is >= 4 and <= 40, "trackingCode", "A tracking code has 4 to 40 characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var order = await FindVisibleAsync(id, user, db, ct);
        if (!order.CanShip)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"Only confirmed orders ship; this one is {order.Status.ToString().ToLowerInvariant()}.");
        }

        await publish.Publish(new OrderShipmentRequested(order.Id, tracking, clock.GetUtcNow()), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted($"/api/ordering/orders/{order.Id}", OrderResponse.From(order));
    }

    private static async Task<Order> FindVisibleAsync(Guid id, ClaimsPrincipal user, OrderingDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        return order is not null && (order.CustomerId == user.UserId() || user.IsInRole(ShopRoles.Admin))
            ? order
            : throw new DomainException(ErrorCodes.NotFound, "There is no such order (yet).");
    }

    private static async Task<Results<Ok<OrderPage>, ValidationProblem>> All(
        OrderingDbContext db, CancellationToken ct, OrderStatus? status = null, int page = 1, int pageSize = 25)
    {
        var validation = new Validation()
            .Require(page >= 1, "page", "Pages start at 1.")
            .Require(pageSize is >= 1 and <= 100, "pageSize", "A page has 1 to 100 orders.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.Orders.AsQueryable();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        var total = await query.CountAsync(ct);
        var items = await Summaries(query).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return TypedResults.Ok(new OrderPage(items, total, page, pageSize));
    }

    private static IQueryable<OrderSummary> Summaries(IQueryable<Order> orders) =>
        orders.AsNoTracking().OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummary(o.Id, o.Status, o.Total, o.Items.Sum(i => i.Quantity), o.CustomerName, o.CreatedAt, o.UpdatedAt));
}
