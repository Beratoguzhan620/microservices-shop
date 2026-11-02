import { ArrowLeft, Check, Loader2, Package, X } from "lucide-react";
import { useEffect, useState } from "react";
import { api, ApiError } from "../api";
import { dateTime, isInFlight, money, progress, shortId } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";
import type { Order } from "../types";
import { StatusBadge } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";

export function Orders() {
  const [orders, reload] = useLoad(() => api.orders(), []);
  return (
    <div className="container narrow">
      <h1 className="page-title">Your orders</h1>
      {orders.state === "loading" && !orders.data && <Loading />}
      {orders.state === "failed" && !orders.data && <Failed message={orders.error} onRetry={reload} />}
      {orders.data?.length === 0 && <Empty title="No orders yet" icon={<Package size={24} />}><a {...linkProps("/")}>Start shopping</a></Empty>}
      {orders.data && orders.data.length > 0 && (
        <div className="card lines">
          {orders.data.map((o) => (
            <a key={o.id} className="line clickable" {...linkProps(`/orders/${o.id}`)}>
              <div className="grow">
                <span className="product-name">Order {shortId(o.id)}</span>
                <span className="small muted">{dateTime(o.createdAt)} · {o.itemCount} {o.itemCount === 1 ? "item" : "items"}</span>
              </div>
              <StatusBadge status={o.status} />
              <span className="line-total">{money(o.total)}</span>
            </a>
          ))}
        </div>
      )}
    </div>
  );
}

/** The order as it moves: right after checkout it may not exist yet, so the page keeps asking until it settles. */
export function OrderPage({ id, admin = false, awaitChangeFrom }: { id: string; admin?: boolean; awaitChangeFrom?: string }) {
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [cancelling, setCancelling] = useState(false);
  // Set after asking for a change, until the order shows it (the saga moves it a moment later).
  const [awaiting, setAwaiting] = useState<string | null>(awaitChangeFrom ?? null);

  useEffect(() => {
    let stopped = false;
    api.order(id).then(
      (o) => { if (!stopped) { setOrder(o); setError(null); } },
      (e: ApiError) => { if (!stopped && e.status !== 404) setError(e.message); },
    );
    return () => { stopped = true; };
  }, [id, tick]);

  // Keep following an order that is still moving on its own, or one that was just cancelled.
  const moving = isInFlight(order?.status) || (order?.status === "Cancelled" && !order.refunded && !!order.cardLast4) || (awaiting !== null && order?.status === awaiting);
  useEffect(() => {
    if (awaiting !== null && order && order.status !== awaiting) setAwaiting(null);
  }, [awaiting, order]);
  useEffect(() => {
    if (!moving) return;
    const timer = setTimeout(() => setTick((t) => t + 1), 1000);
    return () => clearTimeout(timer);
  }, [moving, tick]);

  async function cancel() {
    setCancelling(true);
    try {
      await api.cancel(id);
      setAwaiting(order?.status ?? null);
      setTick((t) => t + 1);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setCancelling(false);
    }
  }

  if (!order) {
    return (
      <div className="container narrow">
        {error ? <Failed message={error} onRetry={() => setTick((t) => t + 1)} /> : <Loading label="Placing your order" />}
      </div>
    );
  }

  const { steps, reached, failed } = progress(order.status);
  return (
    <div className="container narrow">
      <a className="back" {...linkProps(admin ? "/admin/orders" : "/orders")}><ArrowLeft size={14} />{admin ? "All orders" : "Your orders"}</a>
      <div className="row" style={{ alignItems: "flex-start", marginBottom: 16 }}>
        <div className="grow">
          <h1 className="page-title" style={{ margin: 0 }}>Order {shortId(order.id)}</h1>
          <span className="small muted">{dateTime(order.createdAt)}</span>
        </div>
        <StatusBadge status={order.status} />
      </div>

      <section className="card section" aria-label="Progress">
        {failed ? (
          <div className="outcome bad" role="status">
            <X size={18} />
            <div>
              <strong>{order.status === "Cancelled" ? "Cancelled" : "The order could not be placed"}</strong>
              <p>{order.reason}</p>
              {order.refunded && <p className="small">The payment was refunded to the card ending {order.cardLast4 ?? "on file"}.</p>}
              {order.status === "Cancelled" && !order.refunded && order.cardLast4 && <p className="small muted">Refunding the payment…</p>}
              {order.status === "Rejected" && !order.cardLast4 && <p className="small muted">Nothing was charged, and the stock held for it is available again.</p>}
            </div>
          </div>
        ) : (
          <ol className="timeline">
            {steps.map((step, i) => (
              <li key={step} className={i < reached ? "done" : i === reached ? (isInFlight(order.status) ? "current" : "done") : ""}>
                <span className="marker">{i < reached || (i === reached && !isInFlight(order.status)) ? <Check size={13} /> : i === reached ? <Loader2 size={13} className="spin" /> : null}</span>
                {step}
              </li>
            ))}
          </ol>
        )}
        {order.status === "Shipped" && order.trackingCode && <p className="small">Tracking code: <span className="mono">{order.trackingCode}</span></p>}
        {order.cardLast4 && order.status !== "Rejected" && <p className="small muted">Paid with the card ending {order.cardLast4}.</p>}
      </section>

      <section className="card section" aria-label="Items">
        {order.items.map((i) => (
          <div key={i.productId} className="row">
            <span className="grow">{i.quantity} × {i.name}</span>
            <span>{money(i.total)}</span>
          </div>
        ))}
        <div className="row total-row"><span className="grow">Total</span><strong>{money(order.total)}</strong></div>
        <p className="small muted">
          To {order.shippingAddress.fullName}, {order.shippingAddress.line1}, {order.shippingAddress.postalCode} {order.shippingAddress.city}
        </p>
      </section>

      {error && <p className="form-error" role="alert">{error}</p>}
      {order.canCancel && !admin && (
        <button className="button danger" onClick={cancel} disabled={cancelling}>
          {cancelling && <Loader2 size={15} className="spin" />}Cancel the order
        </button>
      )}
    </div>
  );
}
