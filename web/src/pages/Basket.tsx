import { Minus, Plus, ShoppingBag, Trash2 } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { useShop } from "../context";
import { money } from "../format";
import { linkProps } from "../router";
import { Empty, Loading } from "../components/States";

export function BasketPage() {
  const { basket, setBasket } = useShop();
  const [error, setError] = useState<string | null>(null);

  async function set(productId: string, quantity: number) {
    setError(null);
    try {
      setBasket(await api.setQuantity(productId, quantity));
    } catch (e) {
      setError((e as Error).message);
    }
  }

  if (!basket) return <div className="container"><Loading label="Loading your basket" /></div>;

  return (
    <div className="container narrow">
      <h1 className="page-title">Basket</h1>
      {basket.lines.length === 0 ? (
        <Empty title="Your basket is empty" icon={<ShoppingBag size={24} />}>
          <a {...linkProps("/")}>Browse the shop</a>
        </Empty>
      ) : (
        <>
          <div className="card lines">
            {basket.lines.map((line) => (
              <div key={line.productId} className="line">
                <div className="grow">
                  <span className="product-name">{line.name}</span>
                  <span className="small muted">{money(line.unitPrice)} each</span>
                </div>
                <div className="stepper" aria-label={`Quantity of ${line.name}`}>
                  <button className="icon-button" onClick={() => set(line.productId, line.quantity - 1)} aria-label={`One less ${line.name}`}><Minus size={15} /></button>
                  <span>{line.quantity}</span>
                  <button className="icon-button" onClick={() => set(line.productId, line.quantity + 1)} disabled={line.quantity >= 20} aria-label={`One more ${line.name}`}><Plus size={15} /></button>
                </div>
                <span className="line-total">{money(line.total)}</span>
                <button className="icon-button" onClick={() => set(line.productId, 0)} aria-label={`Remove ${line.name}`}><Trash2 size={16} /></button>
              </div>
            ))}
          </div>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="summary">
            <span className="muted">{basket.count} {basket.count === 1 ? "item" : "items"}</span>
            <span className="total">Total <strong>{money(basket.total)}</strong></span>
            <a className="button primary" {...linkProps("/checkout")}>Checkout</a>
          </div>
          <p className="small muted">Prices are checked again when you pay; if one changed, you see it before anything is ordered.</p>
        </>
      )}
    </div>
  );
}
