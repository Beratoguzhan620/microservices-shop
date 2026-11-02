using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Inventory.Data;
using Shop.Inventory.Stock;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;

namespace Shop.Inventory.Features;

public sealed record AvailabilityResponse(Guid ProductId, int Available);

public sealed record StockResponse(Guid ProductId, string Sku, string Name, int OnHand, int Reserved, int Available, DateTimeOffset UpdatedAt, uint Version)
{
    public static StockResponse From(StockItem i) => new(i.ProductId, i.Sku, i.Name, i.OnHand, i.Reserved, i.Available, i.UpdatedAt, i.Version);
}

public sealed record CountStockRequest(int OnHand, uint Version);

internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/inventory/availability", Availability).WithTags("Stock")
            .WithSummary("How many of each product can still be ordered")
            .WithDescription("Up to 100 ids (`ids=a&ids=b`). Held stock for orders in progress is not counted. Unknown products are left out.");

        var admin = app.MapGroup("/api/inventory/stock").WithTags("Stock administration").RequireAuthorization(AuthDefaults.AdminPolicy);
        admin.MapGet("/", List).WithSummary("Stock of every product (admin)")
            .WithDescription("Lowest availability first; `q` searches name and SKU.");
        admin.MapPut("/{productId:guid}", Count).WithSummary("Set the count after a delivery or a stocktake (admin)")
            .WithDescription("Send the `version` you read. The count cannot go below what is held for orders in progress.");
        return app;
    }

    private static async Task<Results<Ok<List<AvailabilityResponse>>, ValidationProblem>> Availability(Guid[] ids, InventoryDbContext db, CancellationToken ct)
    {
        var validation = new Validation().Require(ids.Length is >= 1 and <= 100, "ids", "Ask for 1 to 100 products.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        return TypedResults.Ok(await db.StockItems.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId))
            .Select(i => new AvailabilityResponse(i.ProductId, i.OnHand - i.Reserved))
            .ToListAsync(ct));
    }

    private static async Task<Ok<List<StockResponse>>> List(InventoryDbContext db, CancellationToken ct, string? q = null)
    {
        var query = db.StockItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(i => EF.Functions.ILike(i.Name, pattern, "\\") || EF.Functions.ILike(i.Sku, pattern, "\\"));
        }

        var items = await query.OrderBy(i => i.OnHand - i.Reserved).ThenBy(i => i.Name).Take(500).ToListAsync(ct);
        return TypedResults.Ok(items.ConvertAll(StockResponse.From));
    }

    private static async Task<Ok<StockResponse>> Count(Guid productId, CountStockRequest request, InventoryDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var item = await db.StockItems.SingleOrDefaultAsync(i => i.ProductId == productId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This product is not tracked.");
        if (item.Version != request.Version)
        {
            throw new DomainException(ErrorCodes.Conflict, "The stock changed since you read it (an order or another admin). Reload and try again.");
        }

        item.Count(request.OnHand, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.Conflict, "The stock changed at the same moment. Reload and try again.");
        }

        return TypedResults.Ok(StockResponse.From(item));
    }
}
