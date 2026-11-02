using System.Net.Http.Json;
using Shop.ServiceDefaults.Errors;

namespace Shop.Basket.Baskets;

public sealed record ProductSnapshot(Guid Id, string Sku, string Name, decimal Price, bool IsActive);

/// <summary>Reads current prices from the Catalog service over HTTP, with retries and a circuit breaker.</summary>
public sealed class CatalogClient(HttpClient http)
{
    public async Task<IReadOnlyDictionary<Guid, ProductSnapshot>> SnapshotsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return new Dictionary<Guid, ProductSnapshot>();
        }

        var query = string.Join('&', list.Select(id => $"ids={id}"));
        try
        {
            var products = await http.GetFromJsonAsync<List<ProductSnapshot>>($"/api/catalog/products/snapshots?{query}", ct) ?? [];
            return products.ToDictionary(p => p.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or Polly.ExecutionRejectedException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new DomainException(ErrorCodes.Unavailable, "Prices cannot be checked right now. Try again in a moment.");
        }
    }
}
