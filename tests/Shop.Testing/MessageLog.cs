using System.Collections.Concurrent;
using MassTransit;
using Xunit;

namespace Shop.Testing;

/// <summary>
/// Listens on the bus like any other service would: one temporary receive endpoint per message type, bound to that
/// type's exchange. It sees every message the service under test sends out, including those that leave through
/// the transactional outbox (which reach the bus as serialized bodies, so the test harness cannot match them by
/// type).
/// </summary>
public sealed class MessageLog
{
    private readonly ConcurrentQueue<object> _messages = new();
    private readonly IBus _bus;

    private MessageLog(IBus bus) => _bus = bus;

    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(20);

    public static Task<MessageLog> StartAsync(IBus bus) => Task.FromResult(new MessageLog(bus));

    public async Task<MessageLog> ListenAsync<T>()
        where T : class
    {
        var handle = _bus.ConnectReceiveEndpoint($"test-log-{typeof(T).Name.ToLowerInvariant()}", endpoint =>
            endpoint.Handler<T>(context =>
            {
                _messages.Enqueue(context.Message);
                return Task.CompletedTask;
            }));
        await handle.Ready;
        return this;
    }

    public IEnumerable<T> All<T>(Func<T, bool> match) => _messages.OfType<T>().Where(match);

    public int Count<T>(Func<T, bool> match) => All(match).Count();

    public async Task<T> WaitAsync<T>(Func<T, bool> match)
    {
        await Messages.UntilAsync(() => All(match).Any(), $"No {typeof(T).Name} went out.");
        return All(match).Last();
    }
}
