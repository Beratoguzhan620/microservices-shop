namespace Shop.Contracts.Inventory;

public sealed record StockLine(Guid ProductId, int Quantity);

/// <summary>Hold these quantities for the order, all of them or none.</summary>
public sealed record ReserveStock(Guid OrderId, IReadOnlyList<StockLine> Lines);

public sealed record StockReserved(Guid OrderId, DateTimeOffset At);

public sealed record StockReservationFailed(Guid OrderId, string Reason, IReadOnlyList<Guid> UnavailableProductIds, DateTimeOffset At);

/// <summary>The order went through: the held quantities leave the warehouse for good.</summary>
public sealed record CommitStock(Guid OrderId);

/// <summary>The order did not go through: whatever was held for it is available again.</summary>
public sealed record ReleaseStock(Guid OrderId, string Reason);

public sealed record StockReleased(Guid OrderId, DateTimeOffset At);
