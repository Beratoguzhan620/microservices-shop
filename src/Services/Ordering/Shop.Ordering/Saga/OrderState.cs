using MassTransit;

namespace Shop.Ordering.Saga;

/// <summary>Where one order is in its process. Stored next to the order, in the same transaction as its changes.</summary>
public sealed class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public decimal Total { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}
