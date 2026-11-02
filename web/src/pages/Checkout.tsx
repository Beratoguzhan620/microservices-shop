import { CreditCard, Loader2, Lock } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useShop } from "../context";
import { formatCard, money, parseExpiry, testCards } from "../format";
import { linkProps, navigate } from "../router";
import type { Address, Basket } from "../types";
import { Empty, Loading } from "../components/States";

const blank: Address = { fullName: "", line1: "", city: "", postalCode: "", phone: "" };

export function Checkout() {
  const { session, basket, setBasket } = useShop();
  const [address, setAddress] = useState<Address>({ ...blank, fullName: session?.user.displayName ?? "" });
  const [card, setCard] = useState({ number: "", expiry: "", cvc: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [changes, setChanges] = useState<string[] | null>(null);

  if (!basket) return <div className="container"><Loading /></div>;
  if (basket.lines.length === 0) {
    return (
      <div className="container narrow">
        <Empty title="Nothing to check out"><a {...linkProps("/")}>Browse the shop</a></Empty>
      </div>
    );
  }

  const set = (key: keyof Address) => (e: { target: { value: string } }) => setAddress((a) => ({ ...a, [key]: e.target.value }));
  const fieldError = (key: string) => error?.fields[key]?.[0] ?? error?.fields[`address.${key}`]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    const expiry = parseExpiry(card.expiry);
    if (!expiry) {
      setError(new ApiError("Write the expiry date as MM/YY.", 400, "validation_failed", { expiry: ["Write the expiry date as MM/YY."] }));
      return;
    }

    setBusy(true);
    setError(null);
    try {
      // The card goes to the payment service only; checkout carries a one-time token.
      const token = await api.addCard(card.number, expiry[0], expiry[1], card.cvc);
      const { orderId } = await api.checkout(address, basket!.total, token.token);
      setBasket({ ...basket!, lines: [], count: 0, total: 0 });
      navigate(`/orders/${orderId}`, true);
    } catch (err) {
      const problem = err as ApiError;
      if (problem.code === "basket_changed" && problem.body?.basket) {
        // Prices moved since the basket was filled: show the new total and let the customer confirm it.
        setBasket(problem.body.basket as Basket);
        setChanges((problem.body.changes as string[]) ?? []);
      } else {
        setError(problem);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="container">
      <h1 className="page-title">Checkout</h1>
      <form className="checkout" onSubmit={submit} noValidate>
        <div className="stack">
          <section className="card section" aria-label="Delivery details">
            <h2>Delivery</h2>
            <label className="field">Full name<input className="input" value={address.fullName} onChange={set("fullName")} autoComplete="name" />{fieldError("fullName") && <span className="field-error">{fieldError("fullName")}</span>}</label>
            <label className="field">Address<input className="input" value={address.line1} onChange={set("line1")} autoComplete="street-address" placeholder="Atatürk Cad. No: 24/5" />{fieldError("line1") && <span className="field-error">{fieldError("line1")}</span>}</label>
            <div className="two">
              <label className="field">City<input className="input" value={address.city} onChange={set("city")} autoComplete="address-level2" />{fieldError("city") && <span className="field-error">{fieldError("city")}</span>}</label>
              <label className="field">Postal code<input className="input" value={address.postalCode} onChange={set("postalCode")} inputMode="numeric" maxLength={5} autoComplete="postal-code" />{fieldError("postalCode") && <span className="field-error">{fieldError("postalCode")}</span>}</label>
            </div>
            <label className="field">Phone<input className="input" value={address.phone} onChange={set("phone")} inputMode="tel" autoComplete="tel" placeholder="0555 123 45 67" />{fieldError("phone") && <span className="field-error">{fieldError("phone")}</span>}</label>
          </section>

          <section className="card section" aria-label="Payment">
            <h2><CreditCard size={17} /> Payment</h2>
            <label className="field">Card number<input className="input mono" value={card.number} onChange={(e) => setCard({ ...card, number: formatCard(e.target.value) })} inputMode="numeric" autoComplete="cc-number" placeholder="4242 4242 4242 4242" /></label>
            <div className="two">
              <label className="field">Expiry<input className="input mono" value={card.expiry} onChange={(e) => setCard({ ...card, expiry: e.target.value })} placeholder="MM/YY" autoComplete="cc-exp" />{error?.fields.expiry && <span className="field-error">{error.fields.expiry[0]}</span>}</label>
              <label className="field">CVC<input className="input mono" value={card.cvc} onChange={(e) => setCard({ ...card, cvc: e.target.value.replace(/\D/g, "").slice(0, 4) })} inputMode="numeric" autoComplete="cc-csc" /></label>
            </div>
            <details className="small muted">
              <summary>Test cards</summary>
              <ul className="test-cards">
                {testCards.map((t) => (
                  <li key={t.number}><button type="button" className="link mono" onClick={() => setCard({ number: t.number, expiry: "12/30", cvc: "123" })}>{t.number}</button> {t.outcome}</li>
                ))}
              </ul>
            </details>
          </section>
        </div>

        <aside className="card section order-summary" aria-label="Order summary">
          <h2>Your order</h2>
          {basket.lines.map((l) => (
            <div key={l.productId} className="row small">
              <span className="grow">{l.quantity} × {l.name}</span>
              <span>{money(l.total)}</span>
            </div>
          ))}
          <div className="row total-row"><span className="grow">Total</span><strong>{money(basket.total)}</strong></div>
          {changes && (
            <div className="notice" role="alert">
              <strong>Prices changed since you added these.</strong>
              <ul>{changes.map((c) => <li key={c}>{c}</li>)}</ul>
              <span>The total above is the new one. Place the order again to accept it.</span>
            </div>
          )}
          {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
          {error && Object.keys(error.fields).length > 0 && <p className="form-error" role="alert">Check the highlighted fields.</p>}
          <button className="button primary wide" disabled={busy}>
            {busy ? <Loader2 size={16} className="spin" /> : <Lock size={15} />}Pay {money(basket.total)}
          </button>
          <p className="small muted">Stock is held for you and the card charged in a moment; you will see each step.</p>
        </aside>
      </form>
    </div>
  );
}
