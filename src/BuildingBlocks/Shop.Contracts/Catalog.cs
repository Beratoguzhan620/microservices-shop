namespace Shop.Contracts.Catalog;

/// <summary>A product was added to the catalog; Inventory starts tracking its stock.</summary>
public sealed record ProductCreated(Guid ProductId, string Sku, string Name, decimal Price, DateTimeOffset At);
