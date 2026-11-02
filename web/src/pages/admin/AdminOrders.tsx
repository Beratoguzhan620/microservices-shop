import { ChevronLeft, ChevronRight, Loader2, Package, Truck, X } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../../api";
import { dateTime, money, shortId, statusLabel } from "../../format";
import { useLoad } from "../../hooks";
import { linkProps, navigate } from "../../router";
import type { OrderStatus } from "../../types";
import { StatusBadge } from "../../components/Badges";
import { Empty, Failed, Loading } from "../../components/States";
import { OrderPage } from "../Orders";

const filters: (OrderStatus | "")[] = ["", "Confirmed", "AwaitingPayment", "Shipped", "Rejected", "Cancelled"];

export function AdminOrders() {
  const [status, setStatus] = useState<OrderStatus | "">("Confirmed");
  const [page, setPage] = useState(1);
  const [orders, reload] = useLoad(() => api.adminOrders(status || undefined, page), [status, page]);
  const data = orders.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="card">
      <div className="toolbar">
        <div className="tabs" role="tablist" aria-label="Status">
          {filters.map((f) => (
            <button key={f || "all"} role="tab" aria-selected={status === f} className={`tab${status === f ? " active" : ""}`} onClick={() => { setStatus(f); setPage(1); }}>
              {f ? statusLabel[f] : "All"}
            </button>
          ))}
        </div>
      </div>
      {orders.state === "failed" && !data && <Failed message={orders.error} onRetry={reload} />}
      {!data && orders.state === "loading" && <Loading />}
      {data?.items.length === 0 && <Empty title={status === "Confirmed" ? "Nothing to ship" : "No orders here"} icon={<Package size={24} />} />}
      {data && data.items.length > 0 && (
        <table className="table">
          <thead><tr><th>Order</th><th className="hide-sm">Customer</th><th>Status</th><th className="hide-sm">Items</th><th>Total</th><th className="hide-sm">Placed</th></tr></thead>
          <tbody>
            {data.items.map((o) => (
              <tr key={o.id} className="clickable" onClick={() => navigate(`/admin/orders/${o.id}`)}>
                <td className="mono"><a {...linkProps(`/admin/orders/${o.id}`)} onClick={(e) => e.preventDefault()}>{shortId(o.id)}</a></td>
                <td className="hide-sm">{o.customerName}</td>
                <td><StatusBadge status={o.status} /></td>
                <td className="hide-sm">{o.itemCount}</td>
                <td>{money(o.total)}</td>
                <td className="hide-sm muted small">{dateTime(o.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {data && data.total > data.pageSize && (
        <div className="pager">
          <span>Page {page} of {pages}</span>
          <div className="row">
            <button className="button small" disabled={page <= 1} onClick={() => setPage(page - 1)} aria-label="Previous page"><ChevronLeft size={15} /></button>
            <button className="button small" disabled={page >= pages} onClick={() => setPage(page + 1)} aria-label="Next page"><ChevronRight size={15} /></button>
          </div>
        </div>
      )}
    </div>
  );
}

export function AdminOrder({ id }: { id: string }) {
  const [order, reload] = useLoad(() => api.order(id), [id]);
  const [tracking, setTracking] = useState("");
  const [busy, setBusy] = useState<"ship" | "cancel" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [version, setVersion] = useState(0);
  const [from, setFrom] = useState<string | undefined>(undefined);

  async function act(kind: "ship" | "cancel", action: () => Promise<unknown>) {
    setBusy(kind);
    setError(null);
    try {
      await action();
      // The saga moves the order a moment later; the order view below follows it until it does.
      setFrom(order.data?.status);
      setVersion((v) => v + 1);
      setTimeout(reload, 1500);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  const ship = (e: FormEvent) => { e.preventDefault(); act("ship", () => api.ship(id, tracking)); };

  return (
    <>
      {order.data?.status === "Confirmed" && (
        <form className="card section ship" onSubmit={ship} aria-label="Ship">
          <h2><Truck size={17} /> Ready to ship</h2>
          <div className="row wrap">
            <label className="field grow">Tracking code<input className="input mono" value={tracking} onChange={(e) => setTracking(e.target.value)} placeholder="YK-123456789" /></label>
            <button className="button primary" disabled={busy !== null || tracking.trim().length < 4}>{busy === "ship" && <Loader2 size={15} className="spin" />}Mark as shipped</button>
            <button type="button" className="button danger" disabled={busy !== null} onClick={() => act("cancel", () => api.cancel(id, "Cancelled by the shop."))}>
              {busy === "cancel" ? <Loader2 size={15} className="spin" /> : <X size={15} />}Cancel and refund
            </button>
          </div>
          {error && <p className="form-error" role="alert">{error}</p>}
        </form>
      )}
      <div key={version}><OrderPage id={id} admin awaitChangeFrom={from} /></div>
    </>
  );
}
