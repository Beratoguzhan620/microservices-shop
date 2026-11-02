using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shop.Catalog.Data;
using Shop.Catalog.Products;
using Shop.Contracts.Catalog;
using Shop.ServiceDefaults;
using Shop.ServiceDefaults.Auth;
using Shop.ServiceDefaults.Errors;

namespace Shop.Catalog.Features;

public sealed record CategoryResponse(Guid Id, string Name, string Slug, int ProductCount);

public sealed record ProductResponse(
    Guid Id, string Sku, string Name, string Slug, string Description, decimal Price, string Currency,
    CategoryRef Category, bool IsActive, DateTimeOffset UpdatedAt, uint Version);

public sealed record CategoryRef(Guid Id, string Name, string Slug);

public sealed record ProductPage(IReadOnlyList<ProductResponse> Items, int Total, int Page, int PageSize);

public sealed record CreateCategoryRequest(string Name);

public sealed record CreateProductRequest(string Sku, string Name, string? Description, decimal Price, Guid CategoryId);

/// <summary><c>Version</c> is the one read last; a newer change on the server makes the update a 409.</summary>
public sealed record UpdateProductRequest(string Name, string? Description, decimal Price, Guid CategoryId, uint Version);

/// <summary>What other services need to price a basket: the current price, and whether it is still sold.</summary>
public sealed record ProductSnapshot(Guid Id, string Sku, string Name, decimal Price, bool IsActive);

public enum ProductSort
{
    Newest,
    PriceAsc,
    PriceDesc,
    Name,
}

internal static class CatalogEndpoints
{
    public const string Currency = "TRY";
    public const int MaxPageSize = 60;

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var shop = app.MapGroup("/api/catalog").WithTags("Catalog");
        shop.MapGet("/categories", Categories).WithSummary("Categories with their number of products on sale");
        shop.MapGet("/products", Search).WithSummary("Search products")
            .WithDescription("Filter by text, category slug and price; sort by `Newest`, `PriceAsc`, `PriceDesc` or `Name`. Archived products are only listed for admins who ask with `includeArchived=true`.");
        shop.MapGet("/products/{slug}", BySlug).WithSummary("One product by its slug");
        shop.MapGet("/products/snapshots", Snapshots).WithSummary("Current prices of several products")
            .WithDescription("For the basket: up to 50 ids (`ids=a&ids=b`); archived products are returned with `isActive: false`, unknown ids are left out.");

        var admin = app.MapGroup("/api/catalog").WithTags("Catalog administration").RequireAuthorization(AuthDefaults.AdminPolicy);
        admin.MapPost("/categories", CreateCategory).WithSummary("Add a category (admin)");
        admin.MapPost("/products", CreateProduct).WithSummary("Add a product (admin)");
        admin.MapPut("/products/{id:guid}", UpdateProduct).WithSummary("Change a product (admin)")
            .WithDescription("Send the `version` you read; if someone changed the product since, the answer is 409 and nothing is overwritten.");
        admin.MapPost("/products/{id:guid}/archive", Archive).WithSummary("Take a product off sale (admin)");
        admin.MapPost("/products/{id:guid}/restore", Restore).WithSummary("Put an archived product back on sale (admin)");
        return app;
    }

    private static async Task<Ok<List<CategoryResponse>>> Categories(CatalogDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(c.Id, c.Name, c.Slug, db.Products.Count(p => p.CategoryId == c.Id && p.IsActive)))
            .ToListAsync(ct));

    private static async Task<Results<Ok<ProductPage>, ValidationProblem>> Search(
        CatalogDbContext db, ClaimsPrincipal user, CancellationToken ct,
        string? q = null, string? category = null, decimal? minPrice = null, decimal? maxPrice = null,
        ProductSort sort = ProductSort.Newest, bool includeArchived = false, int page = 1, int pageSize = 24)
    {
        var validation = new Validation()
            .Require(page >= 1, "page", "Pages start at 1.")
            .Require(pageSize is >= 1 and <= MaxPageSize, "pageSize", $"A page has 1 to {MaxPageSize} products.")
            .Require(minPrice is null || maxPrice is null || minPrice <= maxPrice, "maxPrice", "The highest price is below the lowest.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.Products.AsNoTracking();
        if (!(includeArchived && user.IsInRole(ShopRoles.Admin)))
        {
            query = query.Where(p => p.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern, "\\") || EF.Functions.ILike(p.Description, pattern, "\\") || p.Sku == q.Trim().ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(p => db.Categories.Any(c => c.Id == p.CategoryId && c.Slug == category));
        }

        if (minPrice is { } min)
        {
            query = query.Where(p => p.Price >= min);
        }

        if (maxPrice is { } max)
        {
            query = query.Where(p => p.Price <= max);
        }

        query = sort switch
        {
            ProductSort.PriceAsc => query.OrderBy(p => p.Price).ThenBy(p => p.Name),
            ProductSort.PriceDesc => query.OrderByDescending(p => p.Price).ThenBy(p => p.Name),
            ProductSort.Name => query.OrderBy(p => p.Name),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name),
        };

        var total = await query.CountAsync(ct);
        var items = await Project(db, query.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return TypedResults.Ok(new ProductPage(items, total, page, pageSize));
    }

    private static async Task<Results<Ok<List<ProductSnapshot>>, ValidationProblem>> Snapshots(Guid[] ids, CatalogDbContext db, CancellationToken ct)
    {
        var validation = new Validation().Require(ids.Length is >= 1 and <= 50, "ids", "Ask for 1 to 50 products.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        return TypedResults.Ok(await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductSnapshot(p.Id, p.Sku, p.Name, p.Price, p.IsActive))
            .ToListAsync(ct));
    }

    private static async Task<Ok<ProductResponse>> BySlug(string slug, CatalogDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var admin = user.IsInRole(ShopRoles.Admin);
        var product = await Project(db, db.Products.AsNoTracking().Where(p => p.Slug == slug && (p.IsActive || admin))).SingleOrDefaultAsync(ct);
        return TypedResults.Ok(product ?? throw new DomainException(ErrorCodes.NotFound, "There is no such product."));
    }

    private static async Task<Created<CategoryResponse>> CreateCategory(CreateCategoryRequest request, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var category = Category.Create(request.Name, clock.GetUtcNow());
        db.Categories.Add(category);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.Conflict, "A category with this name already exists.");
        }

        return TypedResults.Created($"/api/catalog/categories", new CategoryResponse(category.Id, category.Name, category.Slug, 0));
    }

    private static async Task<Created<ProductResponse>> CreateProduct(
        CreateProductRequest request, CatalogDbContext db, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        await EnsureCategoryAsync(db, request.CategoryId, ct);
        var product = Product.Create(request.Sku, request.Name, request.Description ?? string.Empty, request.Price, request.CategoryId, clock.GetUtcNow());
        if (await db.Products.AnyAsync(p => p.Slug == product.Slug, ct))
        {
            product.DisambiguateSlug();
        }

        db.Products.Add(product);

        // Written to the outbox with the product, so Inventory hears about every product that exists, and only those.
        await publish.Publish(new ProductCreated(product.Id, product.Sku, product.Name, product.Price, product.CreatedAt), ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation("ix_products_sku"))
        {
            throw new DomainException(ErrorCodes.Conflict, "Another product has this SKU.");
        }

        return TypedResults.Created($"/api/catalog/products/{product.Slug}", await LoadAsync(db, product.Id, ct));
    }

    private static async Task<Ok<ProductResponse>> UpdateProduct(
        Guid id, UpdateProductRequest request, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var product = await FindAsync(db, id, ct);
        if (product.Version != request.Version)
        {
            throw new DomainException(ErrorCodes.Conflict, "Someone changed this product since you opened it. Reload it and try again.");
        }

        await EnsureCategoryAsync(db, request.CategoryId, ct);
        var slugBefore = product.Slug;
        product.Update(request.Name, request.Description ?? string.Empty, request.Price, request.CategoryId, clock.GetUtcNow());
        if (product.Slug != slugBefore && await db.Products.AnyAsync(p => p.Slug == product.Slug && p.Id != id, ct))
        {
            product.DisambiguateSlug();
        }

        await SaveAsync(db, ct);
        return TypedResults.Ok(await LoadAsync(db, id, ct));
    }

    private static async Task<Ok<ProductResponse>> Archive(Guid id, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        (await FindAsync(db, id, ct)).Archive(clock.GetUtcNow());
        await SaveAsync(db, ct);
        return TypedResults.Ok(await LoadAsync(db, id, ct));
    }

    private static async Task<Ok<ProductResponse>> Restore(Guid id, CatalogDbContext db, TimeProvider clock, CancellationToken ct)
    {
        (await FindAsync(db, id, ct)).Restore(clock.GetUtcNow());
        await SaveAsync(db, ct);
        return TypedResults.Ok(await LoadAsync(db, id, ct));
    }

    private static async Task SaveAsync(CatalogDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.Conflict, "Someone changed this product at the same moment. Reload it and try again.");
        }
    }

    private static async Task EnsureCategoryAsync(CatalogDbContext db, Guid categoryId, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, ct))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "There is no such category.");
        }
    }

    private static async Task<Product> FindAsync(CatalogDbContext db, Guid id, CancellationToken ct) =>
        await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no such product.");

    private static Task<ProductResponse> LoadAsync(CatalogDbContext db, Guid id, CancellationToken ct) =>
        Project(db, db.Products.AsNoTracking().Where(p => p.Id == id)).SingleAsync(ct);

    internal static IQueryable<ProductResponse> Project(CatalogDbContext db, IQueryable<Product> products) =>
        from p in products
        join c in db.Categories on p.CategoryId equals c.Id
        select new ProductResponse(p.Id, p.Sku, p.Name, p.Slug, p.Description, p.Price, Currency,
            new CategoryRef(c.Id, c.Name, c.Slug), p.IsActive, p.UpdatedAt, p.Version);
}
