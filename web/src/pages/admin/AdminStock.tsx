import { Check, Loader2, Search } from "lucide-react";
import { useState } from "react";
import { api } from "../../api";
import { useDebounced, useLoad } from "../../hooks";
import type { StockItem } from "../../types";
import { Empty, Failed, Loading } from "../../components/States";

function CountCell({ item, onSaved }: { item: StockItem; onSaved: (item: StockItem) => void }) {
  const [value, setValue] = useState(String(item.onHand));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const changed = value !== String(item.onHand);

  async function save() {
    setBusy(true);
    setError(null);
    try {
      onSaved(await api.count(item.productId, Number(value), item.version));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="row">
        <input className="input count" value={value} onChange={(e) => setValue(e.target.value.replace(/\D/g, ""))} inputMode="numeric" aria-label={`Stock of ${item.name}`} />
        {changed && (
          <button className="icon-button" onClick={save} disabled={busy} aria-label={`Save stock of ${item.name}`}>
            {busy ? <Loader2 size={15} className="spin" /> : <Check size={15} />}
          </button>
        )}
      </div>
      {error && <span className="field-error">{error}</span>}
    </div>
  );
}

export function AdminStock() {
  const [text, setText] = useState("");
  const q = useDebounced(text);
  const [stock, reload, setStock] = useLoad(() => api.stock(q), [q]);

  return (
    <div className="card">
      <div className="toolbar">
        <label className="search">
          <Search size={15} />
          <span className="sr-only">Search stock</span>
          <input className="input" placeholder="Name or SKU" value={text} onChange={(e) => setText(e.target.value)} />
        </label>
        <span className="small muted">Held stock belongs to orders still going through; it comes back if they do not.</span>
      </div>
      {stock.state === "failed" && !stock.data && <Failed message={stock.error} onRetry={reload} />}
      {!stock.data && <Loading />}
      {stock.data?.length === 0 && <Empty title="No products are tracked yet" />}
      {stock.data && stock.data.length > 0 && (
        <table className="table">
          <thead><tr><th>Product</th><th>On hand</th><th>Held</th><th>Available</th></tr></thead>
          <tbody>
            {stock.data.map((item) => (
              <tr key={item.productId}>
                <td><span className="mono small muted">{item.sku}</span><br />{item.name}</td>
                <td><CountCell key={item.version} item={item} onSaved={(saved) => setStock(stock.data!.map((s) => (s.productId === saved.productId ? saved : s)))} /></td>
                <td>{item.reserved}</td>
                <td><strong className={item.available <= 0 ? "danger-text" : item.available <= 5 ? "warn-text" : ""}>{item.available}</strong></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
