import { Archive, ArchiveRestore, Loader2, Pencil, Plus, Search } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../../api";
import { money } from "../../format";
import { useDebounced, useLoad } from "../../hooks";
import type { Category, Product } from "../../types";
import { Dialog } from "../../components/Dialog";
import { Empty, Failed, Loading } from "../../components/States";

function ProductDialog({ product, categories, onClose, onSaved }: { product: Product | null; categories: Category[]; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    sku: product?.sku ?? "",
    name: product?.name ?? "",
    description: product?.description ?? "",
    price: product ? String(product.price) : "",
    categoryId: product?.category.id ?? categories[0]?.id ?? "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const body = { name: form.name, description: form.description, price: Number(form.price.replace(",", ".")), categoryId: form.categoryId };
    try {
      if (product) await api.updateProduct(product.id, { ...body, version: product.version });
      else await api.createProduct({ ...body, sku: form.sku });
      onSaved();
      onClose();
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title={product ? `Edit ${product.name}` : "New product"} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <div className="two">
          <label className="field">SKU<input className="input mono" value={form.sku} onChange={set("sku")} disabled={!!product} placeholder="KHV-010" /></label>
          <label className="field">Price (₺)<input className="input" value={form.price} onChange={set("price")} inputMode="decimal" /></label>
        </div>
        <label className="field">Name<input className="input" value={form.name} onChange={set("name")} /></label>
        <label className="field">Category
          <select className="select" value={form.categoryId} onChange={set("categoryId")}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        <label className="field">Description<textarea className="textarea" value={form.description} onChange={set("description")} rows={3} /></label>
        {error && <p className="form-error" role="alert">{error.message}</p>}
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onClose}>Cancel</button>
          <button className="button primary" disabled={busy}>{busy && <Loader2 size={15} className="spin" />}Save</button>
        </div>
      </form>
    </Dialog>
  );
}

export function AdminProducts() {
  const [text, setText] = useState("");
  const q = useDebounced(text);
  const [products, reload] = useLoad(() => api.products({ q, includeArchived: true, pageSize: 60, sort: "Name" }), [q]);
  const [categories] = useLoad(() => api.categories(), []);
  const [editing, setEditing] = useState<Product | null | undefined>(undefined);
  const [error, setError] = useState<string | null>(null);

  async function toggle(p: Product) {
    setError(null);
    try {
      if (p.isActive) await api.archive(p.id);
      else await api.restore(p.id);
      reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="card">
      <div className="toolbar">
        <label className="search">
          <Search size={15} />
          <span className="sr-only">Search products</span>
          <input className="input" placeholder="Name or SKU" value={text} onChange={(e) => setText(e.target.value)} />
        </label>
        <span className="grow" />
        <button className="button primary" onClick={() => setEditing(null)} disabled={!categories.data}><Plus size={15} />New product</button>
      </div>
      {error && <p className="form-error" role="alert" style={{ margin: 12 }}>{error}</p>}
      {products.state === "failed" && !products.data && <Failed message={products.error} onRetry={reload} />}
      {!products.data && <Loading />}
      {products.data?.items.length === 0 && <Empty title="No products match" />}
      {products.data && products.data.items.length > 0 && (
        <table className="table">
          <thead><tr><th>SKU</th><th>Name</th><th className="hide-sm">Category</th><th>Price</th><th /></tr></thead>
          <tbody>
            {products.data.items.map((p) => (
              <tr key={p.id} className={p.isActive ? "" : "archived"}>
                <td className="mono">{p.sku}</td>
                <td>{p.name}{!p.isActive && <span className="badge plain" style={{ marginLeft: 6 }}>Archived</span>}</td>
                <td className="hide-sm">{p.category.name}</td>
                <td>{money(p.price)}</td>
                <td style={{ textAlign: "right", whiteSpace: "nowrap" }}>
                  <button className="icon-button" onClick={() => setEditing(p)} aria-label={`Edit ${p.name}`}><Pencil size={15} /></button>
                  <button className="icon-button" onClick={() => toggle(p)} aria-label={`${p.isActive ? "Archive" : "Restore"} ${p.name}`}>
                    {p.isActive ? <Archive size={15} /> : <ArchiveRestore size={15} />}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editing !== undefined && categories.data && (
        <ProductDialog product={editing} categories={categories.data} onClose={() => setEditing(undefined)} onSaved={reload} />
      )}
    </div>
  );
}
