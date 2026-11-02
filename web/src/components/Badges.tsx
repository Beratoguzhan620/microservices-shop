import { statusLabel } from "../format";
import type { OrderStatus } from "../types";

export function StatusBadge({ status }: { status: OrderStatus }) {
  return (
    <span className={`badge status-${status}`}>
      <span className="dot" />
      {statusLabel[status]}
    </span>
  );
}

/** "In stock", "Only 3 left" or "Sold out", from what can still be ordered. */
export function StockNote({ available }: { available: number | undefined }) {
  if (available === undefined) return null;
  if (available <= 0) return <span className="stock out">Sold out</span>;
  if (available <= 5) return <span className="stock low">Only {available} left</span>;
  return <span className="stock">In stock</span>;
}
