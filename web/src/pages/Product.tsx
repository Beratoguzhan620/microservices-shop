import { ArrowLeft, Check, Loader2, Minus, Plus, ShoppingBag } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { useShop } from "../context";
import { money } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import { StockNote } from "../components/Badges";
import { ProductArt } from "../components/ProductArt";
import { Failed, Loading } from "../components/States";

export function ProductPage({ slug }: { slug: string }) {
  const { session, setBasket, basket } = useShop();
  const [product, reload] = useLoad(() => api.product(slug), [slug]);
  const [stock] = useLoad(async () => (product.data ? (await api.availability([product.data.id]))[0]?.available : undefined), [product.data?.id]);
  const [quantity, setQuantity] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [added, setAdded] = useState(false);

  if (product.state === "loading" && !product.data) return <div className="container"><Loading /></div>;
  if (product.state === "failed" && !product.data) return <div className="container"><Failed message={product.error} onRetry={reload} /></div>;
  const p = product.data!;
  const inBasket = basket?.lines.find((l) => l.productId === p.id)?.quantity ?? 0;
  const available = stock.data;
  const soldOut = available !== undefined && available <= 0;

  async function add() {
    if (!session) {
      navigate(`/signin?next=${encodeURIComponent(location.pathname)}`);
      return;
    }

    setBusy(true);
    setError(null);
    try {
      setBasket(await api.setQuantity(p.id, Math.min(20, inBasket + quantity)));
      setAdded(true);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="container">
      <a className="back" {...linkProps(`/?category=${p.category.slug}`)}><ArrowLeft size={14} />{p.category.name}</a>
      <div className="product-page">
        <ProductArt category={p.category.slug} size={120} />
        <div className="stack">
          <div>
            <span className="small muted mono">{p.sku}</span>
            <h1>{p.name}</h1>
          </div>
          <span className="price large">{money(p.price)}</span>
          <p>{p.description}</p>
          <StockNote available={available} />
          <div className="row">
            <div className="stepper" aria-label="Quantity">
              <button className="icon-button" onClick={() => setQuantity(Math.max(1, quantity - 1))} aria-label="One less"><Minus size={15} /></button>
              <span aria-live="polite">{quantity}</span>
              <button className="icon-button" onClick={() => setQuantity(Math.min(20, quantity + 1))} aria-label="One more"><Plus size={15} /></button>
            </div>
            <button className="button primary" onClick={add} disabled={busy || soldOut}>
              {busy ? <Loader2 size={16} className="spin" /> : <ShoppingBag size={16} />}
              {soldOut ? "Sold out" : "Add to basket"}
            </button>
          </div>
          {added && (
            <p className="form-ok" role="status">
              <Check size={15} /> In your basket ({inBasket}). <a {...linkProps("/basket")}>Go to the basket</a>
            </p>
          )}
          {error && <p className="form-error" role="alert">{error}</p>}
        </div>
      </div>
    </div>
  );
}
