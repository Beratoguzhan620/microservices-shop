using MassTransit;
using MassTransit.Testing;
using Xunit;

namespace Shop.Testing;

/// <summary>
/// Waits for messages by polling, with a deadline. The harness's own "wait until idle" can return too early here:
/// messages published through the outbox are sent a moment after the consumer finishes, and in between the bus
/// looks idle.
/// </summary>
public static class Messages
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<T> PublishedAsync<T>(this ITestHarness harness, Func<T, bool> match)
        where T : class
    {
        await UntilAsync(() => harness.CountPublished(match) > 0, $"No {typeof(T).Name} was published.");
        return harness.Published.Select<T>(m => match(m.Context.Message)).Last().Context.Message;
    }

    public static Task ConsumedAsync<T>(this ITestHarness harness, Func<T, bool> match)
        where T : class =>
        UntilAsync(() => harness.Consumed.Select<T>(m => match(m.Context.Message)).Any(), $"No {typeof(T).Name} was consumed.");

    public static int CountPublished<T>(this ITestHarness harness, Func<T, bool> match)
        where T : class => harness.Published.Select<T>(m => match(m.Context.Message)).Count();

    public static async Task UntilAsync(Func<bool> condition, string failure, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? Timeout);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, failure);
            await Task.Delay(50);
        }
    }
}
