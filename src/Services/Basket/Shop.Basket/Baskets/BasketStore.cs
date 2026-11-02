using System.Text.Json;
using StackExchange.Redis;

namespace Shop.Basket.Baskets;

/// <summary>Baskets in Redis, one JSON value per customer, expiring after 30 days without changes.</summary>
public sealed class BasketStore(IConnectionMultiplexer redis)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static RedisKey Key(Guid customerId) => $"basket:{customerId:N}";

    public async Task<CustomerBasket?> GetAsync(Guid customerId)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(customerId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CustomerBasket>(value.ToString(), Json);
    }

    public Task SaveAsync(CustomerBasket basket) =>
        redis.GetDatabase().StringSetAsync(Key(basket.CustomerId), JsonSerializer.Serialize(basket, Json), Lifetime);

    public Task DeleteAsync(Guid customerId) => redis.GetDatabase().KeyDeleteAsync(Key(customerId));

    /// <summary>
    /// Takes the basket out of Redis in one step (<c>GETDEL</c>). Of two checkouts at the same moment, only one gets
    /// the basket; the other finds it empty.
    /// </summary>
    public async Task<CustomerBasket?> TakeAsync(Guid customerId)
    {
        var value = await redis.GetDatabase().StringGetDeleteAsync(Key(customerId));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<CustomerBasket>(value.ToString(), Json);
    }
}
