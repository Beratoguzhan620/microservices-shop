using System.Text.RegularExpressions;
using Shop.ServiceDefaults.Errors;

namespace Shop.Catalog.Products;

public sealed class Category
{
    private Category()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public static Category Create(string name, DateTimeOffset now)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 2 or > 60)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A category name has 2 to 60 characters.");
        }

        return new Category { Id = Guid.CreateVersion7(now), Name = trimmed, Slug = Slugs.From(trimmed) };
    }
}

/// <summary>
/// Something the shop sells. Prices are in Turkish lira with two decimals. Products are archived rather than
/// deleted, because past orders keep pointing at them. <see cref="Version"/> is PostgreSQL's <c>xmin</c>: an
/// admin who saves an outdated copy gets a conflict instead of silently overwriting someone else's change.
/// </summary>
public sealed partial class Product
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const decimal MaxPrice = 1_000_000m;

    private Product()
    {
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Slug { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public Guid CategoryId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Product Create(string sku, string name, string description, decimal price, Guid categoryId, DateTimeOffset now)
    {
        var product = new Product
        {
            Id = Guid.CreateVersion7(now),
            Sku = CheckSku(sku),
            IsActive = true,
            CreatedAt = now,
        };
        product.Update(name, description, price, categoryId, now);
        return product;
    }

    /// <returns>Whether the price changed, which the basket and checkout need to know.</returns>
    public bool Update(string name, string description, decimal price, Guid categoryId, DateTimeOffset now)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 2 or > NameMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A product name has 2 to {NameMaxLength} characters.");
        }

        var text = (description ?? string.Empty).Trim();
        if (text.Length > DescriptionMaxLength)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A description has at most {DescriptionMaxLength} characters.");
        }

        if (price <= 0 || price > MaxPrice || decimal.Round(price, 2) != price)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A price is more than 0, at most 1,000,000 and has at most two decimals.");
        }

        var priceChanged = Price != price;
        if (Name != trimmed)
        {
            Slug = Slugs.From(trimmed);
        }

        Name = trimmed;
        Description = text;
        Price = price;
        CategoryId = categoryId;
        UpdatedAt = now;
        return priceChanged;
    }

    /// <summary>Makes the slug unique by adding the SKU, when another product already has the name.</summary>
    public void DisambiguateSlug() => Slug = $"{Slugs.From(Name)}-{Sku.ToLowerInvariant()}";

    public void Archive(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public static string CheckSku(string? sku)
    {
        var normalized = (sku ?? string.Empty).Trim().ToUpperInvariant();
        return SkuPattern().IsMatch(normalized)
            ? normalized
            : throw new DomainException(ErrorCodes.ValidationFailed, "A SKU has 3 to 32 letters, digits or dashes, like 'KHV-001'.");
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{1,30}[A-Z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
